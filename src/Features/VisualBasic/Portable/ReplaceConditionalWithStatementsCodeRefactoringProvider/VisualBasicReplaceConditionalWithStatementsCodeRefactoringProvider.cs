﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Composition
Imports System.Diagnostics.CodeAnalysis
Imports Microsoft.CodeAnalysis.CodeRefactorings
Imports Microsoft.CodeAnalysis.VisualBasic.Extensions
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.CodeAnalysis.Editing
Imports Microsoft.CodeAnalysis.Host.Mef
Imports Microsoft.CodeAnalysis.ReplaceConditionalWithStatements
Imports System.Runtime.InteropServices

Namespace Microsoft.CodeAnalysis.VisualBasic.ReplaceConditionalWithStatements

    <ExportCodeRefactoringProvider(LanguageNames.VisualBasic, Name:=PredefinedCodeRefactoringProviderNames.ReplaceConditionalWithStatements), [Shared]>
    Friend Class VisualBasicReplaceConditionalWithStatementsCodeRefactoringProvider
        Inherits AbstractReplaceConditionalWithStatementsCodeRefactoringProvider(of
            ExpressionSyntax,
            TernaryConditionalExpressionSyntax,
            StatementSyntax,
            ThrowStatementSyntax,
            YieldStatementSyntax,
            ReturnStatementSyntax,
            ExpressionStatementSyntax,
            LocalDeclarationStatementSyntax,
            ArgumentSyntax,
            ArgumentListSyntax,
            ModifiedIdentifierSyntax,
            VariableDeclaratorSyntax,
            EqualsValueSyntax)

        <ImportingConstructor>
        <Obsolete(MefConstruction.ImportingConstructorMessage, True)>
        Public Sub New()
        End Sub

        Protected Overrides Function IsAssignmentStatement(statement As StatementSyntax) As Boolean
            Return TypeOf statement Is AssignmentStatementSyntax
        End Function

        Protected Overrides Function HasSingleVariable(
                localDeclarationStatement As LocalDeclarationStatementSyntax,
                <Out> ByRef variable As ModifiedIdentifierSyntax) As Boolean
            If localDeclarationStatement.Declarators.Count = 1
                Dim declarator = localDeclarationStatement.Declarators(0)
                If declarator.Names.Count = 1
                    variable = declarator.Names(0)
                    Return True
                End If
            End If

            variable = Nothing
            Return False
        End Function

        Protected Overrides Function GetUpdatedLocalDeclarationStatement(
                generator As SyntaxGenerator,
                localDeclarationStatement As LocalDeclarationStatementSyntax,
                symbol As ILocalSymbol) As LocalDeclarationStatementSyntax
            ' If we have `dim x = if(a, b, c)`
            ' then we have to add an actual type of the local when breaking this into multiple statements.
            Dim declarator = localDeclarationStatement.Declarators(0)
            If declarator.AsClause Is Nothing
                localDeclarationStatement = localDeclarationStatement.ReplaceNode(
                    declarator, declarator.WithAsClause(SyntaxFactory.SimpleAsClause(
                        symbol.Type.GenerateTypeSyntax())))
            End If

            declarator = localDeclarationStatement.declarators(0)
            Return localDeclarationStatement.ReplaceNode(
                declarator,
                declarator.WithInitializer(nothing))
        End Function
    End Class
End Namespace
