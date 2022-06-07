﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.EmbeddedLanguages;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Classification
{
    internal abstract class AbstractEmbeddedLanguageClassificationService :
        AbstractEmbeddedLanguageFeatureService<IEmbeddedLanguageClassifier>, IEmbeddedLanguageClassificationService
    {
        /// <summary>
        /// Finally classifier to run if there is no embedded language in a string.  It will just classify escape sequences.
        /// </summary>
        private readonly IEmbeddedLanguageClassifier _fallbackClassifier;

        protected AbstractEmbeddedLanguageClassificationService(
            string languageName,
            EmbeddedLanguageInfo info,
            ISyntaxKinds syntaxKinds,
            IEmbeddedLanguageClassifier fallbackClassifier,
            IEnumerable<Lazy<IEmbeddedLanguageClassifier, EmbeddedLanguageMetadata>> allClassifiers)
            : base(languageName, info, syntaxKinds, allClassifiers)
        {
            _fallbackClassifier = fallbackClassifier;
        }

        public async Task AddEmbeddedLanguageClassificationsAsync(
            Document document, TextSpan textSpan, ClassificationOptions options, ArrayBuilder<ClassifiedSpan> result, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            AddEmbeddedLanguageClassifications(document.Project, semanticModel, textSpan, options, result, cancellationToken);
        }

        public void AddEmbeddedLanguageClassifications(
            Project? project, SemanticModel semanticModel, TextSpan textSpan, ClassificationOptions options, ArrayBuilder<ClassifiedSpan> result, CancellationToken cancellationToken)
        {
            using var _ = ArrayBuilder<IEmbeddedLanguageClassifier>.GetInstance(out var classifierBuffer);
            var root = semanticModel.SyntaxTree.GetRoot(cancellationToken);
            var worker = new Worker(this, project, semanticModel, textSpan, options, result, classifierBuffer, cancellationToken);
            worker.Recurse(root);
        }

        private ref struct Worker
        {
            private readonly AbstractEmbeddedLanguageClassificationService _owner;
            private readonly Project? _project;
            private readonly SemanticModel _semanticModel;
            private readonly TextSpan _textSpan;
            private readonly ClassificationOptions _options;
            private readonly ArrayBuilder<ClassifiedSpan> _result;
            private readonly ArrayBuilder<IEmbeddedLanguageClassifier> _classifierBuffer;
            private readonly CancellationToken _cancellationToken;

            public Worker(
                AbstractEmbeddedLanguageClassificationService service,
                Project? project,
                SemanticModel semanticModel,
                TextSpan textSpan,
                ClassificationOptions options,
                ArrayBuilder<ClassifiedSpan> result,
                ArrayBuilder<IEmbeddedLanguageClassifier> classifierBuffer,
                CancellationToken cancellationToken)
            {
                _owner = service;
                _project = project;
                _semanticModel = semanticModel;
                _textSpan = textSpan;
                _options = options;
                _result = result;
                _classifierBuffer = classifierBuffer;
                _cancellationToken = cancellationToken;
            }

            public void Recurse(SyntaxNode node)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (node.Span.IntersectsWith(_textSpan))
                {
                    foreach (var child in node.ChildNodesAndTokens())
                    {
                        if (child.IsNode)
                        {
                            Recurse(child.AsNode()!);
                        }
                        else
                        {
                            ProcessToken(child.AsToken());
                        }
                    }
                }
            }

            private void ProcessToken(SyntaxToken token)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                ProcessTriviaList(token.LeadingTrivia);
                ClassifyToken(token);
                ProcessTriviaList(token.TrailingTrivia);
            }

            private void ClassifyToken(SyntaxToken token)
            {
                if (token.Span.IntersectsWith(_textSpan) && _owner.SyntaxTokenKinds.Contains(token.RawKind))
                {
                    _classifierBuffer.Clear();

                    var context = new EmbeddedLanguageClassificationContext(
                        _project, _semanticModel, token, _options, _owner.Info.VirtualCharService, _result, _cancellationToken);

                    // First, see if this is a string annotated with either a comment or [StringSyntax] attribute. If
                    // so, delegate to the first classifier we have registered for whatever language ID we find.
                    if (_owner.Detector.IsEmbeddedLanguageToken(token, _semanticModel, _cancellationToken, out var identifier, out _) &&
                        _owner.IdentifierToServices.TryGetValue(identifier, out var classifiers))
                    {
                        foreach (var classifier in classifiers)
                        {
                            // keep track of what classifiers we've run so we don't call into them multiple times.
                            _classifierBuffer.Add(classifier.Value);

                            // If this classifier added values then need to check the other ones.
                            if (TryClassify(classifier.Value, context))
                                return;
                        }
                    }

                    // It wasn't an annotated API.  See if it's some legacy API our historical classifiers have direct
                    // support for (for example, .net APIs prior to Net6).
                    foreach (var legacyClassifier in _owner.LegacyServices)
                    {
                        // don't bother trying to classify again if we already tried above.
                        if (_classifierBuffer.Contains(legacyClassifier.Value))
                            continue;

                        // If this classifier added values then need to check the other ones.
                        if (TryClassify(legacyClassifier.Value, context))
                            return;
                    }

                    // Finally, give the fallback classifier a chance to classify basic language escapes.
                    TryClassify(_owner._fallbackClassifier, context);
                }
            }

            private bool TryClassify(IEmbeddedLanguageClassifier classifier, EmbeddedLanguageClassificationContext context)
            {
                var count = _result.Count;
                classifier.RegisterClassifications(context);
                return _result.Count != count;
            }

            private void ProcessTriviaList(SyntaxTriviaList triviaList)
            {
                foreach (var trivia in triviaList)
                    ProcessTrivia(trivia);
            }

            private void ProcessTrivia(SyntaxTrivia trivia)
            {
                if (trivia.HasStructure && trivia.FullSpan.IntersectsWith(_textSpan))
                    Recurse(trivia.GetStructure()!);
            }
        }
    }
}
