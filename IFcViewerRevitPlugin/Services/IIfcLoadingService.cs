using IFcViewerRevitPlugin.DTOs;
using System;
using System.Threading.Tasks;
using Xbim.Ifc;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Service for loading IFC files
    /// </summary>
    public interface IIfcLoadingService
    {
        /// <summary>
        /// Loads an IFC file with the specified options
        /// </summary>
        Task<IfcStore> LoadIfcFileAsync(string filePath, ModelLoadOptions options = null, Action<int> progressCallback = null);

        /// <summary>
        /// Checks if a file path is valid
        /// </summary>
        bool IsValidIfcFile(string filePath);
    }
}
