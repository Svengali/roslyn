﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.LanguageServer.Handler.SemanticTokens;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Roslyn.Test.Utilities;
using Xunit;
using LSP = Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.LanguageServer.UnitTests.SemanticTokens
{
    public class SemanticTokensTests : AbstractLanguageServerProtocolTests
    {
        [Fact]
        public async Task TestGetSemanticTokensAsync()
        {
            var markup =
@"{|caret:|}// Comment
static class C { }";
            using var workspace = CreateTestWorkspace(markup, out var locations);
            var results = await RunGetSemanticTokensAsync(workspace.CurrentSolution, locations["caret"].First());

            var expectedResults = new LSP.SemanticTokens
            {
                Data = new int[]
                {
                    // Line | Char | Len | Token type                                                          | Modifier
                       0,     0,     10,   SemanticTokensHelpers.GetTokenTypeIndex(SemanticTokenTypes.Comment),  0, // '// Comment'
                       1,     0,     6,    SemanticTokensHelpers.GetTokenTypeIndex(SemanticTokenTypes.Keyword),  0, // 'static'
                       0,     7,     5,    SemanticTokensHelpers.GetTokenTypeIndex(SemanticTokenTypes.Keyword),  0, // 'class'
                       0,     6,     1,    SemanticTokensHelpers.GetTokenTypeIndex(SemanticTokenTypes.Class),    (int)TokenModifiers.Static, // 'C'
                       0,     2,     1,    SemanticTokensHelpers.GetTokenTypeIndex(SemanticTokenTypes.Operator), 0, // '{'
                       0,     2,     1,    SemanticTokensHelpers.GetTokenTypeIndex(SemanticTokenTypes.Operator), 0, // '}'
                },
                ResultId = "0"
            };

            Assert.Equal(results.Data, expectedResults.Data);
            Assert.Equal(results.ResultId, expectedResults.ResultId);
        }

        private static async Task<LSP.SemanticTokens> RunGetSemanticTokensAsync(Solution solution, LSP.Location caret)
            => await GetLanguageServer(solution).ExecuteRequestAsync<LSP.SemanticTokensParams, LSP.SemanticTokens>(
                LSP.SemanticTokensMethods.TextDocumentSemanticTokensName,
                CreateSemanticTokensParams(caret), new LSP.VSClientCapabilities(), null, CancellationToken.None);

        private static LSP.SemanticTokensParams CreateSemanticTokensParams(LSP.Location caret)
            => new LSP.SemanticTokensParams
            {
                TextDocument = new LSP.TextDocumentIdentifier { Uri = caret.Uri }
            };
    }
}
