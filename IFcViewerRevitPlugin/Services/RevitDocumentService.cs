using Autodesk.Revit.DB;
using IFcViewerRevitPlugin.Constants;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Service for managing Revit document operations
    /// </summary>
    public interface IRevitDocumentService
    {
        /// <summary>
        /// Validates if a document can be exported
        /// </summary>
        bool CanExportDocument(Document document, out string message);

        /// <summary>
        /// Creates a cache key for a document
        /// </summary>
        string GetDocumentCacheKey(Document document);

        /// <summary>
        /// Sanitizes a file name for safe file system usage
        /// </summary>
        string SanitizeFileName(string fileName);
    }

    public class RevitDocumentService : IRevitDocumentService
    {
        public bool CanExportDocument(Document document, out string message)
        {
            message = string.Empty;

            if (document == null)
            {
                message = "No active document found.";
                return false;
            }

            if (document.IsFamilyDocument)
            {
                message = "Cannot export family documents.";
                return false;
            }

            return true;
        }

        public string GetDocumentCacheKey(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            try
            {
                string path = document.PathName;

                if (string.IsNullOrEmpty(path))
                {
                    return $"unsaved_{document.Title}_{document.GetHashCode()}";
                }

                var fileInfo = new FileInfo(path);
                return $"{path}_{fileInfo.LastWriteTime.Ticks}";
            }
            catch
            {
                return $"doc_{document.Title}_{document.GetHashCode()}";
            }
        }

        public string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return "unnamed";
            }

            string sanitized = fileName;

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(c, '_');
            }

            sanitized = sanitized.Replace(" ", "_");

            return sanitized;
        }
    }
}
