using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using IFcViewerRevitPlugin.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace IFcViewerRevitPlugin
{
    /// <summary>
    /// External Event Handler for IFC export with performance optimizations
    /// </summary>
    public class ViewerExternalEventHandler : IExternalEventHandler
    {
        private Document _doc;
        private Action<string> _onIfcExported;
        private static Dictionary<string, string> _exportCache = new Dictionary<string, string>();

        public void SetContext(Document doc, Action<string> onIfcExported)
        {
            _doc = doc;
            _onIfcExported = onIfcExported;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                if (_doc == null) return;

                string ifcPath = ExportRevitToIfcOptimized(_doc);

                if (!string.IsNullOrEmpty(ifcPath) && File.Exists(ifcPath))
                {
                    _onIfcExported?.Invoke(ifcPath);
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Export Error", $"Error: {ex.Message}");
            }
        }

        public string GetName()
        {
            return "IFC Viewer External Event";
        }

        private string ExportRevitToIfcOptimized(Document doc)
        {
            if (doc == null) return null;

            string tempPath = Path.Combine(Path.GetTempPath(), "RevitIFCExport");
            if (!Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);

            // Generate cache key based on document identity
            string cacheKey = GetDocumentCacheKey(doc);

            // Check if we have a recent cached export (within last 5 minutes)
            if (_exportCache.ContainsKey(cacheKey))
            {
                string cachedPath = _exportCache[cacheKey];
                if (File.Exists(cachedPath))
                {
                    var fileInfo = new FileInfo(cachedPath);
                    if (DateTime.Now - fileInfo.LastWriteTime < TimeSpan.FromMinutes(5))
                    {
                        TaskDialog.Show("IFC Export", $"Using cached export from {fileInfo.LastWriteTime:HH:mm:ss}",
                            TaskDialogCommonButtons.Ok);
                        return cachedPath;
                    }
                }
            }

            string fileName = $"{SanitizeFileName(doc.Title)}_{DateTime.Now:yyyyMMdd_HHmmss}.ifc";
            string fullPath = Path.Combine(tempPath, fileName);

            try
            {
                // Optimized IFC export options for performance
                IFCExportOptions ifcOptions = new IFCExportOptions
                {
                    FileVersion = IFCVersion.IFC2x3,
                    WallAndColumnSplitting = false,  // Faster export
                    ExportBaseQuantities = false,     // Skip unnecessary data
                    SpaceBoundaryLevel = 2,           // Include spaces but minimal boundaries
                };

                // Optional: Create filtered view for specific categories only
                // Uncomment if you want to export only certain categories
                View filteredView = CreateFilteredView(doc);
                if (filteredView != null)
                {
                    ifcOptions.FilterViewId = filteredView.Id;
                }

                using (var trans = new Transaction(doc, "Export IFC"))
                {
                    trans.Start();
                    doc.Export(tempPath, fileName, ifcOptions);
                    trans.Commit();
                }

                // Cache the export path
                _exportCache[cacheKey] = fullPath;

                // Clean up old cached files (keep last 3)
                CleanupOldExports(tempPath, 3);

                return fullPath;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Export Error", $"Error exporting to IFC: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Generate a unique cache key for the document based on path and modified time
        /// </summary>
        private string GetDocumentCacheKey(Document doc)
        {
            try
            {
                string path = doc.PathName;
                if (string.IsNullOrEmpty(path))
                {
                    // For unsaved documents, use title + instance
                    return $"unsaved_{doc.Title}_{doc.GetHashCode()}";
                }

                // Use file path + last modified time
                var fileInfo = new FileInfo(path);
                return $"{path}_{fileInfo.LastWriteTime.Ticks}";
            }
            catch
            {
                return $"doc_{doc.Title}_{doc.GetHashCode()}";
            }
        }

        /// <summary>
        /// Clean up old IFC export files, keeping only the most recent N files
        /// </summary>
        private void CleanupOldExports(string directory, int keepCount)
        {
            try
            {
                var files = Directory.GetFiles(directory, "*.ifc")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                // Delete files beyond the keep count
                foreach (var file in files.Skip(keepCount))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                        // Ignore errors deleting old files
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        /// <summary>
        /// Optional: Create a filtered 3D view for specific categories
        /// Uncomment and use if you want to export only certain elements
        /// </summary>
        private View CreateFilteredView(Document doc)
        {
            try
            {
                // Find default 3D view
                View3D default3D = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name == "{3D}");

                if (default3D == null) return null;

                View3D filteredView = null;
                ElementId viewId = null;

                using (var trans = new Transaction(doc, "Create Filtered View"))
                {
                    trans.Start();

                    viewId = default3D.Duplicate(ViewDuplicateOption.Duplicate);
                    filteredView = doc.GetElement(viewId) as View3D;

                    if (filteredView != null)
                    {
                        filteredView.Name = $"IFC Export Filter - {Guid.NewGuid().ToString().Substring(0, 8)}";

                        // Define which categories to export
                        var categoriesToShow = new[]
                        {
                            BuiltInCategory.OST_Walls,
                            BuiltInCategory.OST_Floors,
                            BuiltInCategory.OST_Ceilings,
                            BuiltInCategory.OST_Rooms,
                        };

                        // Hide all categories except the ones we want
                        foreach (Category cat in doc.Settings.Categories)
                        {
                            if (cat.CategoryType == CategoryType.Model &&
                                cat.get_AllowsVisibilityControl(filteredView))
                            {
                                bool shouldShow = categoriesToShow.Contains((BuiltInCategory)cat.Id.IntegerValue);

                                try
                                {
                                    filteredView.SetCategoryHidden(cat.Id, !shouldShow);
                                }
                                catch
                                {
                                    // Some categories can't be hidden
                                }
                            }
                        }
                    }

                    trans.Commit();
                }

                return filteredView;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating filtered view: {ex.Message}");
                return null;
            }
        }

        private string SanitizeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }
    }

    /// <summary>
    /// Main command - creates modeless window with optimizations
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Commands : IExternalCommand
    {
        private static ExternalEvent _externalEvent;
        private static ViewerExternalEventHandler _eventHandler;
        private static ViewerWindow _viewerWindow;

        public Commands()
        {
            LoadLibrary("RestSharp.dll");
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
                // Initialize external event handler if not exists
                if (_eventHandler == null)
                {
                    _eventHandler = new ViewerExternalEventHandler();
                    _externalEvent = ExternalEvent.Create(_eventHandler);
                }

                // Check if window exists and is open
                if (_viewerWindow != null)
                {
                    try
                    {
                        // Try to activate existing window
                        _viewerWindow.Activate();
                        _viewerWindow.Focus();
                        return Result.Succeeded;
                    }
                    catch
                    {
                        // Window was closed or disposed, create new one
                        _viewerWindow = null;
                    }
                }

                // Create new viewer window on Revit's UI thread
                _viewerWindow = new ViewerWindow(doc);
                _viewerWindow.SetExternalEvent(_externalEvent, _eventHandler);

                // Show as modeless
                _viewerWindow.Show();

                // Handle window closed event
                _viewerWindow.Closed += (s, e) => { _viewerWindow = null; };

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
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string assemblyPath = Path.Combine(pluginDir, assemblyName + ".dll");

                if (File.Exists(assemblyPath))
                {
                    return Assembly.LoadFrom(assemblyPath);
                }

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