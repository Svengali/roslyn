﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CodeFixes
{
    internal sealed partial class FixAllState
    {
        public readonly int CorrelationId = LogAggregator.GetNextId();

        public FixAllContext.DiagnosticProvider DiagnosticProvider { get; }

        public FixAllProvider? FixAllProvider { get; }
        public string? CodeActionEquivalenceKey { get; }
        public CodeFixProvider CodeFixProvider { get; }
        public ImmutableHashSet<string> DiagnosticIds { get; }
        public Document? Document { get; }
        public Project Project { get; }
        public FixAllScope Scope { get; }
        public CodeActionOptionsProvider CodeActionOptionsProvider { get; }

        // Note: TriggerSpan can be null from the back-compat public constructor of FixAllContext.
        public TextSpan? TriggerSpan { get; }

        /// <summary>
        /// Optional fix all spans to be fixed within the document. Can be empty
        /// if fixing the entire document, project or solution.
        /// If non-empty, <see cref="Document"/> is guaranteed to be not null.
        /// </summary>
        public ImmutableArray<TextSpan> FixAllSpans { get; }

        internal FixAllState(
            FixAllProvider? fixAllProvider,
            TextSpan? triggerSpan,
            Document? document,
            Project project,
            CodeFixProvider codeFixProvider,
            FixAllScope scope,
            ImmutableArray<TextSpan> fixAllSpans,
            string? codeActionEquivalenceKey,
            IEnumerable<string> diagnosticIds,
            FixAllContext.DiagnosticProvider fixAllDiagnosticProvider,
            CodeActionOptionsProvider codeActionOptionsProvider)
        {
            Debug.Assert(document == null || document.Project == project);
            Debug.Assert(fixAllSpans.IsDefaultOrEmpty || document != null);

            // We need trigger span for span based fix all scopes, i.e. FixAllScope.ContainingMember and FixAllScope.ContainingType
            Debug.Assert(triggerSpan.HasValue || scope is not FixAllScope.ContainingMember or FixAllScope.ContainingType);

            FixAllProvider = fixAllProvider;
            TriggerSpan = triggerSpan;
            Document = document;
            Project = project;
            CodeFixProvider = codeFixProvider;
            Scope = scope;
            FixAllSpans = fixAllSpans.NullToEmpty();
            CodeActionEquivalenceKey = codeActionEquivalenceKey;
            DiagnosticIds = ImmutableHashSet.CreateRange(diagnosticIds);
            DiagnosticProvider = fixAllDiagnosticProvider;
            CodeActionOptionsProvider = codeActionOptionsProvider;
        }

        public Solution Solution => Project.Solution;
        internal bool IsFixMultiple => DiagnosticProvider is FixMultipleDiagnosticProvider;

        public FixAllState WithScope(FixAllScope scope)
            => With(scope: scope);

        public FixAllState WithCodeActionEquivalenceKey(string? codeActionEquivalenceKey)
            => With(codeActionEquivalenceKey: codeActionEquivalenceKey);

        public FixAllState WithDocumentAndProject(Document? document, Project project)
            => With(documentAndProject: (document, project));

        public FixAllState WithFixAllSpans(ImmutableArray<TextSpan> fixAllSpans)
            => With(fixAllSpans: fixAllSpans);

        public FixAllState With(
            Optional<(Document? document, Project project)> documentAndProject = default,
            Optional<FixAllScope> scope = default,
            Optional<string?> codeActionEquivalenceKey = default,
            Optional<ImmutableArray<TextSpan>> fixAllSpans = default)
        {
            var (newDocument, newProject) = documentAndProject.HasValue ? documentAndProject.Value : (Document, Project);
            var newScope = scope.HasValue ? scope.Value : Scope;
            var newCodeActionEquivalenceKey = codeActionEquivalenceKey.HasValue ? codeActionEquivalenceKey.Value : CodeActionEquivalenceKey;
            var newFixAllSpans = fixAllSpans.HasValue ? fixAllSpans.Value.NullToEmpty() : FixAllSpans;

            if (newDocument == Document &&
                newProject == Project &&
                newScope == Scope &&
                newCodeActionEquivalenceKey == CodeActionEquivalenceKey &&
                newFixAllSpans.SetEquals(FixAllSpans))
            {
                return this;
            }

            return new FixAllState(
                FixAllProvider,
                TriggerSpan,
                newDocument,
                newProject,
                CodeFixProvider,
                newScope,
                newFixAllSpans,
                newCodeActionEquivalenceKey,
                DiagnosticIds,
                DiagnosticProvider,
                CodeActionOptionsProvider);
        }

        #region FixMultiple

        internal static FixAllState Create(
            FixAllProvider fixAllProvider,
            ImmutableDictionary<Document, ImmutableArray<Diagnostic>> diagnosticsToFix,
            CodeFixProvider codeFixProvider,
            string? codeActionEquivalenceKey,
            CodeActionOptionsProvider codeActionOptionsProvider)
        {
            var triggerDocument = diagnosticsToFix.First().Key;
            var triggerSpan = diagnosticsToFix.First().Value.FirstOrDefault()?.Location.SourceSpan;
            var diagnosticIds = GetDiagnosticsIds(diagnosticsToFix.Values);
            var diagnosticProvider = new FixMultipleDiagnosticProvider(diagnosticsToFix);
            return new FixAllState(
                fixAllProvider,
                triggerSpan,
                triggerDocument,
                triggerDocument.Project,
                codeFixProvider,
                FixAllScope.Custom,
                fixAllSpans: default,
                codeActionEquivalenceKey,
                diagnosticIds,
                diagnosticProvider,
                codeActionOptionsProvider);
        }

        internal static FixAllState Create(
            FixAllProvider fixAllProvider,
            ImmutableDictionary<Project, ImmutableArray<Diagnostic>> diagnosticsToFix,
            CodeFixProvider codeFixProvider,
            string? codeActionEquivalenceKey,
            CodeActionOptionsProvider codeActionOptionsProvider)
        {
            var triggerProject = diagnosticsToFix.First().Key;
            var diagnosticIds = GetDiagnosticsIds(diagnosticsToFix.Values);
            var diagnosticProvider = new FixMultipleDiagnosticProvider(diagnosticsToFix);
            return new FixAllState(
                fixAllProvider,
                triggerSpan: null,
                document: null,
                triggerProject,
                codeFixProvider,
                FixAllScope.Custom,
                fixAllSpans: default,
                codeActionEquivalenceKey,
                diagnosticIds,
                diagnosticProvider,
                codeActionOptionsProvider);
        }

        private static ImmutableHashSet<string> GetDiagnosticsIds(IEnumerable<ImmutableArray<Diagnostic>> diagnosticsCollection)
        {
            var uniqueIds = ImmutableHashSet.CreateBuilder<string>();
            foreach (var diagnostics in diagnosticsCollection)
            {
                foreach (var diagnostic in diagnostics)
                {
                    uniqueIds.Add(diagnostic.Id);
                }
            }

            return uniqueIds.ToImmutable();
        }

        #endregion
    }
}
