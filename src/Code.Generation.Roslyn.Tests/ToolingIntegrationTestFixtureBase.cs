﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Code.Generation.Roslyn
{
    using Integration;
    using Xunit;
    using Xunit.Abstractions;
    using static Domain;
    using static Keywords;
    using static Path;
    using static Program;
    using static ServiceManager<GeneratedSyntaxTreeDescriptor, GeneratedSyntaxTreeRegistry>;
    using static StringLiterals;
    using static Version;
    using static RegexOptions;

    /// <summary>
    /// The primary goal of these tests is to verify invocation of the Tooling. Short of
    /// invoking via Command Line, never mind wiring up some Microsoft Build Targets, this
    /// is about as comprehensive an end-to-end set of integration tests as there gets for
    /// purposes of verifying expected behavior. I think we even should be able to achieve
    /// scenarios as complicated as re-generating code based on updated source, etc. We can
    /// even verify expected Error Level codes during the process.
    /// </summary>
    public abstract class ToolingIntegrationTestFixtureBase : TestFixtureBase
    {
        /// <summary>
        /// Gets the Bundle for use throughout the Integration tests.
        /// </summary>
        protected TestCaseBundle Bundle { get; } = new TestCaseBundle();

        protected string ExpectedOutputDirectory => Combine(Bundle.ProjectName, "obj");

        protected string ExpectedResponsePath => Combine(ExpectedOutputDirectory, $"{Bundle.ProjectName}.g.rsp");

        /// <summary>
        /// Protected Constructor.
        /// </summary>
        /// <param name="outputHelper"></param>
        protected ToolingIntegrationTestFixtureBase(ITestOutputHelper outputHelper)
            : base(outputHelper)
        {
        }

        /// <summary>
        /// Console application Main method delegate.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        protected delegate int MainCallback(params string[] args);

        /// <summary>
        /// The Default <see cref="MainCallback"/> simply Invokes the Main method itself.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private static int DefaultMainCallback(params string[] args) => Main(args);

        /// <summary>
        /// Output String Builder used throughout the integration tests.
        /// </summary>
        protected StringBuilder OutBuilder { get; private set; }

        /// <summary>
        /// Verifies via the <paramref name="callback"/> given <paramref name="args"/>.
        /// Returns the Error Level received after the <see cref="MainCallback"/>.
        /// </summary>
        /// <param name="callback">The Main Callback.
        /// Defaults to <see cref="DefaultMainCallback"/>.</param>
        /// <param name="args"></param>
        /// <returns></returns>
        protected virtual int Verify(MainCallback callback, params string[] args)
        {
            Out = new StringWriter((OutBuilder = new StringBuilder()).AssertNotNull());
            return (callback ?? DefaultMainCallback).Invoke(args);
        }

        /// <summary>
        /// Verifies the <paramref name="args"/> assuming default <see cref="MainCallback"/>.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        protected virtual int Verify(params string[] args) => Verify(null, args);

        /// <summary>
        /// Operates on the <paramref name="bundle"/>.
        /// </summary>
        /// <param name="bundle"></param>
        internal delegate void TestCaseBundleOperator(TestCaseBundle bundle);

        /// <summary>
        /// Operates on the <paramref name="builder"/>.
        /// </summary>
        /// <param name="builder"></param>
        internal delegate void ToolingParameterOperator(ToolingParameterBuilder builder);

        /// <summary>
        /// Renders the <see cref="OutBuilder"/> while also trimming any whitespace.
        /// </summary>
        /// <returns></returns>
        private string RenderOut() => $"{OutBuilder}".Trim();

        /// <summary>
        /// Expecting a Version to be composed by from two to four parts of nothing but digits.
        /// Optionally expecting an Informational Version, which could technically be anything.
        /// We do not care about range checking from this perspective.
        /// </summary>
        private Regex VersionRegEx { get; } = new Regex(@"(?<ver>(\d+\.){1,3}\d+)( \((?<info>\.+)\))?", Compiled);

        /// <summary>
        /// Verifies that we at least receive a Version.
        /// </summary>
        [Fact]
        public void Verify_Version()
        {
            var errorLevelNotExpected = (int?) null;

            // ReSharper disable once ExpressionIsAlwaysNull
            var errorLevel = Verify($"{DoubleDash}version").AssertEqual(1);

            var rendered = RenderOut();

            OutputHelper.WriteLine($"Tooling version is {rendered} (Error Level: {errorLevel}).");

            // Do some additional introspection concerning the expected Version response.
            var match = VersionRegEx.Match(rendered);

            string s;
            match.Success.AssertTrue();

            TryParse(s = match.GetGroupValue(ver), out var result).AssertTrue();

            $"{result}".AssertEqual(s);

            (!match.TryGetGroupValue(info, out s) || s?.Any() == true).AssertTrue();
        }

        /// <summary>
        /// Verifies the expected outcome when No Arguments are Specified.
        /// </summary>
        [Fact]
        public void Verify_No_Arguments_Specified()
        {
            // No need to do any If Then Else here since we will Assert the Expectation anyway.
            (Verify() == 1).AssertTrue();
            // Then Verify the further Out Rendering.
            RenderOut().AssertEqual(NoSourceFilesSpecified);
        }

        /// <summary>
        /// Pretty much encapsulates the Round Trip invocation of the <see cref="Main"/> method
        /// exposed in the Code Generation Tooling. This is as comprehensive an integration test
        /// as there is short of calling out to the Command Line Process itself, never mind wiring
        /// up the Microsoft Build targets.
        /// </summary>
        /// <param name="bundleOp"></param>
        /// <param name="parameterOp"></param>
        /// <param name="registrySet"></param>
        /// <returns></returns>
        internal virtual int VerifyWithOperators(TestCaseBundleOperator bundleOp, ToolingParameterOperator parameterOp
            , out GeneratedSyntaxTreeRegistry registrySet)
        {
            bundleOp?.Invoke(Bundle);
            var builder = new ToolingParameterBuilder {Project = $"{Bundle.ProjectName}"};
            parameterOp?.Invoke(builder);
            var verified = Verify(builder.ToArray());
            var loaded = TryLoad(Combine(builder.Output, builder.Generated), out registrySet);
            loaded.AssertEqual(registrySet?.Any() == true);
            return verified;
        }

        /// <summary>
        /// Pretty much encapsulates the Round Trip invocation of the <see cref="Main"/> method
        /// exposed in the Code Generation Tooling. This is as comprehensive an integration test
        /// as there is short of calling out to the Command Line Process itself, never mind wiring
        /// up the Microsoft Build targets.
        /// </summary>
        /// <param name="bundleOp"></param>
        /// <param name="parameterOp"></param>
        /// <returns></returns>
        /// <see cref="VerifyWithOperators(TestCaseBundleOperator,ToolingParameterOperator,out GeneratedSyntaxTreeRegistry)"/>
        internal virtual int VerifyWithOperators(TestCaseBundleOperator bundleOp, ToolingParameterOperator parameterOp)
            => VerifyWithOperators(bundleOp, parameterOp, out _);

        protected virtual void VerifyGeneratedSyntaxTreeRegistry(GeneratedSyntaxTreeRegistry registry, int? expectedCount = null)
        {
            var expectedOutputDirectory = ExpectedOutputDirectory;

            // TODO: TBD: it is probably fair to say this should be the case ALWAYS, regardless of the scenario.
            registry.AssertNotNull().OutputDirectory.AssertNotNull().AssertEqual(expectedOutputDirectory);

            if (expectedCount.HasValue)
            {
                registry.Count.AssertEqual(expectedCount.Value);
            }

            // Not so much Asserting the Collection, as it is leaving it potentially Open Ended.
            void VerifyGenerated(GeneratedSyntaxTreeDescriptor x)
            {
                var sourceLastWritten = File.GetLastWriteTimeUtc(x.SourceFilePath.AssertFileExists());

                var generatedPaths = x.GeneratedAssetKeys
                    .Select(y => $"{Combine(expectedOutputDirectory, $"{y:D}.g.cs").AssertFileExists()}")
                    .ToArray();

                var allGeneratedLastWritten = generatedPaths.Select(File.GetLastWriteTimeUtc).ToArray();
                allGeneratedLastWritten.All(y => y >= sourceLastWritten).AssertTrue();
            }

            registry.ToList().ForEach(VerifyGenerated);
        }

        protected virtual void VerifyResponseFile(GeneratedSyntaxTreeRegistry expectedRegistry)
        {
            var actualPaths = File.ReadLines(ExpectedResponsePath.AssertFileExists()).ToArray();

            var expectedOutputDirectory = ExpectedOutputDirectory;

            string CombineBaseDirectory(string fileName) => Combine(expectedOutputDirectory, fileName);

            var expectedPaths = expectedRegistry.SelectMany(d => d.GeneratedAssetKeys.Select(x => $"{x:D}.g.cs"))
                .Select(CombineBaseDirectory).ToArray();

            // ReSharper disable CommentTypo
            // Make sure that the Actuals are Actuals, same for Expected.
            actualPaths.Length.AssertEqual(expectedPaths.Length);

            // In no particular order so long as all of the Paths are present and accounted for.
            foreach (var expectedPath in expectedPaths)
            {
                actualPaths.AssertContains(expectedPath);
            }
            // ReSharper restore CommentTypo
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                Bundle.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
