namespace IFcViewerRevitPlugin.Constants
{
    /// <summary>
    /// Constants related to UI elements and messages
    /// </summary>
    public static class UiConstants
    {
        // Status Messages
        public const string StatusReady = "Ready";
        public const string StatusExporting = "Exporting to IFC...";
        public const string StatusLoadingModel = "Loading IFC model...";
        public const string StatusCreatingGeometry = "Creating 3D geometry (this may take a moment)...";
        public const string StatusGeometryProgress = "Creating geometry: {0}%";
        public const string StatusExportFailed = "Export failed";
        public const string StatusModelLoadFailed = "Failed to load model";
        public const string StatusModelLoaded = "Model loaded - {0} instances";
        public const string StatusFilteringByLevel = "Filtering by level: {0}";
        public const string StatusLevelNotFound = "Level not found: {0}";
        public const string StatusRoomNotFound = "Room not found: {0}";
        public const string StatusNoLevelsFound = "No levels found in model";
        public const string StatusErrorLoadingModel = "Error loading model";

        // Combo Box Default Items
        public const string AllLevelsItem = "-- All Levels --";
        public const string AllRoomsItem = "-- All Rooms --";

        // Default Names
        public const string DefaultLevelNameFormat = "Level_{0}";
        public const string DefaultSpaceNameFormat = "Space_{0}";

        // Dialog Titles
        public const string ErrorDialogTitle = "Error";
        public const string WarningDialogTitle = "Warning";
        public const string GeometryWarningDialogTitle = "Geometry Warning";
        public const string IFCLoadErrorDialogTitle = "IFC Load Error";
        public const string ExportErrorDialogTitle = "Export Error";
    }
}
