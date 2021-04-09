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
using System.Linq;
using Microsoft.PythonTools.Editor.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using LSP = Microsoft.VisualStudio.LanguageServer.Protocol;


namespace Microsoft.PythonTools.Intellisense {
    class NavigationInfo {
        public readonly string Name;
        public readonly SnapshotSpan Span;
        public readonly NavigationInfo[] Children;
        public readonly NavigationKind Kind;

        public static readonly NavigationInfo Empty = new NavigationInfo(null, NavigationKind.None, new SnapshotSpan(), Array.Empty<NavigationInfo>());

        public NavigationInfo(string name, NavigationKind kind, SnapshotSpan span, NavigationInfo[] children) {
            Name = name;
            Kind = kind;
            Span = span;
            Children = children;
        }

        public static NavigationInfo FromDocumentSymbols(object result, ITextView textView) {
            var documentSymbols = result as LSP.DocumentSymbol[];
            var symbols = result as LSP.SymbolInformation[];
            if (documentSymbols != null) {
                return new NavigationInfo(
                    null,
                    NavigationKind.None,
                    new SnapshotSpan(),
                    documentSymbols.Select(s => FromDocumentSymbol(s, textView)).ToArray());
            }
            if (symbols != null) {
                return new NavigationInfo(
                    null,
                    NavigationKind.None,
                    new SnapshotSpan(),
                    symbols.Select(s => FromDocumentSymbol(s, textView)).ToArray());
            }
            return NavigationInfo.Empty;
        }

        public static NavigationInfo FromDocumentSymbol(object result, ITextView textView) {
            var documentSymbol = result as LSP.DocumentSymbol;
            var symbol = result as LSP.SymbolInformation;
            if (documentSymbol != null) {
                return new NavigationInfo(
                    documentSymbol.Name,
                    KindFromSymbol(documentSymbol.Kind),
                    textView.GetSnapshotSpan(documentSymbol.Range),
                    documentSymbol.Children != null ? 
                        documentSymbol.Children.Select(c => FromDocumentSymbol(c, textView)).ToArray() : 
                        new NavigationInfo[0]);
            } 
            if (symbol != null && symbol.Location.Uri.LocalPath == textView.GetPath()) {
                return new NavigationInfo(
                    symbol.Name,
                    KindFromSymbol(symbol.Kind),
                    textView.GetSnapshotSpan(symbol.Location.Range),
                    new NavigationInfo[0]);
            }
            return NavigationInfo.Empty;
        }

        private static NavigationKind KindFromSymbol(LSP.SymbolKind documentSymbolKind) {
            switch (documentSymbolKind) {
                case LSP.SymbolKind.Class:
                    return NavigationKind.Class;

                case LSP.SymbolKind.Function:
                    return NavigationKind.Function;

                case LSP.SymbolKind.Method:
                    return NavigationKind.ClassMethod;

                case LSP.SymbolKind.Property:
                    return NavigationKind.Property;

                default:
                    return NavigationKind.None;
            }
        }
    }

    enum NavigationKind {
        None,
        Class,
        Function,
        StaticMethod,
        ClassMethod,
        Property
    }
}
