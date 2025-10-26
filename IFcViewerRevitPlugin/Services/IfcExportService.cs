using Autodesk.Revit.DB;
using IFcViewerRevitPlugin.Constants;
using IFcViewerRevitPlugin.DTOs;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Implementation of IFC export service
    /// </summary>
    public class IfcExportService : IIfcExportService
    {
        public async Task<string> ExportToIfcAsync(Document document, ExportOptions options = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document), "No active document found.");
            }

            options ??= ExportOptions.CreateDefault();

            string tempPath = GetTempExportPath();
            EnsureDirectoryExists(tempPath);

            string fileName = GenerateExportFileName(document, options.IsLightweightExport);
            string fullPath = Path.Combine(tempPath, fileName);

            return await Task.Run(() => ExportDocument(document, tempPath, fileName, options));
        }

        public string GenerateExportFileName(Document document, bool isLightweight = false)
        {
            string sanitizedTitle = SanitizeFileName(document.Title);
            string format = isLightweight
                ? ExportConstants.LightFileNameFormat
                : ExportConstants.FileNameFormat;

            return string.Format(format, sanitizedTitle, DateTime.Now);
        }

        private string ExportDocument(Document document, string tempPath, string fileName, ExportOptions options)
        {
            try
            {
                var ifcOptions = CreateIfcExportOptions(options);

                using (var trans = new Transaction(document, ExportConstants.TransactionName))
                {
                    trans.Start();
                    document.Export(tempPath, fileName, ifcOptions);
                    trans.Commit();
                }

                return Path.Combine(tempPath, fileName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error exporting to IFC: {ex.Message}", ex);
            }
        }

        private IFCExportOptions CreateIfcExportOptions(ExportOptions options)
        {
            return new IFCExportOptions
            {
                FileVersion = options.FileVersion,
                WallAndColumnSplitting = options.WallAndColumnSplitting,
                ExportBaseQuantities = options.ExportBaseQuantities,
                SpaceBoundaryLevel = options.SpaceBoundaryLevel
            };
        }

        private string GetTempExportPath()
        {
            return Path.Combine(Path.GetTempPath(), ExportConstants.TempFolderName);
        }

        private void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private string SanitizeFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars()
                .Aggregate(fileName, (current, c) => current.Replace(c, '_'));
        }
    }
}
