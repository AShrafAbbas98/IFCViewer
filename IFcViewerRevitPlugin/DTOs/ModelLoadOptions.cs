namespace IFcViewerRevitPlugin.DTOs
{
    /// <summary>
    /// Options for loading and processing IFC models
    /// </summary>
    public class ModelLoadOptions
    {
        public string FilePath { get; set; }
        public bool OptimizeForPerformance { get; set; } = true;
        public int? MaxThreads { get; set; }
        public double? Deflection { get; set; }
        public bool AdjustWorldCoordinateSystem { get; set; } = true;
    }
}
