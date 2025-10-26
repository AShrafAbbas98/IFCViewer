using IFcViewerRevitPlugin.DTOs;
using System.Collections.Generic;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Service for filtering IFC elements
    /// </summary>
    public interface IFilteringService
    {
        /// <summary>
        /// Gets entity labels for all elements in a storey
        /// </summary>
        HashSet<int> GetEntityLabelsForStorey(IIfcBuildingStorey storey, IfcStore model);

        /// <summary>
        /// Gets entity labels for all elements in a space
        /// </summary>
        HashSet<int> GetEntityLabelsForSpace(IIfcSpace space, IfcStore model);

        /// <summary>
        /// Gets entity labels for elements intersecting a bounding box
        /// </summary>
        HashSet<int> GetEntityLabelsIntersectingBbox(BoundingBoxData bbox, IfcStore model);

        /// <summary>
        /// Gets all building storeys from a model
        /// </summary>
        List<IIfcBuildingStorey> GetBuildingStoreys(IfcStore model);

        /// <summary>
        /// Gets all spaces in a storey
        /// </summary>
        List<IIfcSpace> GetSpacesInStorey(IIfcBuildingStorey storey, IfcStore model);
    }
}
