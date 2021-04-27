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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Python.Parsing;
using Microsoft.PythonTools.Editor;
using Microsoft.PythonTools.Editor.Core;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.InteractiveWindow;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.IncrementalSearch;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using VSConstants = Microsoft.VisualStudio.VSConstants;
using LSP = Microsoft.VisualStudio.LanguageServer.Protocol;
using Microsoft.PythonTools.LanguageServerClient;

namespace Microsoft.PythonTools.Repl.Completion {
    internal sealed class IntellisenseController : IIntellisenseController, IOleCommandTarget {
        private readonly PythonEditorServices _services;
        private readonly ITextView _textView;
        private readonly IntellisenseControllerProvider _provider;
        private readonly IIncrementalSearch _incSearch;
        private ICompletionSession _activeSession;
        private ISignatureHelpSession _sigHelpSession;
        private IAsyncQuickInfoSession _quickInfoSession;
        internal IOleCommandTarget _oldTarget;
        private static readonly string[] _allStandardSnippetTypes = { ExpansionClient.Expansion, ExpansionClient.SurroundsWith };
        private static readonly string[] _surroundsWithSnippetTypes = { ExpansionClient.SurroundsWith, ExpansionClient.SurroundsWithStatement };

        public static readonly object SuppressErrorLists = new object();
        public static readonly object FollowDefaultEnvironment = new object();
        private const string _defaultCompletionChars = "{}[]().,:;+-*/%&|^~=<>#@\\";


        /// <summary>
        /// Attaches events for invoking Statement completion 
        /// </summary>
        public IntellisenseController(IntellisenseControllerProvider provider, ITextView textView) {
            _textView = textView;
            _provider = provider;
            _services = provider.Services;
            _incSearch = _services.IncrementalSearch.GetIncrementalSearch(textView);
            _textView.MouseHover += TextViewMouseHover;
            textView.Properties.AddProperty(typeof(IntellisenseController), this);  // added so our key processors can get back to us
            _textView.Closed += TextView_Closed;
        }

        public async void ConnectSubjectBuffer(ITextBuffer subjectBuffer) {
            var buffer = _services.GetBufferInfo(subjectBuffer);
            for (int retries = 5; retries > 0; --retries) {
                try {
                    // This would be were we'd cache items?
                    return;
                } catch (InvalidOperationException) {
                    // Analysis entry changed, so we should retry
                }
            }
            Debug.Fail("Failed to connect subject buffer after multiple retries");
        }

        public void DisconnectSubjectBuffer(ITextBuffer subjectBuffer) {
            var bi = PythonTextBufferInfo.TryGetForBuffer(subjectBuffer);
            bi?.RemoveSink(this);
        }


        private void TextView_Closed(object sender, EventArgs e) {
            Close();
        }

        internal void Close() {
            _textView.MouseHover -= TextViewMouseHover;
            _textView.Closed -= TextView_Closed;
            _textView.Properties.RemoveProperty(typeof(IntellisenseController));
            // Do not disconnect subject buffers here - VS will handle that for us
        }

        private void TextViewMouseHover(object sender, MouseHoverEventArgs e) {
            TextViewMouseHoverWorker(e)
                .HandleAllExceptions(_services.Site, GetType())
                .DoNotWait();
        }

        private static async Task DismissQuickInfo(IAsyncQuickInfoSession session) {
            if (session != null && session.State != QuickInfoSessionState.Dismissed) {
                await session.DismissAsync();
            }
        }

