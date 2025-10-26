using IFcViewerRevitPlugin.DTOs;
using System;
using System.Threading.Tasks;
using Xbim.Ifc;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Service for generating 3D geometry from IFC models
    /// </summary>
    public interface IGeometryService
    {
        /// <summary>
        /// Generates 3D geometry for an IFC model
        /// </summary>
        Task GenerateGeometryAsync(IfcStore model, GeometryGenerationOptions options);

        /// <summary>
        /// Checks if a model has geometry
        /// </summary>
        bool HasGeometry(IfcStore model);
    }
}
