using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using IFcViewerRevitPlugin.Views;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xbim.Ifc4.Interfaces;

namespace IFcViewerRevitPlugin
{
    /// <summary>
    /// External Event Handler with support for full model and room-scoped exports
    /// </summary>
    public class ViewerExternalEventHandler : IExternalEventHandler
    {
        private Document _doc;
        private Action<string> _onIfcExported;
        private IIfcSpace _roomScope; // For room-scoped exports
        private static Dictionary<string, string> _exportCache = new Dictionary<string, string>();
        private static Dictionary<string, string> _roomExportCache = new Dictionary<string, string>();

        public void SetContext(Document doc, Action<string> onIfcExported)
        {
            _doc = doc;
            _onIfcExported = onIfcExported;
            _roomScope = null; // Full model export
        }

        public void SetRoomContext(Document doc, IIfcSpace room, Action<string> onIfcExported)
        {
            _doc = doc;
            _onIfcExported = onIfcExported;
            _roomScope = room; // Room-scoped export
        }

        public void Execute(UIApplication app)
        {
            try
            {
                if (_doc == null) return;

                string ifcPath;

                if (_roomScope != null)
                {
                    // Export room-scoped IFC
                    ifcPath = ExportRoomScopedIfc(_doc, _roomScope);
                }
                else
                {
                    // Export full model IFC
                    ifcPath = ExportFullModelIfc(_doc);
                }

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

        private string ExportFullModelIfc(Document doc)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "RevitIFCExport");
            if (!Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);

            // Check cache
            string cacheKey = GetDocumentCacheKey(doc);
            if (_exportCache.ContainsKey(cacheKey))
            {
                string cachedPath = _exportCache[cacheKey];
                if (File.Exists(cachedPath))
                {
                    var fileInfo = new FileInfo(cachedPath);
                    if (DateTime.Now - fileInfo.LastWriteTime < TimeSpan.FromMinutes(5))
                    {
                        return cachedPath;
                    }
                }
            }

            string fileName = $"{SanitizeFileName(doc.Title)}_{DateTime.Now:yyyyMMdd_HHmmss}.ifc";
            string fullPath = Path.Combine(tempPath, fileName);


            try
            {
                IFCExportOptions ifcOptions = new IFCExportOptions
                {
                    FileVersion = IFCVersion.IFC2x3,
                    WallAndColumnSplitting = false,
                    ExportBaseQuantities = false,
                    SpaceBoundaryLevel = 2
                };

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

                // Cache the export
                _exportCache[cacheKey] = fullPath;
                CleanupOldExports(tempPath, 3);

                return fullPath;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Export Error", $"Error exporting full model: {ex.Message}");
                return null;
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

        private string ExportRoomScopedIfc(Document doc, IIfcSpace space)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "RevitIFCExport", "Rooms");
            if (!Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);

            // Check room cache
            string roomName = space.Name ?? space.LongName ?? $"Space_{space.EntityLabel}";
            string roomCacheKey = $"{GetDocumentCacheKey(doc)}_{roomName}";

            if (_roomExportCache.ContainsKey(roomCacheKey))
            {
                string cachedPath = _roomExportCache[roomCacheKey];
                if (File.Exists(cachedPath))
                {
                    var fileInfo = new FileInfo(cachedPath);
                    if (DateTime.Now - fileInfo.LastWriteTime < TimeSpan.FromMinutes(10))
                    {
                        return cachedPath;
                    }
                }
            }

            try
            {
                Room revitRoom = FindRevitRoomByName(doc, roomName);
                if (revitRoom == null) return null;

                // Create the section box view
                View3D roomView = CreateRoomScopedView(doc, revitRoom);
                if (roomView == null) return null;

                string fileName = $"{SanitizeFileName(doc.Title)}_Room_{SanitizeFileName(roomName)}_{DateTime.Now:yyyyMMdd_HHmmss}.ifc";
                string fullPath = Path.Combine(tempPath, fileName);

                // CRITICAL: Use these export options for proper section box clipping
                IFCExportOptions ifcOptions = new IFCExportOptions
                {
                    FileVersion = IFCVersion.IFC2x3CV2,
                    WallAndColumnSplitting = true,  // This enables geometry splitting at section box boundaries
                    ExportBaseQuantities = false,
                    SpaceBoundaryLevel = 1,
                    FilterViewId = roomView.Id,     // Use our section box view
                };

                using (var trans = new Transaction(doc, "Export Room IFC"))
                {
                    trans.Start();
                    doc.Export(tempPath, fileName, ifcOptions);
                    trans.Commit();
                }

                // Clean up the temporary view
                using (var trans = new Transaction(doc, "Delete Temp View"))
                {
                    trans.Start();
                    try { doc.Delete(roomView.Id); } catch { }
                    trans.Commit();
                }

                // Cache and return
                _roomExportCache[roomCacheKey] = fullPath;
                CleanupOldExports(tempPath, 10);

                return fullPath;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Export Error", $"Error exporting room '{roomName}': {ex.Message}");
                return null;
            }
        }

        private Room FindRevitRoomByName(Document doc, string roomName)
        {
            try
            {
                // Find room by name or number
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => !string.IsNullOrEmpty(r.Name) || !string.IsNullOrEmpty(r.Number))
                    .ToList();

                // Try exact match on name
                var room = rooms.FirstOrDefault(r => r.Name == roomName);
                if (room != null) return room;

                // Try match on number
                room = rooms.FirstOrDefault(r => r.Number == roomName);
                if (room != null) return room;

                // Try partial match
                room = rooms.FirstOrDefault(r =>
                    (r.Name != null && r.Name.Contains(roomName)) ||
                    (r.Number != null && r.Number.Contains(roomName)));

                return room;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding room: {ex.Message}");
                return null;
            }
        }

