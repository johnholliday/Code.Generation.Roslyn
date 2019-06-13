﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Code.Generation.Roslyn
{
    using Microsoft.Extensions.DependencyModel;
    using Microsoft.Extensions.DependencyModel.Resolution;
    using Validation;
    using static AssemblyLoadContext;
    using static Directory;
    using static Path;
    using static Resources;
    using static Strings;
    using static SearchOption;
    using static StringComparison;
    using StringBinaryPredicate = BinaryPredicate<string>;

    // TODO: TBD: might even call it Manager of the underlying Descriptor Set...
    public class AssemblyReferenceServiceManager : ServiceManager<AssemblyDescriptor, AssemblyDescriptorRegistry>
    {
        /// <summary>
        /// Currently &quot;.dll&quot;.
        /// </summary>
        private static HashSet<string> AllowedAssemblyExtensions { get; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {AllowedAssemblyExtensionDll};

        /// <summary>
        /// Gets or Sets the <see cref="DependencyContext"/> throughout the Compilation.
        /// Defaults to <see cref="DependencyContext.Default"/>.
        /// </summary>
        /// <see cref="DependencyContext.Default"/>
        private DependencyContext Context { get; set; } = DependencyContext.Default;

        /// <summary>
        /// Gets or Sets the Resolver.
        /// </summary>
        private CompositeCompilationAssemblyResolver Resolver { get; set; }

        /// <summary>
        /// Gets a registry of <see cref="Assembly"/> instances by <see cref="string"/> Assembly Name.
        /// </summary>
        private Dictionary<string, Assembly> AssembliesByName { get; } = new Dictionary<string, Assembly>();

        // TODO: TBD: not sure what the purpose behind this is/was to be honest...
        private HashSet<string> DirectoryPathsWithResolver { get; } = new HashSet<string>();

        /// <summary>
        /// Gets the list of Assembly Reference Paths.
        /// </summary>
        internal IReadOnlyList<string> ReferencePath { get; }

        /// <summary>
        /// Gets the Directory Paths in which to Search for Generator Assemblies.
        /// </summary>
        private IReadOnlyList<string> SearchPaths { get; }

        /// <summary>
        /// Internal Constructor.
        /// </summary>
        /// <param name="outputDirectory">The Output Directory.</param>
        /// <param name="registryFileName">The Registry File Name.</param>
        /// <param name="referencePath">The Assembly Reference Path.</param>
        /// <param name="searchPaths">The Assembly Search Paths.</param>
        /// <inheritdoc />
        internal AssemblyReferenceServiceManager(string outputDirectory, string registryFileName
        , IReadOnlyList<string> referencePath, IReadOnlyList<string> searchPaths)
            : base(outputDirectory, registryFileName)
        {
            Verify.Operation(referencePath != null, FormatVerifyOperationMessage(nameof(referencePath)));
            Verify.Operation(searchPaths != null, FormatVerifyOperationMessage(nameof(searchPaths)));

            ReferencePath = referencePath;
            SearchPaths = searchPaths;

            Resolver = new CompositeCompilationAssemblyResolver(new ICompilationAssemblyResolver[]
            {
                new ReferenceAssemblyPathResolver(),
                new PackageCompilationAssemblyResolver()
            });

            // TODO: TBD: doesn't this get recycled by the GC then?
            var loadContext = GetLoadContext(GetType().GetTypeInfo().Assembly);
            loadContext.Resolving += ResolveAssembly;
        }

        /// <summary>
        /// Gets the Last Written Timestamp when the Assemblies were updated.
        /// </summary>
        internal DateTime? AssembliesLastWrittenTimestamp
            => TryLoad(out var registrySet)
                ? registrySet.Select(x => File.GetLastWriteTimeUtc(x.AssemblyPath)).Max()
                : (DateTime?) null;

        /// <summary>
        /// Purge those <see cref="AssemblyDescriptor.AssemblyPath"/> which Do Not Exist.
        /// </summary>
        /// <returns></returns>
        internal AssemblyReferenceServiceManager PurgeNotExists()
        {
            bool NotExists(string path) => !File.Exists(path);
            RegistrySet?.RemoveWhere(x => NotExists(x.AssemblyPath));
            TrySave();
            return this;
        }

        /// <summary>
        /// Loads the <see cref="Assembly"/> associated with the <paramref name="assemblyName"/>.
        /// </summary>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        internal Assembly LoadAssembly(AssemblyName assemblyName)
        {
            IEnumerable<string> GetAllMatchingAssemblyReferencePaths(
                IEnumerable<string> referencePaths, IEnumerable<string> searchPaths)
            {
                const StringComparison comparison = OrdinalIgnoreCase;

                foreach (var y in referencePaths.Select(GetFileNameWithoutExtension)
                    .Where(x => x.Equals(assemblyName.Name, comparison)))
                {
                    yield return y;
                }

                var searchPattern = $"{assemblyName.Name}{AllowedAssemblyExtensionDll}";
                const SearchOption option = TopDirectoryOnly;

                foreach (var y in searchPaths
                    .SelectMany(x => EnumerateFiles(x, searchPattern, option), (_, x) => new { _, x })
                    .Where(tuple => AllowedAssemblyExtensions.Contains(GetExtension(tuple.x)))
                    .Select(tuple => tuple.x))
                {
                    yield return y;
                }
            }

            // TODO: TBD: best I can figure, the "loaded assemblies" 'registry' was only ever glommed on to...
            // TODO: TBD: would never grow, shrink, adjust, to the current state of a target project.
            foreach (var x in GetAllMatchingAssemblyReferencePaths(ReferencePath, SearchPaths).Where(IsNotNullOrEmpty)
            )
            {
                return LoadAssembly(x);
            }

            return Assembly.Load(assemblyName);
        }

        /// <summary>
        /// Resolves the <see cref="Assembly"/> given <paramref name="context"/> and
        /// <paramref name="name"/> in a consistent manner using <see cref="DoesNameEqual"/>
        /// predicate.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        private Assembly ResolveAssembly(AssemblyLoadContext context, AssemblyName name) => ResolveAssembly(context, name, DoesNameEqual);

        private Assembly ResolveAssembly(AssemblyLoadContext context, AssemblyName name, StringBinaryPredicate predicate)
        {
            // ReSharper disable once RedundantEmptyObjectOrCollectionInitializer
            bool TryResolveAssemblyPaths(out IEnumerable<string> assemblyPaths)
            {
                var paths = (List<string>) (assemblyPaths = new List<string> { });

                var rl = FindMatchingLibrary(Context.RuntimeLibraries, name, predicate);
                if (rl == null)
                {
                    return false;
                }

                var groups = rl.RuntimeAssemblyGroups;

                var wrapper = new CompilationLibrary(rl.Type, rl.Name, rl.Version, rl.Hash
                    , groups.SelectMany(g => g.AssetPaths), rl.Dependencies, rl.Serviceable);

                return Resolver.TryResolveAssemblyPaths(wrapper, paths) && assemblyPaths.Any();
            }

            if (TryResolveAssemblyPaths(out var resolvedPaths))
            {
                return resolvedPaths.Select(context.LoadFromAssemblyPath).FirstOrDefault();
            }

            return ReferencePath.Select(GetFileNameWithoutExtension).Where(f => predicate(f, name.Name))
                .Select(context.LoadFromAssemblyPath).FirstOrDefault();
        }

        /// <summary>
        /// Returns whether <paramref name="a"/> Does Equals <paramref name="b"/>.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private static bool DoesNameEqual(string a, string b) => string.Equals(a, b, OrdinalIgnoreCase);

        private static TLibrary FindMatchingLibrary<TLibrary>(IEnumerable<TLibrary> libraries, AssemblyName name, StringBinaryPredicate predicate)
            where TLibrary : Library
        {
            foreach (var l in libraries)
            {
                if (predicate(l.Name, name.Name))
                {
                    return l;
                }

                /* If the Package Name does not exactly match the Predicate,
                 * then we verify whether the Assembly File Name is a match. */

                if (l is RuntimeLibrary rl
                    && rl.RuntimeAssemblyGroups.Any(
                        g => g.AssetPaths.Select(GetFileNameWithoutExtension)
                            .Any(x => predicate(x, name.Name))
                    )
                )
                {
                    return l;
                }
            }

            return null;
        }

        //// TODO: TBD: the whole thing with this one is a faulty assumption, quite probably...
        //// TODO: TBD: would the assembly name not be sufficient? why by path?
        /// <summary>
        /// Loads the <see cref="Assembly"/> corresponding with the <paramref name="assemblyName"/>.
        /// </summary>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        private Assembly LoadAssembly(string assemblyName)
        {
            Assembly LoadAssemblyFromTypeLoadContext()
            {
                var loadContext = GetLoadContext(GetType().GetTypeInfo().Assembly);
                return loadContext.LoadFromAssemblyName(new AssemblyName(assemblyName));
            }

            // ReSharper disable once InvertIf
            if (!AssembliesByName.ContainsKey(assemblyName))
            {
                var assembly = LoadAssemblyFromTypeLoadContext();

                var loadedContext = DependencyContext.Load(assembly);
                if (loadedContext != null)
                {
                    Context = Context.Merge(loadedContext);
                }

                //// TODO: TBD: what was the intent behind this?
                //var basePath = GetDirectoryName(path);
                //if (!DirectoryPathsWithResolver.Contains(basePath))
                //{
                //    Resolver = Resolver.AbsorbResolvers(new AppBaseCompilationAssemblyResolver(basePath));
                //    DirectoryPathsWithResolver.Add(basePath);
                //}

                AssembliesByName.Add(assemblyName, assembly);
            }

            return AssembliesByName[assemblyName];
        }
    }
}
