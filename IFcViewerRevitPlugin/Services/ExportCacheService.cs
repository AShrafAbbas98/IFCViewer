using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Service for managing export cache and cleanup
    /// </summary>
    public interface IExportCacheService
    {
        /// <summary>
        /// Gets a cached export path if it exists and is recent
        /// </summary>
        string GetCachedExport(string cacheKey, TimeSpan maxAge);

        /// <summary>
        /// Caches an export path
        /// </summary>
        void CacheExport(string cacheKey, string filePath);

        /// <summary>
        /// Cleans up old export files
        /// </summary>
        void CleanupOldExports(string directory, int keepCount);

        /// <summary>
        /// Clears all cached exports
        /// </summary>
        void ClearCache();
    }

    public class ExportCacheService : IExportCacheService
    {
        private readonly Dictionary<string, string> _exportCache = new Dictionary<string, string>();
        private readonly object _cacheLock = new object();

        public string GetCachedExport(string cacheKey, TimeSpan maxAge)
        {
            lock (_cacheLock)
            {
                if (!_exportCache.ContainsKey(cacheKey))
                {
                    return null;
                }

                string cachedPath = _exportCache[cacheKey];

                if (!File.Exists(cachedPath))
                {
                    _exportCache.Remove(cacheKey);
                    return null;
                }

                var fileInfo = new FileInfo(cachedPath);
                if (DateTime.Now - fileInfo.LastWriteTime > maxAge)
                {
                    _exportCache.Remove(cacheKey);
                    return null;
                }

                return cachedPath;
            }
        }

        public void CacheExport(string cacheKey, string filePath)
        {
            lock (_cacheLock)
            {
                _exportCache[cacheKey] = filePath;
            }
        }

        public void CleanupOldExports(string directory, int keepCount)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return;
                }

                var files = Directory.GetFiles(directory, "*.ifc")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                foreach (var file in files.Skip(keepCount))
                {
                    TryDeleteFile(file);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _exportCache.Clear();
            }
        }

        private void TryDeleteFile(FileInfo file)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete {file.Name}: {ex.Message}");
            }
        }
    }
}
