namespace IFcViewerRevitPlugin.DTOs
{
    /// <summary>
    /// Options for generating 3D geometry from IFC models
    /// </summary>
    public class GeometryGenerationOptions
    {
        public int MaxThreads { get; set; }
        public double Deflection { get; set; }
        public bool AdjustWorldCoordinateSystem { get; set; }
        public System.Action<int, object> ProgressCallback { get; set; }
    }
}
