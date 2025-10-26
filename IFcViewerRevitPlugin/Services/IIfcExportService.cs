using Autodesk.Revit.DB;
using IFcViewerRevitPlugin.DTOs;
using System.Threading.Tasks;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Service for exporting Revit documents to IFC format
    /// </summary>
    public interface IIfcExportService
    {
        /// <summary>
        /// Exports a Revit document to IFC file
        /// </summary>
        Task<string> ExportToIfcAsync(Document document, ExportOptions options = null);

        /// <summary>
        /// Generates a safe file name for the export
        /// </summary>
        string GenerateExportFileName(Document document, bool isLightweight = false);
    }
}
