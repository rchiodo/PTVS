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
using Microsoft.CookiecutterTools.Infrastructure;

namespace Microsoft.CookiecutterTools.Interpreters {
    /// <summary>
    /// Specifies the version of the Python language to be used for parsing.
    /// 
    /// Referred to from C++ in PyDebugAttach.cpp and python.h and must be kept in sync
    /// </summary>
    public enum PythonLanguageVersion {
        None = 0,
        V24 = 0x0204,
        V25 = 0x0205,
        V26 = 0x0206,
        V27 = 0x0207,
        V30 = 0x0300,
        V31 = 0x0301,
        V32 = 0x0302,
        V33 = 0x0303,
        V34 = 0x0304,
        V35 = 0x0305,
        V36 = 0x0306,
        V37 = 0x0307,
        V38 = 0x0308,
        V39 = 0x0309,
        V310 = 0x030a,
        V311 = 0x030b,
        V312 = 0x030c,
        V313 = 0x030d,
    }

    public static class PythonLanguageVersionExtensions {
        public static bool Is2x(this PythonLanguageVersion version) {
            return (((int)version >> 8) & 0xff) == 2;
        }

        public static bool Is3x(this PythonLanguageVersion version) {
            return (((int)version >> 8) & 0xff) == 3;
        }

        public static bool IsNone(this PythonLanguageVersion version) {
            return version == PythonLanguageVersion.None;
        }

        public static Version ToVersion(this PythonLanguageVersion version) {
            return new Version(((int)version) >> 8, ((int)version) & 0xff);
        }

        public static PythonLanguageVersion ToLanguageVersion(this Version version) {
            switch (version.Major) {
                case 0:
                    switch (version.Minor) {
                        case 0: return PythonLanguageVersion.None;
                    }
                    break;
                case 2:
                    switch (version.Minor) {
                        case 4: return PythonLanguageVersion.V24;
                        case 5: return PythonLanguageVersion.V25;
                        case 6: return PythonLanguageVersion.V26;
                        case 7: return PythonLanguageVersion.V27;
                    }
                    break;
                case 3:
                    switch (version.Minor) {
                        case 0: return PythonLanguageVersion.V30;
                        case 1: return PythonLanguageVersion.V31;
                        case 2: return PythonLanguageVersion.V32;
                        case 3: return PythonLanguageVersion.V33;
                        case 4: return PythonLanguageVersion.V34;
                        case 5: return PythonLanguageVersion.V35;
                        case 6: return PythonLanguageVersion.V36;
                        case 7: return PythonLanguageVersion.V37;
                        case 8: return PythonLanguageVersion.V38;
                        case 9: return PythonLanguageVersion.V39;
                        case 10: return PythonLanguageVersion.V310;
                        case 11: return PythonLanguageVersion.V311;
                        case 12: return PythonLanguageVersion.V312;
                        case 13: return PythonLanguageVersion.V313;
                    }
                    break;
            }
            throw new InvalidOperationException("Unsupported Python version: {0}".FormatInvariant(version));
        }

    }
}
