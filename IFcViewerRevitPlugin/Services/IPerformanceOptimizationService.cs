namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Service for optimizing performance based on model characteristics
    /// </summary>
    public interface IPerformanceOptimizationService
    {
        /// <summary>
        /// Determines the optimal number of threads for geometry generation
        /// </summary>
        int DetermineOptimalThreadCount(int instanceCount);

        /// <summary>
        /// Determines the appropriate deflection (detail level) for geometry
        /// </summary>
        double DetermineDeflection(int instanceCount);

        /// <summary>
        /// Gets a human-readable description of the detail level
        /// </summary>
        string GetDetailDescription(double deflection);
    }
}
