using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using IFcViewerRevitPlugin.Views;
using System;
using System.IO;
using System.Reflection;

namespace IFcViewerRevitPlugin
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Commands : IExternalCommand
    {
        public Commands()
        {
            // Load required assemblies
            //LoadLibrary("Xbim.Ifc.dll");
            //LoadLibrary("Xbim.Common.dll");
            //LoadLibrary("Xbim.Presentation.dll");
            LoadLibrary("RestSharp.dll");

            // Set up assembly resolver
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolve;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;

            if (uiDoc == null)
            {
                message = "No active document found. Please open a Revit project first.";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }

            Document doc = uiDoc.Document;

            if (doc.IsFamilyDocument)
            {
                message = "This command cannot be used in a family document.";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }

            try
            {
                // Create and show the viewer window
                ViewerWindow window = new ViewerWindow(uiDoc);
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error opening IFC Viewer: {ex.Message}";
                TaskDialog.Show("Error", $"{message}\n\nDetails:\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        #region Assembly Resolution

        private Assembly AssemblyResolve(object sender, ResolveEventArgs args)
        {
            string assemblyName = new AssemblyName(args.Name).Name;

            if (string.IsNullOrEmpty(assemblyName))
                return null;

            try
            {
                // Get the directory where the plugin is installed
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string assemblyPath = Path.Combine(pluginDir, assemblyName + ".dll");

                if (File.Exists(assemblyPath))
                {
                    return Assembly.LoadFrom(assemblyPath);
                }

                // Check for common variations
                string[] variations = new[]
                {
                    assemblyName + ".dll",
                    assemblyName.Replace(".dll", "") + ".dll"
                };

                foreach (string variation in variations)
                {
                    assemblyPath = Path.Combine(pluginDir, variation);
                    if (File.Exists(assemblyPath))
                    {
                        return Assembly.LoadFrom(assemblyPath);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Assembly resolution failed for {assemblyName}: {ex.Message}");
            }

            return null;
        }

        private static void LoadLibrary(string assemblyName)
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string assemblyPath = Path.Combine(pluginDir, assemblyName);

                if (File.Exists(assemblyPath))
                {
                    Assembly.LoadFrom(assemblyPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to preload {assemblyName}: {ex.Message}");
            }
        }

        #endregion
    }
}