        private async Task TextViewMouseHoverWorker(MouseHoverEventArgs e) {
            var pt = e.TextPosition.GetPoint(EditorExtensions.IsPythonContent, PositionAffinity.Successor);
            if (pt == null) {
                return;
            }

            if (_textView.TextBuffer.GetInteractiveWindow() != null &&
                pt.Value.Snapshot.Length > 1 &&
                pt.Value.Snapshot[0] == '$') {
                // don't provide quick info on help, the content type doesn't switch until we have
                // a complete command otherwise we shouldn't need to do this.
                await DismissQuickInfo(Interlocked.Exchange(ref _quickInfoSession, null));
                return;
            }

            // TODO: Get quick info
            object entry = null;
            if (entry == null) {
                await DismissQuickInfo(Interlocked.Exchange(ref _quickInfoSession, null));
                return;
            }

            var session = _quickInfoSession;
            if (session != null) {
                try {
                    var span = session.ApplicableToSpan?.GetSpan(pt.Value.Snapshot);
                    if (span != null && span.Value.Contains(pt.Value)) {
                        return;
                    }
                } catch (ArgumentException) {
                }
            }

            // TODO: Quick Info on Repl
            object quickInfo = null;
            if (quickInfo == null) {
                await DismissQuickInfo(Interlocked.Exchange(ref _quickInfoSession, null));
                return;
            }

            var viewPoint = _textView.BufferGraph.MapUpToBuffer(
                pt.Value,
                PointTrackingMode.Positive,
                PositionAffinity.Successor,
                _textView.TextBuffer
            );

            if (viewPoint != null) {
                _quickInfoSession = await _services.QuickInfoBroker.TriggerQuickInfoAsync(
                    _textView,
                    viewPoint.Value.Snapshot.CreateTrackingPoint(viewPoint.Value, PointTrackingMode.Positive),
                    QuickInfoSessionOptions.TrackMouse
                );
            }
        }

        internal async Task TriggerQuickInfoAsync() {
            if (_quickInfoSession != null && _quickInfoSession.State != QuickInfoSessionState.Dismissed) {
                await _quickInfoSession.DismissAsync();
            }

            _quickInfoSession = await _services.QuickInfoBroker.TriggerQuickInfoAsync(_textView);
        }

        private static object _intellisenseAnalysisEntry = new object();


        /// <summary>
        /// Detaches the events
        /// </summary>
        /// <param name="textView"></param>
        public void Detach(ITextView textView) {
            if (_textView == null) {
                throw new InvalidOperationException(Strings.IntellisenseControllerAlreadyDetachedException);
            }
            if (textView != _textView) {
                throw new ArgumentException(Strings.IntellisenseControllerNotAttachedToSpecifiedTextViewException, nameof(textView));
            }

            _textView.MouseHover -= TextViewMouseHover;
            _textView.Properties.RemoveProperty(typeof(IntellisenseController));

            DetachKeyboardFilter();
        }

        private string GetTextBeforeCaret(int includeCharsAfter = 0) {
            var maybePt = _textView.Caret.Position.Point.GetPoint(_textView.TextBuffer, PositionAffinity.Predecessor);
            if (!maybePt.HasValue) {
                return string.Empty;
            }
            var pt = maybePt.Value + includeCharsAfter;

            var span = new SnapshotSpan(pt.GetContainingLine().Start, pt);
            return span.GetText();
        }

        private void HandleChar(char ch) {
            // We trigger completions when the user types . or space.  Called via our IOleCommandTarget filter
            // on the text view.
            //
            // We trigger signature help when we receive a "(".  We update our current sig when 
            // we receive a "," and we close sig help when we receive a ")".

            if (!_incSearch.IsActive) {
                var prefs = _services.Python.LangPrefs;

                var session = Volatile.Read(ref _activeSession);
                var sigHelpSession = Volatile.Read(ref _sigHelpSession);
                var literalSpan = GetStringLiteralSpan();
                if (literalSpan.HasValue &&
                    // UNDONE: Do not automatically trigger file path completions github#2352
                    //ShouldTriggerStringCompletionSession(prefs, literalSpan.Value) &&
                    (session?.IsDismissed ?? true)) {
                    //TriggerCompletionSession(false);
                    return;
                }

                switch (ch) {
                    case '@':
                        if (!string.IsNullOrWhiteSpace(GetTextBeforeCaret(-1))) {
                            break;
                        }
                        goto case '.';
                    case '.':
                    case ' ':
                        if (prefs.AutoListMembers && GetStringLiteralSpan() == null) {
                            TriggerCompletionSession(false, ch).DoNotWait();
                        }
                        break;
                    case '(':
                        if (prefs.AutoListParams && GetStringLiteralSpan() == null) {
                            OpenParenStartSignatureSession();
                        }
                        break;
                    case ')':
                        if (sigHelpSession != null) {
                            sigHelpSession.Dismiss();
                        }

                        if (prefs.AutoListParams) {
                            // trigger help for outer call if there is one
                            TriggerSignatureHelp();
                        }
                        break;
                    case '=':
                    case ',':
                        if (sigHelpSession == null) {
                            if (prefs.AutoListParams) {
                                CommaStartSignatureSession();
                            }
                        } else {
                            UpdateCurrentParameter();
                        }
                        break;
                    default:
                        // Note: Don't call CompletionSets property if session is dismissed to avoid NRE
                        if (Tokenizer.IsIdentifierStartChar(ch) &&
                            ((session?.IsDismissed ?? false ? 0 : session?.CompletionSets.Count ?? 0) == 0)) {
                            bool commitByDefault;
                            if (ShouldTriggerIdentifierCompletionSession(out commitByDefault)) {
                                TriggerCompletionSession(false, ch, commitByDefault).DoNotWait();
                            }
                        }
                        break;
                }
            }
        }

