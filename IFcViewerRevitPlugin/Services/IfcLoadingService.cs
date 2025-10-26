using IFcViewerRevitPlugin.DTOs;
using System;
using System.IO;
using System.Threading.Tasks;
using Xbim.Ifc;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Implementation of IFC loading service
    /// </summary>
    public class IfcLoadingService : IIfcLoadingService
    {
        private readonly IGeometryService _geometryService;
        private readonly IPerformanceOptimizationService _performanceService;

        public IfcLoadingService(
            IGeometryService geometryService,
            IPerformanceOptimizationService performanceService)
        {
            _geometryService = geometryService ?? throw new ArgumentNullException(nameof(geometryService));
            _performanceService = performanceService ?? throw new ArgumentNullException(nameof(performanceService));
        }

        public async Task<IfcStore> LoadIfcFileAsync(
            string filePath,
            ModelLoadOptions options = null,
            Action<int> progressCallback = null)
        {
            if (!IsValidIfcFile(filePath))
            {
                throw new ArgumentException("Invalid IFC file path", nameof(filePath));
            }

            options ??= CreateDefaultOptions();

            return await Task.Run(() => LoadIfcFile(filePath, options, progressCallback));
        }

        public bool IsValidIfcFile(string filePath)
        {
            return !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
        }

        private IfcStore LoadIfcFile(string filePath, ModelLoadOptions options, Action<int> progressCallback)
        {
            var model = IfcStore.Open(filePath);

            if (_geometryService.HasGeometry(model))
            {
                return model;
            }

            var geometryOptions = CreateGeometryOptions(model, options, progressCallback);
            _geometryService.GenerateGeometryAsync(model, geometryOptions).Wait();

            return model;
        }

        private GeometryGenerationOptions CreateGeometryOptions(
            IfcStore model,
            ModelLoadOptions options,
            Action<int> progressCallback)
        {
            int instanceCount = model.Instances.Count();

            return new GeometryGenerationOptions
            {
                MaxThreads = options.MaxThreads ?? _performanceService.DetermineOptimalThreadCount(instanceCount),
                Deflection = options.Deflection ?? _performanceService.DetermineDeflection(instanceCount),
                AdjustWorldCoordinateSystem = options.AdjustWorldCoordinateSystem,
                ProgressCallback = progressCallback != null
                    ? (progress, state) => progressCallback(progress)
                    : null
            };
        }

        private ModelLoadOptions CreateDefaultOptions()
        {
            return new ModelLoadOptions
            {
                OptimizeForPerformance = true,
                AdjustWorldCoordinateSystem = true
            };
        }
    }
}
