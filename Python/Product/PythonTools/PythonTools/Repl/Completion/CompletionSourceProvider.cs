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
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.PythonTools.Repl.Completion {
    [Export(typeof(ICompletionSourceProvider)), ContentType(PythonCoreConstants.ContentType), Order, Name("CompletionProvider")]
    internal class CompletionSourceProvider : ICompletionSourceProvider {
        internal readonly IGlyphService _glyphService;
        internal readonly PythonToolsService _pyService;
        internal readonly IServiceProvider _serviceProvider;

        [ImportingConstructor]
        public CompletionSourceProvider([Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider, IGlyphService glyphService) {
            _pyService = serviceProvider.GetPythonToolsService();
            _glyphService = glyphService;
            _serviceProvider = serviceProvider;
        }

        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer) {
            // Only create these completion sources for repls. Normal python buffers
            // are handled by the LanguageServer.Client in the VSSDK
            if (textBuffer.IsReplBuffer()) {
                return new CompletionSource(this, textBuffer);
            }
            return null;
        }
    }
}
