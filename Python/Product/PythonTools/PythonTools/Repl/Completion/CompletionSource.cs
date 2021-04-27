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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.InteractiveWindow;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Microsoft.VisualStudio.Text;

namespace Microsoft.PythonTools.Repl.Completion {
    static class CompletionSessionExtensions {
        private const string CompleteWord = nameof(CompleteWord);
        private const string TriggerChar = nameof(TriggerChar);

        public static void SetCompleteWordMode(this IIntellisenseSession session)
            => session.Properties[CompleteWord] = true;

        public static void ClearCompleteWordMode(this IIntellisenseSession session)
            => session.Properties.RemoveProperty(CompleteWord);

        public static bool IsCompleteWordMode(this IIntellisenseSession session)
            => session.Properties.TryGetProperty(CompleteWord, out bool prop) && prop;

        public static void SetTriggerCharacter(this IIntellisenseSession session, char triggerChar)
            => session.Properties[TriggerChar] = triggerChar;

        public static char GetTriggerCharacter(this IIntellisenseSession session)
            => session.Properties.TryGetProperty(TriggerChar, out char c) ? c : '\0';
    }
    /// <summary>
    /// This class supports auto complete in a repl. 
    /// </summary>
    /// <remarks>
    /// This is necessary because the VSSDK provides completions for ITextViews (through IAsyncCompletionSourceProvider) but not ITextBuffers
    /// The Repl has an ITextView but it's not a python content type.
    /// </remarks>
    class CompletionSource : ICompletionSource {
        private readonly ITextBuffer _textBuffer;
        private readonly CompletionSourceProvider _provider;

        public CompletionSource(CompletionSourceProvider provider, ITextBuffer textBuffer) {
            _textBuffer = textBuffer;
            _provider = provider;
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets) {
            var textBuffer = _textBuffer;
            var triggerPoint = session.GetTriggerPoint(textBuffer);
            if (textBuffer.Properties.TryGetProperty(typeof(IInteractiveEvaluator), out IInteractiveEvaluator evaluator)) {
                if (evaluator is PythonCommonInteractiveEvaluator commonEvaluator) {
                    System.Diagnostics.Debug.WriteLine("Might be able to get completions");
                }
            }
        }

        public void Dispose() {
        }
    }
}
