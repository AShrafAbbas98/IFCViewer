using Autodesk.Revit.DB;

namespace IFcViewerRevitPlugin.Constants
{
    /// <summary>
    /// Constants related to IFC export operations
    /// </summary>
    public static class ExportConstants
    {
        public const string TempFolderName = "RevitIFCExport";
        public const string FileExtension = ".ifc";
        public const string FileNameFormat = "{0}_{1:yyyyMMdd_HHmmss}.ifc";
        public const string LightFileNameFormat = "{0}_light_{1:yyyyMMdd_HHmmss}.ifc";
        public const string TransactionName = "Export IFC";

        // Default Export Options
        public static readonly IFCVersion DefaultIfcVersion = IFCVersion.IFC2x3CV2;
        public const bool DefaultWallAndColumnSplitting = false;
        public const bool DefaultExportBaseQuantities = false;
        public const int DefaultSpaceBoundaryLevel = 1;
        public const int MinimalSpaceBoundaryLevel = 0;
    }
}
