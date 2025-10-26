using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using IFcViewerRevitPlugin.Infrastructure;
using IFcViewerRevitPlugin.Services;
using IFcViewerRevitPlugin.Views;
using System;
using System.IO;
using System.Reflection;

namespace IFcViewerRevitPlugin
{
    /// <summary>
    /// Refactored external event handler using service layer
    /// Follows Single Responsibility and Clean Code principles
    /// </summary>
    public class ViewerExternalEventHandlerRefactored : IExternalEventHandler
    {
        private readonly IRevitDocumentService _documentService;
        private readonly IRevitViewService _viewService;
        private readonly IExportCacheService _cacheService;

        private Document _document;
        private Action<string> _onIfcExported;
        private string _roomName;
        private bool _isRoomScopedExport;

        public ViewerExternalEventHandlerRefactored()
        {
            var container = ServiceContainer.Instance;
            _documentService = container.Resolve<IRevitDocumentService>();
            _viewService = container.Resolve<IRevitViewService>();
            _cacheService = container.Resolve<IExportCacheService>();
        }

        #region Public Methods

        public void SetContext(Document document, Action<string> onIfcExported)
        {
            _document = document;
            _onIfcExported = onIfcExported;
            _isRoomScopedExport = false;
            _roomName = null;
        }

        public void SetRoomContext(Document document, string roomName, Action<string> onIfcExported)
        {
            _document = document;
            _onIfcExported = onIfcExported;
            _isRoomScopedExport = true;
            _roomName = roomName;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                if (!ValidateDocument())
                {
                    return;
                }

                string ifcPath = _isRoomScopedExport
                    ? ExportRoomScoped()
                    : ExportFullModel();

                if (IsValidExportPath(ifcPath))
                {
                    _onIfcExported?.Invoke(ifcPath);
                }
            }
            catch (Exception ex)
            {
                ShowError("Export Error", $"Error: {ex.Message}");
            }
        }

        public string GetName()
        {
            return "IFC Viewer External Event";
        }

        #endregion

        #region Private Methods - Validation

        private bool ValidateDocument()
        {
            if (_document == null)
            {
                ShowError("Error", "No document specified");
                return false;
            }

            if (!_documentService.CanExportDocument(_document, out string message))
            {
                ShowError("Error", message);
                return false;
            }

            return true;
        }

