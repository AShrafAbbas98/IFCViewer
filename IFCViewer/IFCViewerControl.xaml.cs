using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;
using Xbim.Presentation;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.IO;

namespace IFCViewer
{
    public partial class IFCViewerControl : UserControl
    {
        private IfcStore _model;
        private Xbim3DModelContext _context;

        public IFCViewerControl()
        {
            InitializeComponent();
            Loaded += IFCViewerControl_Loaded;
        }

        private void IFCViewerControl_Loaded(object sender, RoutedEventArgs e)
        {
            ModelProvider.Refresh();
        }

        private ObjectDataProvider ModelProvider
        {
            get { return MainFrame.DataContext as ObjectDataProvider; }
        }

        public void LoadIfcFile(string filePath)
        {
            try
            {
                _model = IfcStore.Open(filePath);
                _context = new Xbim3DModelContext(_model);
                _context.CreateContext();
                ModelProvider.ObjectInstance = _model;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading IFC file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void FilterByLevel(string levelName)
        {
            if (_model == null) return;

            try
            {
                var level = _model.Instances
                    .OfType<IIfcBuildingStorey>()
                    .FirstOrDefault(l => l.Name == levelName);

                if (level != null)
                {
                    var elementsInLevel = GetElementsInLevel(level);
                    ApplyScopeBox(elementsInLevel);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error filtering by level: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void FilterByRoom(string roomName)
        {
            if (_model == null) return;

            try
            {
                var room = _model.Instances
                    .OfType<IIfcSpace>()
                    .FirstOrDefault(r => r.Name == roomName || r.LongName == roomName);

                if (room != null)
                {
                    var roomElements = GetRoomBoundaryElements(room);
                    ApplyScopeBox(roomElements);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error filtering by room: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ResetView()
        {
            if (_model != null)
            {
                ModelProvider.ObjectInstance = null;
                _context = new Xbim3DModelContext(_model);
                _context.CreateContext();
                ModelProvider.ObjectInstance = _model;

                // Zoom to extents using the Viewport
                if (DrawingControl.Viewport != null)
                {
                    DrawingControl.Viewport.ZoomExtents();
                }
            }
        }

        private List<IPersistEntity> GetElementsInLevel(IIfcBuildingStorey level)
        {
            var elements = new List<IPersistEntity>();

            // Get all elements contained in the level using proper relationship navigation
            foreach (var rel in level.ContainsElements)
            {
                foreach (var element in rel.RelatedElements)
                {
                    if (element is IIfcProduct product)
                    {
                        elements.Add(product);
                    }
                }
            }

            // Get the slab (floor) for this level
            var slabs = _model.Instances
                .OfType<IIfcSlab>()
                .Where(s => IsElementInLevel(s, level));
            elements.AddRange(slabs.Cast<IPersistEntity>());

            // Get the ceiling for this level
            var ceilings = _model.Instances
                .OfType<IIfcCovering>()
                .Where(c => c.PredefinedType == Xbim.Ifc4.Interfaces.IfcCoveringTypeEnum.CEILING
                            && IsElementInLevel(c, level));
            elements.AddRange(ceilings.Cast<IPersistEntity>());

            return elements;
        }

        private List<IPersistEntity> GetRoomBoundaryElements(IIfcSpace room)
        {
            var elements = new List<IPersistEntity>();

            // Get boundary elements (walls, etc.)
            if (room.BoundedBy != null)
            {
                foreach (var boundary in room.BoundedBy)
                {
                    var relatedElement = boundary.RelatedBuildingElement;
                    if (relatedElement != null && !elements.Contains(relatedElement))
                    {
                        elements.Add(relatedElement);
                    }
                }
            }

            // Get the floor and ceiling related to this room
            var roomLevel = GetRoomLevel(room);
            if (roomLevel != null)
            {
                // Get floor slab
                var floorSlabs = _model.Instances
                    .OfType<IIfcSlab>()
                    .Where(s => IsElementInLevel(s, roomLevel) &&
                                s.PredefinedType == Xbim.Ifc4.Interfaces.IfcSlabTypeEnum.FLOOR);
                elements.AddRange(floorSlabs.Cast<IPersistEntity>());

                // Get ceiling
                var ceilings = _model.Instances
                    .OfType<IIfcCovering>()
                    .Where(c => c.PredefinedType == Xbim.Ifc4.Interfaces.IfcCoveringTypeEnum.CEILING
                                && IsElementInLevel(c, roomLevel));
                elements.AddRange(ceilings.Cast<IPersistEntity>());
            }

            return elements;
        }

        private IIfcBuildingStorey GetRoomLevel(IIfcSpace room)
        {
            // Navigate through spatial structure containment
            foreach (var rel in room.Decomposes)
            {
                if (rel.RelatingObject is IIfcBuildingStorey storey)
                {
                    return storey;
                }
            }
            return null;
        }

        private bool IsElementInLevel(IIfcProduct element, IIfcBuildingStorey level)
        {
            // Check spatial containment relationships
            foreach (var rel in _model.Instances.OfType<IIfcRelContainedInSpatialStructure>())
            {
                if (rel.RelatingStructure == level && rel.RelatedElements.Contains(element))
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyScopeBox(List<IPersistEntity> elementsToShow)
        {
            if (elementsToShow == null || !elementsToShow.Any()) return;

            try
            {
                // Create a lightweight in-memory model
                using (var filteredModel = IfcStore.Create(_model.SchemaVersion, XbimStoreType.InMemoryModel))
                {
                    using (var txn = filteredModel.BeginTransaction("Filter elements"))
                    {
                        var map = new XbimInstanceHandleMap(_model, filteredModel);

                        foreach (var entity in elementsToShow.OfType<IIfcProduct>())
                        {
                            filteredModel.InsertCopy(entity, map, (src, tgt) => true, true, true);
                        }

                        txn.Commit();
                    }

                    var context = new Xbim3DModelContext(filteredModel);
                    context.CreateContext();

                    ModelProvider.ObjectInstance = null;
                    ModelProvider.ObjectInstance = filteredModel;

                    DrawingControl.Viewport?.ZoomExtents();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying scope box: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        public List<string> GetAvailableLevels()
        {
            if (_model == null) return new List<string>();

            return _model.Instances
                .OfType<IIfcBuildingStorey>()
                .Select(l => l.Name.ToString())
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
        }

        public List<string> GetRoomsForLevel(string levelName)
        {
            if (_model == null) return new List<string>();

            var level = _model.Instances
                .OfType<IIfcBuildingStorey>()
                .FirstOrDefault(l => l.Name == levelName);

            if (level == null) return new List<string>();

            var rooms = level.ContainsElements
                .SelectMany(rel => rel.RelatedElements)
                .OfType<IIfcSpace>()
                .Select(r => !string.IsNullOrEmpty(r.LongName?.ToString())
                    ? r.LongName.ToString()
                    : r.Name.ToString())
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();

            return rooms;
        }
    }
}