        private SnapshotSpan? GetStringLiteralSpan() {
            var pyCaret = _textView.GetPythonCaret();
            var aggregator = _services.ClassifierAggregator;
            IClassifier classifier = aggregator.GetClassifier(_textView.TextBuffer);
            if (classifier == null) {
                return null;
            }

            var spans = classifier.GetClassificationSpans(new SnapshotSpan(pyCaret.Value.GetContainingLine().Start, pyCaret.Value));
            var token = spans.LastOrDefault();
            if (!(token?.ClassificationType.IsOfType(PredefinedClassificationTypeNames.String) ?? false)) {
                return null;
            }

            return token.Span;
        }

        private bool ShouldTriggerIdentifierCompletionSession(out bool commitByDefault) {
            commitByDefault = true;

            var caretPoint = _textView.GetPythonCaret();
            if (!caretPoint.HasValue) {
                return false;
            }

            var snapshot = caretPoint.Value.Snapshot;

            var statement = new ReverseExpressionParser(
                snapshot,
                snapshot.TextBuffer,
                snapshot.CreateTrackingSpan(caretPoint.Value.Position, 0, SpanTrackingMode.EdgeNegative),
                _services
            ).GetStatementRange();
            if (!statement.HasValue || caretPoint.Value <= statement.Value.Start) {
                return false;
            }

            var text = new SnapshotSpan(statement.Value.Start, caretPoint.Value).GetText();
            if (string.IsNullOrEmpty(text)) {
                return false;
            }

            // TODO: Figure out if we should trigger
            return true;
        }

        private bool Backspace() {
            var sigHelpSession = Volatile.Read(ref _sigHelpSession);
            if (sigHelpSession != null) {
                if (_textView.Selection.IsActive && !_textView.Selection.IsEmpty) {
                    // when deleting a selection don't do anything to pop up signature help again
                    sigHelpSession.Dismiss();
                    return false;
                }

                SnapshotPoint? caretPoint = _textView.BufferGraph.MapDownToFirstMatch(
                    _textView.Caret.Position.BufferPosition,
                    PointTrackingMode.Positive,
                    EditorExtensions.IsPythonContent,
                    PositionAffinity.Predecessor
                );

                if (caretPoint != null && caretPoint.Value.Position != 0) {
                    var deleting = caretPoint.Value.Snapshot[caretPoint.Value.Position - 1];
                    if (deleting == ',') {
                        caretPoint.Value.Snapshot.TextBuffer.Delete(new Span(caretPoint.Value.Position - 1, 1));
                        UpdateCurrentParameter();
                        return true;
                    } else if (deleting == '(' || deleting == ')') {
                        sigHelpSession.Dismiss();
                        // delete the ( before triggering help again
                        caretPoint.Value.Snapshot.TextBuffer.Delete(new Span(caretPoint.Value.Position - 1, 1));

                        // Pop to an outer nesting of signature help
                        if (_services.Python.LangPrefs.AutoListParams) {
                            TriggerSignatureHelp();
                        }

                        return true;
                    }
                }
            }
            return false;
        }

