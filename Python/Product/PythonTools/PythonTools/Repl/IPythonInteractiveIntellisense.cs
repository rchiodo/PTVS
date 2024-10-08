// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABILITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Common.Parsing;
using Microsoft.PythonTools.Intellisense;
using Microsoft.VisualStudio.Text;
using LSP = Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Microsoft.PythonTools.Repl {
    interface IPythonInteractiveIntellisense {
        IEnumerable<KeyValuePair<string, string>> GetAvailableScopesAndPaths();
        Task<CompletionResult[]> GetMemberNamesAsync(string text, CancellationToken ct);
        Task<OverloadDoc[]> GetSignatureDocumentationAsync(string text, CancellationToken ct);
        Task<object> GetAnalysisCompletions(LSP.Position position, LSP.CompletionContext context, CancellationToken token);
        PythonLanguageVersion LanguageVersion { get; }
    }
}
