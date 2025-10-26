using IFcViewerRevitPlugin.DTOs;
using System.Collections.Generic;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Service for calculating bounding boxes of IFC elements
    /// </summary>
    public interface IBoundingBoxService
    {
        /// <summary>
        /// Calculates bounding box for a building storey
        /// </summary>
        BoundingBoxData CalculateBoundingBoxForStorey(IIfcBuildingStorey storey, IfcStore model);

        /// <summary>
        /// Calculates bounding box for a space
        /// </summary>
        BoundingBoxData CalculateBoundingBoxForSpace(IIfcSpace space, IfcStore model);

        /// <summary>
        /// Calculates bounding box for any product
        /// </summary>
        BoundingBoxData GetBoundingBoxForProduct(IIfcProduct product, IfcStore model);

        /// <summary>
        /// Expands a bounding box to include another bounding box
        /// </summary>
        BoundingBoxData ExpandBounds(BoundingBoxData current, BoundingBoxData newBounds);

        /// <summary>
        /// Checks if two bounding boxes intersect
        /// </summary>
        bool BoundsIntersect(BoundingBoxData b1, BoundingBoxData b2);
    }
}