        private bool IsValidExportPath(string path)
        {
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        #endregion

        #region Private Methods - Full Model Export

        private string ExportFullModel()
        {
            string cacheKey = _documentService.GetDocumentCacheKey(_document);
            string cachedPath = _cacheService.GetCachedExport(cacheKey, TimeSpan.FromMinutes(5));

            if (cachedPath != null)
            {
                return cachedPath;
            }

            string tempPath = GetExportDirectory();
            string fileName = GenerateFileName(false);
            string fullPath = Path.Combine(tempPath, fileName);

            try
            {
                ExportDocumentToIfc(tempPath, fileName, null);

                _cacheService.CacheExport(cacheKey, fullPath);
                _cacheService.CleanupOldExports(tempPath, 3);

                return fullPath;
            }
            catch (Exception ex)
            {
                ShowError("Export Error", $"Error exporting full model: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Private Methods - Room-Scoped Export

        private string ExportRoomScoped()
        {
            string cacheKey = $"{_documentService.GetDocumentCacheKey(_document)}_{_roomName}";
            string cachedPath = _cacheService.GetCachedExport(cacheKey, TimeSpan.FromMinutes(10));

            if (cachedPath != null)
            {
                return cachedPath;
            }

            try
            {
                var room = _viewService.FindRoomByName(_document, _roomName);
                if (room == null)
                {
                    ShowError("Error", $"Room '{_roomName}' not found");
                    return null;
                }

                View3D roomView = _viewService.CreateRoomScopedView(_document, room);
                if (roomView == null)
                {
                    ShowError("Error", "Could not create room view");
                    return null;
                }

                string tempPath = GetRoomExportDirectory();
                string fileName = GenerateFileName(true);
                string fullPath = Path.Combine(tempPath, fileName);

                ExportDocumentToIfc(tempPath, fileName, roomView.Id);

                _viewService.DeleteView(_document, roomView);

                _cacheService.CacheExport(cacheKey, fullPath);
                _cacheService.CleanupOldExports(tempPath, 10);

                return fullPath;
            }
            catch (Exception ex)
            {
                ShowError("Export Error", $"Error exporting room '{_roomName}': {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Private Methods - Export Helpers

        private void ExportDocumentToIfc(string directory, string fileName, ElementId? filterViewId)
        {
            EnsureDirectoryExists(directory);

            var ifcOptions = new IFCExportOptions
            {
                FileVersion = IFCVersion.IFC2x3CV2,
                WallAndColumnSplitting = filterViewId.HasValue,
                ExportBaseQuantities = false,
                SpaceBoundaryLevel = filterViewId.HasValue ? 1 : 2
            };

            if (filterViewId.HasValue)
            {
                ifcOptions.FilterViewId = filterViewId.Value;
            }

            using (var trans = new Transaction(_document, "Export IFC"))
            {
                trans.Start();
                _document.Export(directory, fileName, ifcOptions);
                trans.Commit();
            }
        }

        private string GetExportDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "RevitIFCExport");
        }

        private string GetRoomExportDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "RevitIFCExport", "Rooms");
        }

        private string GenerateFileName(bool isRoomExport)
        {
            string title = _documentService.SanitizeFileName(_document.Title);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            if (isRoomExport)
            {
                string roomName = _documentService.SanitizeFileName(_roomName);
                return $"{title}_Room_{roomName}_{timestamp}.ifc";
            }

            return $"{title}_{timestamp}.ifc";
        }

        private void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private void ShowError(string title, string message)
        {
            TaskDialog.Show(title, message);
        }

        #endregion
    }

    /// <summary>
    /// Refactored main command - Clean and focused
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CommandsRefactored : IExternalCommand
    {
        private static ExternalEvent _externalEvent;
        private static ViewerExternalEventHandlerRefactored _eventHandler;
        private static ViewerWindow _viewerWindow;

        public CommandsRefactored()
        {
            InitializeAssemblyResolution();
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var validationResult = ValidateExecution(commandData, out message);
                if (validationResult != Result.Succeeded)
                {
                    return validationResult;
                }

                InitializeExternalEvent();

                if (TryActivateExistingWindow())
                {
                    return Result.Succeeded;
                }

                OpenNewViewerWindow(commandData.Application.ActiveUIDocument.Document);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error opening IFC Viewer: {ex.Message}";
                TaskDialog.Show("Error", $"{message}\n\nDetails:\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        #region Private Methods - Validation

        private Result ValidateExecution(ExternalCommandData commandData, out string message)
        {
            message = string.Empty;

            UIDocument uiDoc = commandData.Application.ActiveUIDocument;

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

            return Result.Succeeded;
        }

        #endregion

        #region Private Methods - Initialization

        private void InitializeExternalEvent()
        {
            if (_eventHandler == null)
            {
                _eventHandler = new ViewerExternalEventHandlerRefactored();
                _externalEvent = ExternalEvent.Create(_eventHandler);
            }
        }

        private void InitializeAssemblyResolution()
        {
            LoadLibrary("RestSharp.dll");
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolve;
        }

        #endregion

        #region Private Methods - Window Management

        private bool TryActivateExistingWindow()
        {
            if (_viewerWindow != null)
            {
                try
                {
                    _viewerWindow.Activate();
                    _viewerWindow.Focus();
                    return true;
                }
                catch
                {
                    _viewerWindow = null;
                    return false;
                }
            }

            return false;
        }

        private void OpenNewViewerWindow(Document document)
        {
            _viewerWindow = new ViewerWindow(document);
            _viewerWindow.SetExternalEvent(_externalEvent, _eventHandler);
            _viewerWindow.Show();
            _viewerWindow.Closed += OnWindowClosed;
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            _viewerWindow = null;
        }

        #endregion

        #region Assembly Resolution

        private Assembly AssemblyResolve(object sender, ResolveEventArgs args)
        {
            string assemblyName = new AssemblyName(args.Name).Name;

            if (string.IsNullOrEmpty(assemblyName))
            {
                return null;
            }

            return TryLoadAssembly(assemblyName);
        }

        private Assembly TryLoadAssembly(string assemblyName)
        {
            try
            {
                string pluginDir = GetPluginDirectory();
                string[] variations = GetAssemblyVariations(assemblyName);

                foreach (string variation in variations)
                {
                    string assemblyPath = Path.Combine(pluginDir, variation);
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

        private string[] GetAssemblyVariations(string assemblyName)
        {
            return new[]
            {
                assemblyName + ".dll",
                assemblyName.Replace(".dll", "") + ".dll"
            };
        }

        private string GetPluginDirectory()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
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
