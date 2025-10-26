using IFcViewerRevitPlugin.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Implementation of IFC filtering service
    /// </summary>
    public class FilteringService : IFilteringService
    {
        private readonly IBoundingBoxService _boundingBoxService;

        public FilteringService(IBoundingBoxService boundingBoxService)
        {
            _boundingBoxService = boundingBoxService ?? throw new ArgumentNullException(nameof(boundingBoxService));
        }

        public HashSet<int> GetEntityLabelsForStorey(IIfcBuildingStorey storey, IfcStore model)
        {
            var labels = new HashSet<int>();

            if (storey == null || model == null)
            {
                return labels;
            }

            try
            {
                labels.Add(storey.EntityLabel);

                foreach (var rel in storey.ContainsElements)
                {
                    foreach (var related in rel.RelatedElements)
                    {
                        if (related is IIfcProduct product)
                        {
                            labels.Add(product.EntityLabel);
                        }
                    }
                }
            }
            catch
            {
                // Return what we have so far
            }

            return labels;
        }

        public HashSet<int> GetEntityLabelsForSpace(IIfcSpace space, IfcStore model)
        {
            var labels = new HashSet<int>();

            if (space == null || model == null)
            {
                return labels;
            }

            try
            {
                labels.Add(space.EntityLabel);

                foreach (var rel in space.ContainsElements)
                {
                    foreach (var related in rel.RelatedElements)
                    {
                        if (related is IIfcProduct product)
                        {
                            labels.Add(product.EntityLabel);
                        }
                    }
                }
            }
            catch
            {
                // Return what we have so far
            }

            return labels;
        }

        public HashSet<int> GetEntityLabelsIntersectingBbox(BoundingBoxData bbox, IfcStore model)
        {
            var labels = new HashSet<int>();

            if (bbox == null || model == null)
            {
                return labels;
            }

            try
            {
                var products = model.Instances.OfType<IIfcProduct>();

                foreach (var product in products)
                {
                    var productBounds = _boundingBoxService.GetBoundingBoxForProduct(product, model);

                    if (productBounds != null && _boundingBoxService.BoundsIntersect(bbox, productBounds))
                    {
                        labels.Add(product.EntityLabel);
                    }
                }
            }
            catch
            {
                // Return what we have so far
            }

            return labels;
        }

        public List<IIfcBuildingStorey> GetBuildingStoreys(IfcStore model)
        {
            if (model == null)
            {
                return new List<IIfcBuildingStorey>();
            }

            return model.Instances
                .OfType<IIfcBuildingStorey>()
                .OrderBy(s => GetElevation(s))
                .ToList();
        }

        public List<IIfcSpace> GetSpacesInStorey(IIfcBuildingStorey storey, IfcStore model)
        {
            if (storey == null || model == null)
            {
                return new List<IIfcSpace>();
            }

            var spaces = model.Instances.OfType<IIfcSpace>()
                .Where(s => IsSpaceInStorey(s, storey))
                .ToList();

            if (spaces.Any())
            {
                return spaces;
            }

            // Fallback: use spatial intersection
            return GetSpacesInStoreyByBounds(storey, model);
        }

        private bool IsSpaceInStorey(IIfcSpace space, IIfcBuildingStorey storey)
        {
            if (space.Decomposes.Any(rel => rel.RelatingObject == storey))
            {
                return true;
            }

            foreach (var relContained in storey.ContainsElements)
            {
                if (relContained.RelatedElements.Contains(space))
                {
                    return true;
                }
            }

            return false;
        }

        private List<IIfcSpace> GetSpacesInStoreyByBounds(IIfcBuildingStorey storey, IfcStore model)
        {
            var storeyBounds = _boundingBoxService.GetBoundingBoxForProduct(storey, model);

            if (storeyBounds == null)
            {
                return new List<IIfcSpace>();
            }

            return model.Instances.OfType<IIfcSpace>()
                .Where(s =>
                {
                    var spaceBounds = _boundingBoxService.GetBoundingBoxForProduct(s, model);
                    return spaceBounds != null && _boundingBoxService.BoundsIntersect(storeyBounds, spaceBounds);
                })
                .ToList();
        }

        private double GetElevation(IIfcBuildingStorey storey)
        {
            try
            {
                return storey.Elevation?.Value ?? double.NegativeInfinity;
            }
            catch
            {
                return double.NegativeInfinity;
            }
        }
    }
}
