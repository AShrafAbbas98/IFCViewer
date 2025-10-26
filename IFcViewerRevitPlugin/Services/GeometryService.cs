using IFcViewerRevitPlugin.DTOs;
using System;
using System.Threading.Tasks;
using Xbim.Ifc;
using Xbim.ModelGeometry.Scene;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Implementation of geometry generation service
    /// </summary>
    public class GeometryService : IGeometryService
    {
        public async Task GenerateGeometryAsync(IfcStore model, GeometryGenerationOptions options)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!HasGeometry(model))
            {
                await Task.Run(() => GenerateGeometry(model, options));
            }
        }

        public bool HasGeometry(IfcStore model)
        {
            return model?.GeometryStore != null && !model.GeometryStore.IsEmpty;
        }

        private void GenerateGeometry(IfcStore model, GeometryGenerationOptions options)
        {
            try
            {
                var context = new Xbim3DModelContext(model)
                {
                    MaxThreads = options.MaxThreads
                };

                context.CreateContext(
                    adjustWcs: options.AdjustWorldCoordinateSystem
                );
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error creating geometry: {ex.Message}", ex);
            }
        }
    }
}
