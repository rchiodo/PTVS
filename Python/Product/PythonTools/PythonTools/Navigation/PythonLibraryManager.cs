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
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Project;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Navigation;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.PythonTools.Navigation {

    /// <summary>
    /// This interface defines the service that finds Python files inside a hierarchy
    /// and builds the informations to expose to the class view or object browser.
    /// </summary>
    [Guid(PythonConstants.LibraryManagerServiceGuid)]
    internal interface IPythonLibraryManager : ILibraryManager {
    }

    /// <summary>
    /// Implementation of the service that builds the information to expose to the symbols
    /// navigation tools (class view or object browser) from the Python files inside a
    /// hierarchy.
    /// </summary>
    [Guid(PythonConstants.LibraryManagerGuid)]
    internal class PythonLibraryManager : LibraryManager, IPythonLibraryManager {

        public PythonLibraryManager(CommonPackage/*!*/ package)
            : base(package) {
        }

        public override LibraryNode CreateFileLibraryNode(LibraryNode parent, HierarchyNode hierarchy, string name, string filename) {
            return new PythonFileLibraryNode(parent, hierarchy, hierarchy.Caption, filename);
        }

        public override void RegisterHierarchy(IVsHierarchy hierarchy) {
            var project = hierarchy.GetProject()?.GetPythonProject();
            if (project != null) {
                // Don't really need to do anything here as pylance will already be set
            }

            base.RegisterHierarchy(hierarchy);
        }

        public override void UnregisterHierarchy(IVsHierarchy hierarchy) {
            var project = hierarchy.GetProject()?.GetPythonProject();
            if (project != null) {
            }

            base.UnregisterHierarchy(hierarchy);
        }

        protected override void OnNewFile(LibraryTask task) {
            if (IsNonMemberItem(task.ModuleID.Hierarchy, task.ModuleID.ItemID)) {
                return;
            }

            var project = task.ModuleID.Hierarchy
                    .GetProject()?
                    .GetPythonProject();
            if (project == null) {
                return;
            }
        }
    }
}