        private View3D CreateRoomScopedView(Document doc, Room room)
        {
            try
            {
                // Get room bounding box - this is the key!
                BoundingBoxXYZ roomBBox = room.get_BoundingBox(null);

                if (roomBBox == null)
                {
                    // Try alternative method to get room geometry
                    var options = new Autodesk.Revit.DB.Options();
                    options.ComputeReferences = true;
                    options.IncludeNonVisibleObjects = true;

                    GeometryElement geomElem = room.get_Geometry(options);
                    if (geomElem != null)
                    {
                        foreach (GeometryObject geomObj in geomElem)
                        {
                            if (geomObj is Solid solid && solid.Faces.Size > 0)
                            {
                                roomBBox = solid.GetBoundingBox();
                                break;
                            }
                        }
                    }
                }

                if (roomBBox == null)
                {
                    TaskDialog.Show("Room Bounds", "Could not determine room boundaries");
                    return null;
                }

                // Find or create 3D view type
                View3D default3D = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name == "{3D}");

                if (default3D == null)
                {
                    return null;
                }

                View3D roomView = null;
                ElementId viewId = null;

                using (var trans = new Transaction(doc, "Create Room Section View"))
                {
                    trans.Start();

                    // Duplicate the default 3D view
                    viewId = default3D.Duplicate(ViewDuplicateOption.Duplicate);
                    roomView = doc.GetElement(viewId) as View3D;

                    if (roomView != null)
                    {
                        roomView.Name = $"Room_{room.Name ?? room.Number}_{Guid.NewGuid().ToString().Substring(0, 6)}";

                        // CRITICAL: Enable section box
                        roomView.IsSectionBoxActive = true;

                        // Get room bounds with proper padding
                        XYZ min = roomBBox.Min;
                        XYZ max = roomBBox.Max;

                        // Add padding to include walls, doors, windows
                        double padding = 2.0; // Increased padding to ensure we capture adjacent elements
                        XYZ paddingVector = new XYZ(padding, padding, padding);

                        min = min - paddingVector;
                        max = max + paddingVector;

                        // Ensure we have a valid volume
                        if (max.X - min.X < 0.1) max = new XYZ(min.X + 1.0, max.Y, max.Z);
                        if (max.Y - min.Y < 0.1) max = new XYZ(max.X, min.Y + 1.0, max.Z);
                        if (max.Z - min.Z < 0.1) max = new XYZ(max.X, max.Y, min.Z + 1.0);

                        // Create and set section box
                        BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
                        {
                            Min = min,
                            Max = max,
                            Transform = Transform.Identity // World coordinates
                        };

                        roomView.SetSectionBox(sectionBox);

                        // Set appropriate detail level
                        roomView.DetailLevel = ViewDetailLevel.Medium;

                        // Hide unnecessary categories for cleaner export
                        HideUnnecessaryCategories(doc, roomView);

                        // Make sure the view will export clipped geometry
                        roomView.CropBoxActive = true;
                        roomView.CropBoxVisible = false;
                    }

                    trans.Commit();
                }

                return roomView;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("View Creation Error", $"Error creating room view: {ex.Message}");
                return null;
            }
        }
        private void HideUnnecessaryCategories(Document doc, View3D view)
        {
            try
            {
                // Categories to hide for cleaner room visualization
                var categoriesToHide = new[]
                {
                    BuiltInCategory.OST_Grids,
                    BuiltInCategory.OST_Levels,
                    BuiltInCategory.OST_SectionBox,
                    BuiltInCategory.OST_CLines,
                    BuiltInCategory.OST_Constraints
                };

                foreach (var catId in categoriesToHide)
                {
                    try
                    {
                        Category cat = doc.Settings.Categories.get_Item(catId);
                        if (cat != null && cat.get_AllowsVisibilityControl(view))
                        {
                            view.SetCategoryHidden(cat.Id, true);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error hiding categories: {ex.Message}");
            }
        }

        private string GetDocumentCacheKey(Document doc)
        {
            try
            {
                string path = doc.PathName;
                if (string.IsNullOrEmpty(path))
                {
                    return $"unsaved_{doc.Title}_{doc.GetHashCode()}";
                }

                var fileInfo = new FileInfo(path);
                return $"{path}_{fileInfo.LastWriteTime.Ticks}";
            }
            catch
            {
                return $"doc_{doc.Title}_{doc.GetHashCode()}";
            }
        }

        private void CleanupOldExports(string directory, int keepCount)
        {
            try
            {
                var files = Directory.GetFiles(directory, "*.ifc")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                foreach (var file in files.Skip(keepCount))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private string SanitizeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";

            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            // Also remove spaces and special characters for cleaner filenames
            s = s.Replace(" ", "_");

            return s;
        }
    }

    /// <summary>
    /// Main command
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
                        _viewerWindow.Activate();
                        _viewerWindow.Focus();
                        return Result.Succeeded;
                    }
                    catch
                    {
                        _viewerWindow = null;
                    }
                }

                // Create new viewer window
                _viewerWindow = new ViewerWindow(doc);
                _viewerWindow.SetExternalEvent(_externalEvent, _eventHandler);
                _viewerWindow.Show();
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