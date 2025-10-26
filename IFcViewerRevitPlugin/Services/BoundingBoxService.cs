using IFcViewerRevitPlugin.Constants;
using IFcViewerRevitPlugin.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Implementation of bounding box calculation service
    /// </summary>
    public class BoundingBoxService : IBoundingBoxService
    {
        public BoundingBoxData CalculateBoundingBoxForStorey(IIfcBuildingStorey storey, IfcStore model)
        {
            if (storey == null || model == null)
            {
                return null;
            }

            try
            {
                BoundingBoxData totalBounds = null;

                foreach (var rel in storey.ContainsElements)
                {
                    foreach (var related in rel.RelatedElements)
                    {
                        if (related is IIfcProduct product)
                        {
                            var productBounds = GetBoundingBoxForProduct(product, model);
                            if (productBounds != null)
                            {
                                totalBounds = totalBounds == null
                                    ? productBounds
                                    : ExpandBounds(totalBounds, productBounds);
                            }
                        }
                    }
                }

                return totalBounds != null
                    ? AddPercentageBasedPadding(totalBounds)
                    : null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error calculating storey bounds: {ex.Message}", ex);
            }
        }

        public BoundingBoxData CalculateBoundingBoxForSpace(IIfcSpace space, IfcStore model)
        {
            if (space == null || model == null)
            {
                return null;
            }

            try
            {
                var bounds = GetBoundingBoxForProduct(space, model);
                return bounds != null
                    ? AddFixedPadding(bounds)
                    : null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error calculating space bounds: {ex.Message}", ex);
            }
        }

        public BoundingBoxData GetBoundingBoxForProduct(IIfcProduct product, IfcStore model)
        {
            if (product == null || model == null)
            {
                return null;
            }

            try
            {
                using (var geomReader = model.GeometryStore.BeginRead())
                {
                    var shapes = geomReader.ShapeInstancesOfEntity(product.EntityLabel);
                    BoundingBoxData totalBounds = null;

                    foreach (var shape in shapes)
                    {
                        var shapeBounds = new BoundingBoxData(shape.BoundingBox);
                        totalBounds = totalBounds == null
                            ? shapeBounds
                            : ExpandBounds(totalBounds, shapeBounds);
                    }

                    return totalBounds;
                }
            }
            catch
            {
                return null;
            }
        }

        public BoundingBoxData ExpandBounds(BoundingBoxData current, BoundingBoxData newBounds)
        {
            if (current == null) return newBounds;
            if (newBounds == null) return current;

            var minX = Math.Min(current.X, newBounds.X);
            var minY = Math.Min(current.Y, newBounds.Y);
            var minZ = Math.Min(current.Z, newBounds.Z);

            var maxX = Math.Max(current.X + current.SizeX, newBounds.X + newBounds.SizeX);
            var maxY = Math.Max(current.Y + current.SizeY, newBounds.Y + newBounds.SizeY);
            var maxZ = Math.Max(current.Z + current.SizeZ, newBounds.Z + newBounds.SizeZ);

            return new BoundingBoxData
            {
                X = minX,
                Y = minY,
                Z = minZ,
                SizeX = maxX - minX,
                SizeY = maxY - minY,
                SizeZ = maxZ - minZ
            };
        }

        public bool BoundsIntersect(BoundingBoxData b1, BoundingBoxData b2)
        {
            if (b1 == null || b2 == null)
            {
                return false;
            }

            return !(b1.X + b1.SizeX < b2.X || b2.X + b2.SizeX < b1.X ||
                     b1.Y + b1.SizeY < b2.Y || b2.Y + b2.SizeY < b1.Y ||
                     b1.Z + b1.SizeZ < b2.Z || b2.Z + b2.SizeZ < b1.Z);
        }

        private BoundingBoxData AddPercentageBasedPadding(BoundingBoxData bounds)
        {
            var paddingX = bounds.SizeX * GeometryConstants.XYPaddingPercentage;
            var paddingY = bounds.SizeY * GeometryConstants.XYPaddingPercentage;
            var paddingZ = bounds.SizeZ * GeometryConstants.ZPaddingPercentage;

            return bounds.AddPadding(paddingX, paddingY, paddingZ);
        }

        private BoundingBoxData AddFixedPadding(BoundingBoxData bounds)
        {
            return bounds.AddPadding(
                GeometryConstants.DefaultPaddingMeters,
                GeometryConstants.DefaultPaddingMeters,
                GeometryConstants.DefaultPaddingMeters * 0.6
            );
        }
    }
}
