using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Test.Apex;
using Microsoft.Test.Apex.VisualStudio;
using Microsoft.Test.Apex.VisualStudio.Solution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.PythonTools.Apex.IntegrationTests
{
    [TestClass]
    public class AutoCompletion : ApexTest {

        [TestMethod]
        public void TestMethod() {
            // Start an instance of VS
            var visualStudio = this.Operations.CreateHost<VisualStudioHost>();
            visualStudio.Start();

            // Create a new solution and add a C# console app
            visualStudio.ObjectModel.Solution.CreateEmptySolution("HelloWorld", Path.Combine(this.TestExecutionDirectory.FullName, "Assets"));
            var project = visualStudio.ObjectModel.Solution.AddProject(ProjectLanguage.CSharp, ProjectTemplate.ConsoleApplication, "HelloWorld");

            // Open Program.cs and get a handle to the editor
            var item = project["Program.cs"];
            var document = item.GetDocumentAsTextEditor();
            var editor = document.Editor;

            // Add some code to the Main method
            editor.Caret.MoveToExpression("static void Main");
            editor.Caret.MoveDown();
            editor.Caret.MoveToEndOfLine();
            editor.KeyboardCommands.Enter();
            editor.KeyboardCommands.Type("Console.WriteLine(\"Hello World!\");");
            editor.KeyboardCommands.Enter();
            editor.KeyboardCommands.Type("System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));");

            // Run the program in the debugger and wait for it to complete
            visualStudio.ObjectModel.Debugger.Start();
            visualStudio.ObjectModel.Debugger.TryWaitForRunMode(TimeSpan.FromSeconds(30));
            visualStudio.ObjectModel.Debugger.TryWaitForNotRunMode(TimeSpan.FromSeconds(5));

            // Save everything and close
            visualStudio.ObjectModel.Solution.SaveAndClose();
            visualStudio.Stop();
        }
    }
}
