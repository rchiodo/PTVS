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
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Editor;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio.Text;

namespace Microsoft.PythonTools.Intellisense {
    using AP = AnalysisProtocol;

    sealed class BufferParser : IPythonTextBufferInfoEventSink, IDisposable {
        private readonly Timer _timer;
        internal readonly PythonEditorServices _services;
        private readonly VsProjectAnalyzer _analyzer;

        private IList<PythonTextBufferInfo> _buffers;
        private bool _parsing, _requeue, _textChange, _parseImmediately;

        /// <summary>
        /// Maps between buffer ID and buffer info.
        /// </summary>
        private Dictionary<int, PythonTextBufferInfo> _bufferIdMapping = new Dictionary<int, PythonTextBufferInfo>();

        private const int ReparseDelay = 1000;      // delay in MS before we re-parse a buffer w/ non-line changes.

        public static readonly object DoNotParse = new object();
        public static readonly object ParseImmediately = new object();

        public BufferParser(PythonEditorServices services, VsProjectAnalyzer analyzer, string filePath) {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            FilePath = filePath;
            _buffers = Array.Empty<PythonTextBufferInfo>();
            _timer = new Timer(ReparseTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public string FilePath { get; }
        public bool IsTemporaryFile { get; set; }
        public bool SuppressErrorList { get; set; }

        public PythonTextBufferInfo GetBuffer(ITextBuffer buffer) {
            return buffer == null ? null : _services.GetBufferInfo(buffer);
        }

        public PythonTextBufferInfo GetBuffer(int bufferId) {
            lock (this) {
                PythonTextBufferInfo res;
                _bufferIdMapping.TryGetValue(bufferId, out res);
                return res;
            }
        }

        /// <summary>
        /// Indicates that the specified buffer ID has been analyzed with this version.
        /// </summary>
        /// <returns>
        /// True if the specified version is newer than the last one we had received.
        /// </returns>
        public bool Analyzed(int bufferId, int version) {
            return GetBuffer(bufferId)?.UpdateLastReceivedAnalysis(version) ?? false;
        }

        /// <summary>
        /// Indicates that the specified buffer ID has been parsed with this version.
        /// </summary>
        /// <returns>
        /// True if the specified version is newer than the last one we had received.
        /// </returns>
        public bool Parsed(int bufferId, int version) {
            return GetBuffer(bufferId)?.UpdateLastReceivedParse(version) ?? false;
        }

        internal ITextSnapshot GetLastSentSnapshot(ITextBuffer buffer) {
            return GetBuffer(buffer)?.LastSentSnapshot;
        }

        public ITextBuffer[] AllBuffers {
            get {
                return _buffers.Select(x => x.Buffer).ToArray();
            }
        }

        public ITextBuffer[] Buffers {
            get {
                return _buffers.Where(x => !x.DoNotParse).Select(x => x.Buffer).ToArray();
            }
        }

        internal void AddBuffer(ITextBuffer textBuffer) {
            int newId;
            var bi = _services.GetBufferInfo(textBuffer);

            var entry = bi.AnalysisEntry;

            if (entry == null) {
                throw new InvalidOperationException("buffer must have a project entry before parsing");
            }

            lock (this) {
                if (_buffers.Contains(bi)) {
                    return;
                }

                EnsureMutableBuffers();
                _buffers.Add(bi);
                newId = _buffers.Count - 1;

                if (!bi.SetAnalysisBufferId(newId)) {
                    // Raced, and now the buffer belongs somewhere else.
                    Debug.Fail("Race condition adding the buffer to a parser");
                    _buffers[newId] = null;
                    return;
                }
                _bufferIdMapping[newId] = bi;
            }

            if (bi.ParseImmediately) {
                // Any buffer requesting immediate parsing enables it for
                // the whole file.
                _parseImmediately = true;
            }

            bi.AddSink(this, this);
            VsProjectAnalyzer.ConnectErrorList(bi);
        }

        internal void ClearBuffers() {
            lock (this) {
                _bufferIdMapping.Clear();
                foreach (var bi in _buffers) {
                    bi.SetAnalysisBufferId(-1);
                    bi.ClearAnalysisEntry();
                    bi.RemoveSink(this);
                    VsProjectAnalyzer.DisconnectErrorList(bi);
                }
                _buffers = Array.Empty<PythonTextBufferInfo>();
            }
        }

        internal int RemoveBuffer(ITextBuffer subjectBuffer) {
            int result;
            var bi = PythonTextBufferInfo.TryGetForBuffer(subjectBuffer);

            lock (this) {
                if (bi != null) {
                    EnsureMutableBuffers();
                    _buffers.Remove(bi);

                    bi.RemoveSink(this);

                    VsProjectAnalyzer.DisconnectErrorList(bi);
                    _bufferIdMapping.Remove(bi.AnalysisBufferId);
                    bi.SetAnalysisBufferId(-1);

                    bi.Buffer.Properties.RemoveProperty(typeof(PythonTextBufferInfo));
                }
                result = _buffers.Count;
            }

            return result;
        }

        private void EnsureMutableBuffers() {
            if (_buffers.IsReadOnly) {
                _buffers = new List<PythonTextBufferInfo>(_buffers);
            }
        }

        internal void ReparseTimer(object unused) {
            RequeueWorker();
        }

        internal void ReparseWorker(object unused) {
            ITextSnapshot[] snapshots;
            lock (this) {
                if (_parsing) {
                    return;
                }

                _parsing = true;
                snapshots = _buffers.Where(b => !b.DoNotParse).Select(b => b.CurrentSnapshot).ToArray();
            }

            ParseBuffers(snapshots).WaitAndHandleAllExceptions(_services.Site);

            lock (this) {
                _parsing = false;
                if (_requeue) {
                    RequeueWorker();
                }
                _requeue = false;
            }
        }

        public async Task EnsureCodeSyncedAsync(ITextBuffer buffer) {
            var lastSent = GetLastSentSnapshot(buffer);
            var snapshot = buffer.CurrentSnapshot;
            if (lastSent != buffer.CurrentSnapshot) {
                await ParseBuffers(Enumerable.Repeat(snapshot, 1));
            }
        }

        private Task ParseBuffers(IEnumerable<ITextSnapshot> snapshots) {
            return ParseBuffersAsync(_services, _analyzer, snapshots);
        }

        private static IEnumerable<ITextVersion> GetVersions(ITextVersion from, ITextVersion to) {
            for (var v = from; v != null && v != to; v = v.Next) {
                yield return v;
            }
        }

        private static AP.FileUpdate GetUpdateForSnapshot(PythonEditorServices services, ITextSnapshot snapshot) {
            var buffer = services.GetBufferInfo(snapshot.TextBuffer);
            if (buffer.DoNotParse || snapshot.IsReplBufferWithCommand() || buffer.AnalysisBufferId < 0) {
                return null;
            }

            var lastSent = buffer.LastSentSnapshot;

            if (lastSent?.Version == snapshot.Version) {
                // this snapshot is up to date...
                return null;
            }

            // Update last sent snapshot and the analysis cookie to our
            // current snapshot.
            buffer.LastSentSnapshot = snapshot;
            var entry = buffer.AnalysisEntry;
            if (entry != null) {
                entry.AnalysisCookie = new SnapshotCookie(snapshot);
            }

            if (lastSent == null || lastSent.TextBuffer != buffer.Buffer) {
                // First time parsing from a live buffer, send the entire
                // file and set our initial snapshot.  We'll roll forward
                // to new snapshots when we receive the errors event.  This
                // just makes sure that the content is in sync.
                return new AP.FileUpdate {
                    content = snapshot.GetText(),
                    version = snapshot.Version.VersionNumber,
                    bufferId = buffer.AnalysisBufferId,
                    kind = AP.FileUpdateKind.reset
                };
            }

            var versions = GetVersions(lastSent.Version, snapshot.Version).Select(v => new AP.VersionChanges{
                changes = GetChanges(v)
            }).ToArray();

            return new AP.FileUpdate() {
                versions = versions,
                version = snapshot.Version.VersionNumber,
                bufferId = buffer.AnalysisBufferId,
                kind = AP.FileUpdateKind.changes
            };
        }

        [Conditional("DEBUG")]
        private static void ValidateBufferContents(IEnumerable<ITextSnapshot> snapshots, Dictionary<int, string> code) {
            foreach (var snapshot in snapshots) {
                var bi = PythonTextBufferInfo.TryGetForBuffer(snapshot.TextBuffer);
                if (bi == null) {
                    continue;
                }

                string newCode;
                if (!code.TryGetValue(bi.AnalysisBufferId, out newCode)) {
                    continue;
                }

                Debug.Assert(newCode.TrimEnd() == snapshot.GetText().TrimEnd(), "Buffer content mismatch");
            }
        }

        internal static async Task ParseBuffersAsync(
            PythonEditorServices services,
            VsProjectAnalyzer analyzer,
            IEnumerable<ITextSnapshot> snapshots
        ) {
            var updates = snapshots
                .GroupBy(s => PythonTextBufferInfo.TryGetForBuffer(s.TextBuffer)?.AnalysisEntry.FileId ?? -1)
                .Where(g => g.Key >= 0)
                .Select(g => Tuple.Create(g.Key, g.Select(s => GetUpdateForSnapshot(services, s)).ToArray()))
                .Where(u => u != null).ToList();

            if (!updates.Any()) {
                return;
            }

            analyzer._analysisComplete = false;
            Interlocked.Increment(ref analyzer._parsePending);

            foreach (var update in updates) {
                var res = await analyzer.SendRequestAsync(
                    new AP.FileUpdateRequest() {
                        fileId = update.Item1,
                        updates = update.Item2
                    }
                );

                if (res != null) {
                    Debug.Assert(res.failed != true);
                    analyzer.OnAnalysisStarted();
                    ValidateBufferContents(snapshots, res.newCode);
                } else {
                    Interlocked.Decrement(ref analyzer._parsePending);
                }
            }
        }

        private static AP.ChangeInfo[] GetChanges(ITextVersion curVersion) {
            Debug.WriteLine("Changes for version {0}", curVersion.VersionNumber);
            var changes = new List<AP.ChangeInfo>();
            if (curVersion.Changes != null) {
                foreach (var change in curVersion.Changes) {
                    Debug.WriteLine("Changes for version {0} {1} {2}", change.OldPosition, change.OldLength, change.NewText);
                    
                    changes.Add(
                        new AP.ChangeInfo() {
                            start = change.OldPosition,
                            length = change.OldLength,
                            newText = change.NewText
                        }
                    );
                }
            }
            return changes.ToArray();
        }

        internal void Requeue() {
            RequeueWorker();
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void RequeueWorker() {
            ThreadPool.QueueUserWorkItem(ReparseWorker);
        }

        /// <summary>
        /// Used to track if we have line + text changes, just text changes, or just line changes.
        /// 
        /// If we have text changes followed by a line change we want to immediately reparse.
        /// If we have just text changes we want to reparse in ReparseDelay ms from the last change.
        /// If we have just repeated line changes (e.g. someone's holding down enter) we don't want to
        ///     repeatedly reparse, instead we want to wait ReparseDelay ms.
        /// </summary>
        private bool LineAndTextChanges(TextContentChangedEventArgs e) {
            if (_textChange) {
                _textChange = false;
                return e.Changes.IncludesLineChanges;
            }

            bool mixedChanges = false;
            if (e.Changes.IncludesLineChanges) {
                mixedChanges = IncludesTextChanges(e);
            }

            return mixedChanges;
        }

        /// <summary>
        /// Returns true if the change incldues text changes (not just line changes).
        /// </summary>
        private static bool IncludesTextChanges(TextContentChangedEventArgs e) {
            bool mixedChanges = false;
            foreach (var change in e.Changes) {
                if (!string.IsNullOrEmpty(change.OldText) || change.NewText != Environment.NewLine) {
                    mixedChanges = true;
                    break;
                }
            }
            return mixedChanges;
        }

        public void Dispose() {
            foreach (var buffer in _buffers.ToArray()) {
                RemoveBuffer(buffer.Buffer);
            }
            _timer.Dispose();
        }

        Task IPythonTextBufferInfoEventSink.PythonTextBufferEventAsync(PythonTextBufferInfo sender, PythonTextBufferInfoEventArgs e) {
            switch (e.Event) {
                case PythonTextBufferInfoEvents.TextContentChangedLowPriority:
                    lock (this) {
                        // only immediately re-parse on line changes after we've seen a text change.
                        var ne = (e as PythonTextBufferInfoNestedEventArgs)?.NestedEventArgs as TextContentChangedEventArgs;

                        if (_parsing) {
                            // we are currently parsing, just reque when we complete
                            _requeue = true;
                            _timer.Change(Timeout.Infinite, Timeout.Infinite);
                        } else if (_parseImmediately) {
                            // we are a test buffer, we should requeue immediately
                            Requeue();
                        } else if (ne == null) {
                            // failed to get correct type for this event
                            Debug.Fail("Failed to get correct event type");
                        } else if (LineAndTextChanges(ne)) {
                            // user pressed enter, we should requeue immediately
                            Requeue();
                        } else {
                            // parse if the user doesn't do anything for a while.
                            _textChange = IncludesTextChanges(ne);
                            _timer.Change(ReparseDelay, Timeout.Infinite);
                        }
                    }
                    break;

                case PythonTextBufferInfoEvents.DocumentEncodingChanged:
                    lock (this) {
                        if (_parsing) {
                            // we are currently parsing, just reque when we complete
                            _requeue = true;
                            _timer.Change(Timeout.Infinite, Timeout.Infinite);
                        } else {
                            Requeue();
                        }
                    }
                    break;
            }
            return Task.CompletedTask;
        }
    }
}
