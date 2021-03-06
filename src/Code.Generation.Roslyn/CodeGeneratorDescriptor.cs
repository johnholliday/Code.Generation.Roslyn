﻿using System;
using System.Collections.Generic;

namespace Code.Generation.Roslyn
{
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static Constants;

    public class CodeGeneratorDescriptor
    {
        private string _preambleCommentText;

        /// <summary>
        /// Gets or Sets the generated Preamble. By default, Code Generation issues a canned
        /// Preamble. Canned Preamble Comments are provided out of the box. However, you may
        /// override with whatever preamble you find most appropriate given your code generation
        /// opportunity.
        /// </summary>
        /// <remarks>Gets the <see cref="_preambleCommentText"/> normalized agnostic of any Git
        /// checkout policies.</remarks>
        public string PreambleCommentText
        {
            get => _preambleCommentText.Replace(CarriageReturnLineFeed, LineFeed)
                .Replace(LineFeed, Environment.NewLine);
            set => _preambleCommentText = value ?? string.Empty;
        }

        /// <summary>
        /// Sets the <see cref="PreambleCommentText"/> to the <paramref name="s"/>.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public CodeGeneratorDescriptor WithPreambleComments(string s)
        {
            PreambleCommentText = s;
            return this;
        }

        // TODO: TBD: consider whether to change this to Guid-CompilationUnitSyntax based Dictionary?
        // TODO: TBD: concurrent dictionary? observable dictionary?
        // TODO: TBD: i.e. instead of iterating `generators´ and generated CompilationUnits, etc
        // TODO: TBD: potentially given the dictionary instance as part of the invocation context...
        // TODO: TBD: just watch this collection for changes during the generator invocations...
        /// <summary>
        /// Gets or Sets the desired <see cref="CompilationUnitSyntax"/> instances.
        /// This is the linchpin in the whole process.
        /// </summary>
        public ICollection<CompilationUnitSyntax> CompilationUnits { get; set; } = new List<CompilationUnitSyntax>();

        /// <summary>
        /// Gets or Sets whether to IncludeEndingNewLine.
        /// </summary>
        internal bool IncludeEndingNewLine { get; private set; }

        /// <summary>
        /// Sets whether to <see cref="IncludeEndingNewLine"/>.
        /// </summary>
        /// <param name="includeEndingNewLine"></param>
        /// <returns></returns>
        public CodeGeneratorDescriptor WithEndingNewLine(bool includeEndingNewLine)
        {
            IncludeEndingNewLine = true;
            return this;
        }

        public CodeGeneratorDescriptor WithEndingNewLine() => WithEndingNewLine(true);

        public CodeGeneratorDescriptor()
            : this(() => Resources.PreambleCommentText)
        {
        }

        public CodeGeneratorDescriptor(string preambleCommentText)
            : this(() => preambleCommentText)
        {
        }

        private CodeGeneratorDescriptor(Func<string> getPreambleComment)
        {
            var preambleComment = getPreambleComment();
            PreambleCommentText = preambleComment;
        }
    }
}
