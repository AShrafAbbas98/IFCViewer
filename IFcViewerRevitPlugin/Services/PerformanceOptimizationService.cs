using IFcViewerRevitPlugin.Constants;
using System;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Implementation of performance optimization logic
    /// </summary>
    public class PerformanceOptimizationService : IPerformanceOptimizationService
    {
        public int DetermineOptimalThreadCount(int instanceCount)
        {
            int availableCores = Math.Max(1, Environment.ProcessorCount - GeometryConstants.MaximumThreadsReservedForSystem);

            if (instanceCount < GeometryConstants.SmallModelInstanceThreshold)
            {
                return Math.Min(GeometryConstants.SmallModelThreads, availableCores);
            }

            if (instanceCount < GeometryConstants.MediumModelInstanceThreshold)
            {
                return Math.Min(GeometryConstants.MediumModelThreads, availableCores);
            }

            if (instanceCount < GeometryConstants.LargeModelInstanceThreshold)
            {
                return Math.Min(GeometryConstants.LargeModelThreads, availableCores);
            }

            return Math.Min(GeometryConstants.VeryLargeModelThreads, availableCores);
        }

        public double DetermineDeflection(int instanceCount)
        {
            if (instanceCount < 1000) return GeometryConstants.HighDetailDeflection;
            if (instanceCount < 5000) return GeometryConstants.MediumHighDetailDeflection;
            if (instanceCount < 10000) return GeometryConstants.MediumDetailDeflection;
            if (instanceCount < 20000) return GeometryConstants.MediumLowDetailDeflection;

            return GeometryConstants.LowDetailDeflection;
        }

        public string GetDetailDescription(double deflection)
        {
            if (deflection <= GeometryConstants.HighDetailDeflection) return "High";
            if (deflection <= GeometryConstants.MediumHighDetailDeflection) return "Medium-High";
            if (deflection <= GeometryConstants.MediumDetailDeflection) return "Medium";
            if (deflection <= GeometryConstants.MediumLowDetailDeflection) return "Medium-Low";

            return "Low";
        }
    }
}
