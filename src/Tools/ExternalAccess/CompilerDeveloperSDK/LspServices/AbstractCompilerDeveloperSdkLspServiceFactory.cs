﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.LanguageServer;

namespace Microsoft.CodeAnalysis.ExternalAccess.CompilerDeveloperSdk;

internal abstract class AbstractCompilerDeveloperSdkLspServiceFactory : ILspServiceFactory
{
    public abstract AbstractCompilerDeveloperSdkLspService CreateILspService(CompilerDeveloperSdkLspServices lspServices);

    ILspService ILspServiceFactory.CreateILspService(LspServices lspServices, WellKnownLspServerKinds serverKind)
    {
        return CreateILspService(new CompilerDeveloperSdkLspServices(lspServices));
    }
}
