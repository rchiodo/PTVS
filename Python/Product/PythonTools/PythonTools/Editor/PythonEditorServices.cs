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
using System.ComponentModel.Composition;
using Microsoft.Internal.VisualStudio.Shell.Interop;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.IncrementalSearch;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.PatternMatching;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudioTools;

namespace Microsoft.PythonTools.Editor {
    /// <summary>
    /// Provides centralized access to services used by the editor.
    /// </summary>
    /// <remarks>
    /// MEF services provided by VS should be imported eagerly using the
    /// [Import] attribute on a public field.
    /// 
    /// Traditional services should be lazily imported on first use, and
    /// uses should be audited to ensure they all occur on the UI thread.
    /// 
    /// Services provided by PTVS should be lazily loaded on first access.
    /// Otherwise, we may end up with circular imports in MEF composition.
    /// </remarks>
    [Export]
    sealed class PythonEditorServices {
        [ImportingConstructor]
        public PythonEditorServices([Import(typeof(SVsServiceProvider))] IServiceProvider site) {
            Site = site;
            _componentModel = new Lazy<IComponentModel>(site.GetComponentModel);
            _featureFlags = new Lazy<IVsFeatureFlags>(() => (IVsFeatureFlags)site.GetService(typeof(SVsFeatureFlags)));
        }

        public readonly IServiceProvider Site;

        #region PythonToolsService

        private PythonToolsService _python;

        internal void SetPythonToolsService(PythonToolsService service) {
            if (_python != null) {
                throw new InvalidOperationException("Multiple services created");
            }
            _python = service;
        }

        internal PythonToolsService TryGetPythonToolsService() {
            return _python = Site.GetUIThread().Invoke(() => Site.GetPythonToolsService());
        }

        public PythonToolsService Python => _python ?? TryGetPythonToolsService();

        #endregion

        public PythonTextBufferInfo GetBufferInfo(ITextBuffer textBuffer) {
            return PythonTextBufferInfo.ForBuffer(Site, textBuffer);
        }

        private readonly Lazy<IComponentModel> _componentModel;
        public IComponentModel ComponentModel => _componentModel.Value;

        [Import]
        public IClassificationTypeRegistryService ClassificationTypeRegistryService;
        [Import]
        public IContentTypeRegistryService ContentTypeRegistryService;

        [Import]
        public IInterpreterOptionsService InterpreterOptionsService;

        [Import]
        public IInterpreterRegistryService InterpreterRegistryService;

        [Import]
        private Lazy<IVsEditorAdaptersFactoryService> _editorAdaptersFactoryService = null;
        public IVsEditorAdaptersFactoryService EditorAdaptersFactoryService => _editorAdaptersFactoryService.Value;

        [Import]
        public ICompletionBroker CompletionBroker = null;

        [Import(AllowDefault = true)]
        public IEditorOptionsFactoryService EditorOptionsFactoryService = null;

        [Import]
        public IEditorOperationsFactoryService EditOperationsFactory = null;

        [Import]
        public ISignatureHelpBroker SignatureHelpBroker = null;

        [Import]
        public IAsyncQuickInfoBroker QuickInfoBroker = null;

        [Import]
        public IPeekBroker PeekBroker = null;

        [Import]
        public IIncrementalSearchFactoryService IncrementalSearch = null;

        [Import]
        public ITextMarkerProviderFactory TextMarkerProviderFactory = null;

        [Import]
        public ITextBufferUndoManagerProvider UndoManagerFactory = null;

        [Import]
        public IGlyphService GlyphService = null;

#if !USE_15_5
        [Import(AllowDefault = true)]
        private Lazy<IPatternMatcherFactory> _patternMatcherFactory = null;
        public IPatternMatcherFactory PatternMatcherFactory => _patternMatcherFactory.Value;
#endif

        private Lazy<IVsFeatureFlags> _featureFlags;
        internal IVsFeatureFlags FeatureFlags => _featureFlags.Value;

        public IVsTextManager2 VsTextManager2 => (IVsTextManager2)Site.GetService(typeof(SVsTextManager));
    }
}
