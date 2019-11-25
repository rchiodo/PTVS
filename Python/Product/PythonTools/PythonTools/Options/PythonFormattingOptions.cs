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

namespace Microsoft.PythonTools.Options {
    public sealed class PythonFormattingOptions {
        private readonly PythonToolsService _service;

        private const string Category = "Advanced";

        private const string PasteRemovesReplPromptsSetting = "PasteRemovesReplPrompts";

        internal PythonFormattingOptions(PythonToolsService service) {
            _service = service;
        }

        public void Load() {
            PasteRemovesReplPrompts = _service.LoadBool(PasteRemovesReplPromptsSetting, Category) ?? true;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Save() {
            _service.SaveBool(PasteRemovesReplPromptsSetting, Category, PasteRemovesReplPrompts);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Reset() {
            PasteRemovesReplPrompts = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Changed;

        public bool PasteRemovesReplPrompts {
            get;
            set;
        }
    }
}