        private void OpenParenStartSignatureSession() {
            Volatile.Read(ref _activeSession)?.Dismiss();
            Volatile.Read(ref _sigHelpSession)?.Dismiss();

            TriggerSignatureHelp();
        }

        private void CommaStartSignatureSession() {
            TriggerSignatureHelp();
        }

        /// <summary>
        /// Updates the current parameter for the caret's current position.
        /// 
        /// This will analyze the buffer for where we are currently located, find the current
        /// parameter that we're entering, and then update the signature.  If our current
        /// signature does not have enough parameters we'll find a signature which does.
        /// </summary>
        private void UpdateCurrentParameter() {
            var sigHelpSession = Volatile.Read(ref _sigHelpSession);
            if (sigHelpSession == null) {
                // we moved out of the original span for sig help, re-trigger based upon the position
                TriggerSignatureHelp();
                return;
            }

            int position = _textView.Caret.Position.BufferPosition.Position;

            // TODO: Handle signature help
        }

        private bool SelectSingleBestCompletion(ICompletionSession session) {
            if (session.CompletionSets.Count != 1) {
                return false;
            }
            var set = session.CompletionSets[0];
            if (set == null) {
                return false;
            }

            // TODO: Figure out how to select the best
            //if (set.SelectSingleBest()) {
            //    session.Commit();
            //    return true;
            //}
            return false;
        }

        internal async Task TriggerCompletionSession(bool completeWord, char triggerChar, bool? commitByDefault = null) {
            var caretPoint = _textView.TextBuffer.CurrentSnapshot.CreateTrackingPoint(_textView.Caret.Position.BufferPosition, PointTrackingMode.Positive);
            var session = _services.CompletionBroker.CreateCompletionSession(_textView, caretPoint, true);
            if (session == null) {
                // Session is null when text view has multiple carets
                return;
            }

            session.SetTriggerCharacter(triggerChar);
            if (completeWord) {
                session.SetCompleteWordMode();
            }

            var oldSession = Interlocked.Exchange(ref _activeSession, session);
            if (oldSession != null && !oldSession.IsDismissed) {
                oldSession.Dismiss();
            }

            if (session.IsStarted || session.IsDismissed) {
                return;
            }

            // Trigger the async task for the completions
            var triggerPoint = session.GetTriggerPoint(_textView.TextBuffer);
            if (_textView.TextBuffer.Properties.TryGetProperty(typeof(IInteractiveEvaluator), out IInteractiveEvaluator evaluator)) {
                if (evaluator is SelectableReplEvaluator replEvaluator) {
                    // Setup the context.
                    var context = new LSP.CompletionContext();
                    context.TriggerCharacter = triggerChar.ToString();
                    context.TriggerKind = LSP.CompletionTriggerKind.TriggerCharacter;

                    // Save the cancel source so we can dismiss if a new session is created
                    var cancelSource = new CancellationTokenSource();
                    session.Properties.AddProperty(PropertyConstants.CancelTokenSource, cancelSource);

                    // Translate the trigger point into an LSP position
                    var position = triggerPoint.GetPosition();
                    var task = replEvaluator.GetAnalysisCompletions(position, context, cancelSource.Token);

                    // Add the task into buffer so we can use it in the completion session
                    _textView.TextBuffer.Properties.AddProperty(PropertyConstants.CompletionTaskKey, task);
                }
            }


            session.Start();
            if (!session.IsStarted) {
                Volatile.Write(ref _activeSession, null);
                return;
            }

            if (completeWord && SelectSingleBestCompletion(session)) {
                session.Commit();
                return;
            }

            session.Filter();
            session.Dismissed += OnCompletionSessionDismissedOrCommitted;
            session.Committed += OnCompletionSessionDismissedOrCommitted;
        }

