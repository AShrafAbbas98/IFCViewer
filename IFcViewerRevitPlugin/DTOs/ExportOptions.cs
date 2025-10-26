using Autodesk.Revit.DB;

namespace IFcViewerRevitPlugin.DTOs
{
    /// <summary>
    /// Custom export options wrapper
    /// </summary>
    public class ExportOptions
    {
        public IFCVersion FileVersion { get; set; }
        public bool WallAndColumnSplitting { get; set; }
        public bool ExportBaseQuantities { get; set; }
        public int SpaceBoundaryLevel { get; set; }
        public bool IsLightweightExport { get; set; }

        public static ExportOptions CreateDefault()
        {
            return new ExportOptions
            {
                FileVersion = Constants.ExportConstants.DefaultIfcVersion,
                WallAndColumnSplitting = Constants.ExportConstants.DefaultWallAndColumnSplitting,
                ExportBaseQuantities = Constants.ExportConstants.DefaultExportBaseQuantities,
                SpaceBoundaryLevel = Constants.ExportConstants.DefaultSpaceBoundaryLevel,
                IsLightweightExport = false
            };
        }

        public static ExportOptions CreateLightweight()
        {
            return new ExportOptions
            {
                FileVersion = Constants.ExportConstants.DefaultIfcVersion,
                WallAndColumnSplitting = false,
                ExportBaseQuantities = false,
                SpaceBoundaryLevel = Constants.ExportConstants.MinimalSpaceBoundaryLevel,
                IsLightweightExport = true
            };
        }
    }
}
