using Xbim.Common.Geometry;

namespace IFcViewerRevitPlugin.DTOs
{
    /// <summary>
    /// Data transfer object for bounding box information
    /// </summary>
    public class BoundingBoxData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double SizeX { get; set; }
        public double SizeY { get; set; }
        public double SizeZ { get; set; }

        public BoundingBoxData() { }

        public BoundingBoxData(XbimRect3D rect)
        {
            X = rect.X;
            Y = rect.Y;
            Z = rect.Z;
            SizeX = rect.SizeX;
            SizeY = rect.SizeY;
            SizeZ = rect.SizeZ;
        }

        public XbimRect3D ToXbimRect3D()
        {
            return new XbimRect3D(X, Y, Z, SizeX, SizeY, SizeZ);
        }

        public BoundingBoxData AddPadding(double paddingX, double paddingY, double paddingZ)
        {
            return new BoundingBoxData
            {
                X = X - paddingX,
                Y = Y - paddingY,
                Z = Z - paddingZ,
                SizeX = SizeX + paddingX * 2,
                SizeY = SizeY + paddingY * 2,
                SizeZ = SizeZ + paddingZ * 2
            };
        }
    }
}