        internal void TriggerSignatureHelp() {
            Volatile.Read(ref _sigHelpSession)?.Dismiss();

            ISignatureHelpSession sigHelpSession = null;
            try {
                sigHelpSession = _services.SignatureHelpBroker.TriggerSignatureHelp(_textView);
            } catch (ObjectDisposedException) {
            }

            if (sigHelpSession != null) {
                sigHelpSession.Dismissed += OnSignatureSessionDismissed;
                _sigHelpSession = sigHelpSession;
            }
        }

        private void OnCompletionSessionDismissedOrCommitted(object sender, EventArgs e) {
            // We've just been told that our active session was dismissed.  We should remove all references to it.
            var session = sender as ICompletionSession;
            if (session == null) {
                Debug.Fail("invalid type passed to event");
                return;
            }
            if (session.Properties.TryGetProperty(PropertyConstants.CancelTokenSource, out CancellationTokenSource tokenSource)) {
                tokenSource.Cancel();
                session.Properties.RemoveProperty(PropertyConstants.CancelTokenSource);
                session.Properties.RemoveProperty(PropertyConstants.CompletionTaskKey);
            }
            session.Committed -= OnCompletionSessionDismissedOrCommitted;
            session.Dismissed -= OnCompletionSessionDismissedOrCommitted;
            Interlocked.CompareExchange(ref _activeSession, null, session);
        }

        private void OnSignatureSessionDismissed(object sender, EventArgs e) {
            // We've just been told that our active session was dismissed.  We should remove all references to it.
            var session = sender as ISignatureHelpSession;
            if (session == null) {
                Debug.Fail("invalid type passed to event");
                return;
            }
            session.Dismissed -= OnSignatureSessionDismissed;
            Interlocked.CompareExchange(ref _sigHelpSession, null, session);
        }


        internal bool DismissCompletionSession() {
            var session = Interlocked.Exchange(ref _activeSession, null);
            if (session != null && !session.IsDismissed) {
                session.Dismiss();
                return true;
            }
            return false;
        }

        #region IOleCommandTarget Members

        // we need this because VS won't give us certain keyboard events as they're handled before our key processor.  These
        // include enter and tab both of which we want to complete.

        internal void AttachKeyboardFilter() {
            if (_oldTarget == null) {
                var viewAdapter = _services.EditorAdaptersFactoryService.GetViewAdapter(_textView);
                if (viewAdapter != null) {
                    ErrorHandler.ThrowOnFailure(viewAdapter.AddCommandFilter(this, out _oldTarget));
                }
            }
        }

        private void DetachKeyboardFilter() {
            if (_oldTarget != null) {
                ErrorHandler.ThrowOnFailure(_services.EditorAdaptersFactoryService.GetViewAdapter(_textView).RemoveCommandFilter(this));
                _oldTarget = null;
            }
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
            var session = Volatile.Read(ref _activeSession);
            ISignatureHelpSession sigHelpSession;

            if (pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (int)VSConstants.VSStd2KCmdID.TYPECHAR) {
                var ch = (char)(ushort)System.Runtime.InteropServices.Marshal.GetObjectForNativeVariant(pvaIn);
                bool suppressChar = false;

                if (session != null && !session.IsDismissed) {
                    if (session.SelectedCompletionSet != null &&
                        session.SelectedCompletionSet.SelectionStatus.IsSelected &&
                        _defaultCompletionChars.IndexOf(ch) != -1) {

                        if ((ch == '\\' || ch == '/') && session.SelectedCompletionSet.Moniker == "PythonFilenames") {
                            // We want to dismiss filename completions on slashes
                            // rather than committing them. Then it will probably
                            // be retriggered after the slash is inserted.
                            session.Dismiss();
                        } else {
                            if (ch == session.SelectedCompletionSet.SelectionStatus.Completion.InsertionText.LastOrDefault()) {
                                suppressChar = true;
                            }
                            session.Commit();
                        }
                    } else if (!Tokenizer.IsIdentifierChar(ch)) {
                        session.Dismiss();
                    }
                }

                int res = VSConstants.S_OK;
                if (!suppressChar) {
                    res = _oldTarget != null ? _oldTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) : VSConstants.S_OK;

                    HandleChar(ch);

                    if (session != null && session.IsStarted && !session.IsDismissed) {
                        session.Filter();
                    }
                }

                return res;
            }

