namespace IFcViewerRevitPlugin.Constants
{
    /// <summary>
    /// Constants related to geometry generation and optimization
    /// </summary>
    public static class GeometryConstants
    {
        // Thread Configuration
        public const int MinimumThreads = 2;
        public const int MaximumThreadsReservedForSystem = 2;

        // Instance Count Thresholds for Thread Optimization
        public const int SmallModelInstanceThreshold = 500;
        public const int MediumModelInstanceThreshold = 2000;
        public const int LargeModelInstanceThreshold = 10000;

        // Thread Counts by Model Size
        public const int SmallModelThreads = 2;
        public const int MediumModelThreads = 3;
        public const int LargeModelThreads = 4;
        public const int VeryLargeModelThreads = 6;

        // Deflection (Detail Level) Settings
        public const double HighDetailDeflection = 0.01;
        public const double MediumHighDetailDeflection = 0.05;
        public const double MediumDetailDeflection = 0.1;
        public const double MediumLowDetailDeflection = 0.15;
        public const double LowDetailDeflection = 0.25;

        // Bounding Box Padding
        public const double DefaultPaddingMeters = 0.5;
        public const double XYPaddingPercentage = 0.05;
        public const double ZPaddingPercentage = 0.1;

        // Zoom Padding
        public const double ZoomPaddingXY = 1.0;
        public const double ZoomPaddingZ = 0.5;
    }
}