            if (session != null) {
                if (pguidCmdGroup == VSConstants.VSStd2K) {
                    switch ((VSConstants.VSStd2KCmdID)nCmdID) {
                        case VSConstants.VSStd2KCmdID.RETURN:
                            if (!session.IsDismissed &&
                                (session.SelectedCompletionSet?.SelectionStatus.IsSelected ?? false)) {

                                // If the user has typed all of the characters as the completion and presses
                                // enter we should dismiss & let the text editor receive the enter.  For example 
                                // when typing "import sys[ENTER]" completion starts after the space.  After typing
                                // sys the user wants a new line and doesn't want to type enter twice.

                                bool enterOnComplete = EnterOnCompleteText(session);

                                session.Commit();

                                if (!enterOnComplete) {
                                    return VSConstants.S_OK;
                                }
                            } else {
                                session.Dismiss();
                            }

                            break;
                        case VSConstants.VSStd2KCmdID.TAB:
                            if (!session.IsDismissed) {
                                session.Commit();
                                return VSConstants.S_OK;
                            }

                            break;
                        case VSConstants.VSStd2KCmdID.BACKSPACE:
                        case VSConstants.VSStd2KCmdID.DELETE:
                        case VSConstants.VSStd2KCmdID.DELETEWORDLEFT:
                        case VSConstants.VSStd2KCmdID.DELETEWORDRIGHT:
                            int res = _oldTarget != null ? _oldTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) : VSConstants.S_OK;
                            if (session != null && session.IsStarted && !session.IsDismissed) {
                                session.Filter();
                            }
                            return res;
                    }
                }
            } else if ((sigHelpSession = Volatile.Read(ref _sigHelpSession)) != null) {
                if (pguidCmdGroup == VSConstants.VSStd2K) {
                    switch ((VSConstants.VSStd2KCmdID)nCmdID) {
                        case VSConstants.VSStd2KCmdID.BACKSPACE:
                            bool fDeleted = Backspace();
                            if (fDeleted) {
                                return VSConstants.S_OK;
                            }
                            break;
                        case VSConstants.VSStd2KCmdID.LEFT:
                            UpdateCurrentParameter();
                            return VSConstants.S_OK;
                        case VSConstants.VSStd2KCmdID.RIGHT:
                            UpdateCurrentParameter();
                            return VSConstants.S_OK;
                        case VSConstants.VSStd2KCmdID.HOME:
                        case VSConstants.VSStd2KCmdID.BOL:
                        case VSConstants.VSStd2KCmdID.BOL_EXT:
                        case VSConstants.VSStd2KCmdID.EOL:
                        case VSConstants.VSStd2KCmdID.EOL_EXT:
                        case VSConstants.VSStd2KCmdID.END:
                        case VSConstants.VSStd2KCmdID.WORDPREV:
                        case VSConstants.VSStd2KCmdID.WORDPREV_EXT:
                        case VSConstants.VSStd2KCmdID.DELETEWORDLEFT:
                            sigHelpSession.Dismiss();
                            break;
                    }
                }
            }

            if (_oldTarget != null) {
                return _oldTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }
            return (int)Constants.OLECMDERR_E_UNKNOWNGROUP;
        }

        private bool EnterOnCompleteText(ICompletionSession session) {
            var selectionStatus = session.SelectedCompletionSet.SelectionStatus;
            var mcaret = session.TextView.MapDownToPythonBuffer(session.TextView.Caret.Position.BufferPosition);
            if (!mcaret.HasValue) {
                return false;
            }
            var caret = mcaret.Value;
            var span = session.SelectedCompletionSet.ApplicableTo.GetSpan(caret.Snapshot);

            return caret == span.End &&
                span.Length == selectionStatus.Completion?.InsertionText.Length &&
                span.GetText() == selectionStatus.Completion.InsertionText;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText) {
            if (_oldTarget != null) {
                return _oldTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
            }
            return (int)Constants.OLECMDERR_E_UNKNOWNGROUP;
        }

        #endregion
    }
}
