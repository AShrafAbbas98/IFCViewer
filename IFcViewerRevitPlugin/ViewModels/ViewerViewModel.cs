using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IFcViewerRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;

namespace IFcViewerRevitPlugin.ViewModels
{
    /// <summary>
    /// Optimized ViewerViewModel with async loading and better performance
    /// </summary>
    public class ViewerViewModel : ObservableObject, IDisposable
    {
        private readonly IfcModelWrapper _ifcModel = new IfcModelWrapper();
        private readonly UIDocument _uiDocument;
        private bool _isLoading;

        public ViewerViewModel(UIDocument uiDocument)
        {
            _uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
            LoadIfcCommand = new RelayCommand(async () => await LoadIfcAsync(), () => !_isLoading);
            FilterByLevelCommand = new RelayCommand<string>(FilterByLevel);
            FilterByRoomCommand = new RelayCommand<string>(FilterByRoom);
            ResetViewCommand = new RelayCommand(ResetView);
        }

        #region Events

        public event EventHandler ModelLoaded;
        public event EventHandler<XbimRect3D> ApplySectionBox;
        public event EventHandler<IEnumerable<int>> ElementsFilterChanged;

        #endregion

        #region Properties

        private string _status = "Ready";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private IfcStore _model;
        public IfcStore Model
        {
            get => _model;
            private set
            {
                if (SetProperty(ref _model, value) && value != null)
                {
                    ModelLoaded?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private string _selectedLevel;
        public string SelectedLevel
        {
            get => _selectedLevel;
            set => SetProperty(ref _selectedLevel, value);
        }

        private string _selectedRoom;
        public string SelectedRoom
        {
            get => _selectedRoom;
            set => SetProperty(ref _selectedRoom, value);
        }

        public ObservableCollection<string> Levels { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Rooms { get; } = new ObservableCollection<string>();

        #endregion

        #region Commands

        public IRelayCommand LoadIfcCommand { get; }
        public IRelayCommand<string> FilterByLevelCommand { get; }
        public IRelayCommand<string> FilterByRoomCommand { get; }
        public IRelayCommand ResetViewCommand { get; }

        #endregion

        #region Optimized Loading

        /// <summary>
        /// Async IFC loading with progress feedback and multi-threaded geometry generation
        /// </summary>
        private async Task LoadIfcAsync()
        {
            if (_isLoading) return;

            _isLoading = true;
            ((RelayCommand)LoadIfcCommand).NotifyCanExecuteChanged();

            try
            {
                Status = "Exporting to IFC...";

                // Export on UI thread (Revit API requirement)
                string ifcPath = ExportRevitToIfc();

                if (string.IsNullOrEmpty(ifcPath) || !File.Exists(ifcPath))
                {
                    Status = "Export failed";
                    return;
                }

                Status = "Loading IFC model...";

                // Load file structure (fast)
                await Task.Run(() => _ifcModel.LoadIfcFile(ifcPath));

                if (_ifcModel.Model == null)
                {
                    Status = "Failed to load model";
                    return;
                }

                Status = "Creating 3D geometry (this may take a moment)...";

                // Generate geometry on background thread with multiple cores
                await Task.Run(() =>
                {
                    if (_ifcModel.Model.GeometryStore.IsEmpty)
                    {
                        try
                        {
                            var context = new Xbim3DModelContext(_ifcModel.Model)
                            {
                                // Use multiple threads for geometry generation (much faster!)
                                // Reduce from max to avoid overwhelming the system
                                MaxThreads = Math.Max(2, Environment.ProcessorCount - 1)
                            };

                            // Create geometry with progress callback
                            // ReportProgressDelegate signature: (int percentProgress, object userState)
                            context.CreateContext(
                                progDelegate: (percentProgress, userState) =>
                                {
                                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        Status = $"Creating geometry: {percentProgress}%";
                                    }));
                                },
                                adjustWcs: true
                            );
                        }
                        catch (Exception geomEx)
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                Status = $"Geometry creation failed: {geomEx.Message}";
                                MessageBox.Show(
                                    $"Error creating geometry: {geomEx.Message}\n\nThe model may not display correctly.",
                                    "Geometry Warning",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                            });
                        }
                    }
                });

                // Set model on UI thread
                Model = _ifcModel.Model;

                // Populate UI lists
                await Task.Run(() => PopulateLevelsFromIfc());

                Status = $"Model loaded - {Model.Instances.Count} instances";
            }
            catch (Exception ex)
            {
                Status = "Error loading model";
                MessageBox.Show(
                    $"Error: {ex.Message}\n\nStack Trace: {ex.StackTrace}",
                    "IFC Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
                ((RelayCommand)LoadIfcCommand).NotifyCanExecuteChanged();
            }
        }

        private string ExportRevitToIfc()
        {
            var doc = _uiDocument.Document;
            if (doc == null)
            {
                MessageBox.Show("No active document found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            string tempPath = Path.Combine(Path.GetTempPath(), "RevitIFCExport");
            if (!Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);

            string fileName = $"{SanitizeFileName(doc.Title)}_{DateTime.Now:yyyyMMdd_HHmmss}.ifc";
            string fullPath = Path.Combine(tempPath, fileName);

            try
            {
                // Optimized IFC export options for better performance
                IFCExportOptions ifcOptions = new IFCExportOptions
                {
                    FileVersion = IFCVersion.IFC2x3CV2,
                    WallAndColumnSplitting = false,  // Disable for faster export
                    ExportBaseQuantities = false,     // Disable if not needed
                    SpaceBoundaryLevel = 1,
                    // Add tessellation settings for better geometry
                    //TessellationLevelOfDetail = 0.5  // Lower = faster but less detail
                };

                using (var trans = new Transaction(doc, "Export IFC"))
                {
                    trans.Start();
                    doc.Export(tempPath, fileName, ifcOptions);
                    trans.Commit();
                }

                Status = $"Exported to: {fullPath}";
                return fullPath;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to IFC: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private string SanitizeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        #endregion

        #region Populate UI lists

        private void PopulateLevelsFromIfc()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Levels.Clear();
                Rooms.Clear();
                SelectedLevel = null;
                SelectedRoom = null;
            });

            if (Model == null) return;

            try
            {
                var storeys = Model.Instances.OfType<IIfcBuildingStorey>().ToList();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var storey in storeys)
                    {
                        string name = storey.Name ?? storey.LongName ?? $"Level_{storey.EntityLabel}";
                        Levels.Add(name);
                    }

                    if (Levels.Count == 0)
                    {
                        Status = "No levels found in model";
                    }
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Status = $"Error populating levels: {ex.Message}";
                });
            }
        }

        #endregion

        #region Filtering

        public void FilterByLevel(string level)
        {
            if (string.IsNullOrEmpty(level) || Model == null) return;

            Status = $"Filtering by level: {level}";
            Rooms.Clear();

            try
            {
                var storey = Model.Instances.OfType<IIfcBuildingStorey>()
                    .FirstOrDefault(s => s.Name == level || s.LongName == level);

                if (storey == null)
                {
                    Status = $"Level not found: {level}";
                    return;
                }

                var spaces = Model.Instances.OfType<IIfcSpace>()
                    .Where(s => s.Decomposes.Any(rel => rel.RelatingObject == storey))
                    .ToList();

                foreach (var space in spaces)
                {
                    string roomName = space.Name ?? space.LongName ?? $"Space_{space.EntityLabel}";
                    Rooms.Add(roomName);
                }

                var bbox = CalculateBoundingBoxForStorey(storey);
                if (bbox.HasValue)
                {
                    ApplySectionBox?.Invoke(this, bbox.Value);
                }

                var labels = GetEntityLabelsForStorey(storey);
                ElementsFilterChanged?.Invoke(this, labels);

                SelectedLevel = level;
                SelectedRoom = null;
                Status = $"Level '{level}': {labels.Count} elements";
            }
            catch (Exception ex)
            {
                Status = $"Error filtering by level: {ex.Message}";
            }
        }

        public void FilterByRoom(string room)
        {
            if (string.IsNullOrEmpty(room) || Model == null) return;

            try
            {
                var space = Model.Instances.OfType<IIfcSpace>()
                    .FirstOrDefault(s => s.Name == room || s.LongName == room);

                if (space == null)
                {
                    Status = $"Room not found: {room}";
                    return;
                }

                var bbox = CalculateBoundingBoxForSpace(space);
                if (bbox.HasValue)
                {
                    ApplySectionBox?.Invoke(this, bbox.Value);
                }

                var labels = GetEntityLabelsForSpace(space);

                if (labels.Count < 2)
                {
                    Status = $"Room '{room}': Using spatial filter";
                    labels = GetEntityLabelsIntersectingBbox(bbox.Value);
                }
                else
                {
                    Status = $"Room '{room}': {labels.Count} elements";
                }

                ElementsFilterChanged?.Invoke(this, labels);
                SelectedRoom = room;
            }
            catch (Exception ex)
            {
                Status = $"Error filtering by room: {ex.Message}";
            }
        }

        #endregion

        #region Geometry Helpers

        private XbimRect3D? CalculateBoundingBoxForStorey(IIfcBuildingStorey storey)
        {
            try
            {
                XbimRect3D? bounds = null;

                foreach (var rel in storey.ContainsElements)
                {
                    foreach (var related in rel.RelatedElements)
                    {
                        if (related is IIfcProduct product)
                        {
                            var b = GetBoundingBoxForProduct(product);
                            if (b.HasValue) bounds = ExpandBounds(bounds, b.Value);
                        }
                    }
                }

                if (bounds.HasValue)
                {
                    var bb = bounds.Value;
                    var padding = new XbimVector3D(bb.SizeX * 0.05, bb.SizeY * 0.05, bb.SizeZ * 0.1);
                    return new XbimRect3D(
                        bb.X - padding.X, bb.Y - padding.Y, bb.Z - padding.Z,
                        bb.SizeX + padding.X * 2, bb.SizeY + padding.Y * 2, bb.SizeZ + padding.Z * 2
                    );
                }

                return bounds;
            }
            catch (Exception ex)
            {
                Status = $"Error calculating bounds: {ex.Message}";
                return null;
            }
        }

        private XbimRect3D? CalculateBoundingBoxForSpace(IIfcSpace space)
        {
            try
            {
                var bounds = GetBoundingBoxForProduct(space);
                if (bounds.HasValue)
                {
                    var b = bounds.Value;
                    var padding = new XbimVector3D(0.5, 0.5, 0.3);
                    return new XbimRect3D(
                        b.X - padding.X, b.Y - padding.Y, b.Z - padding.Z,
                        b.SizeX + padding.X * 2, b.SizeY + padding.Y * 2, b.SizeZ + padding.Z * 2
                    );
                }
                return bounds;
            }
            catch (Exception ex)
            {
                Status = $"Error calculating space bounds: {ex.Message}";
                return null;
            }
        }

        private XbimRect3D? GetBoundingBoxForProduct(IIfcProduct product)
        {
            try
            {
                if (product == null || Model == null) return null;

                using (var geomReader = Model.GeometryStore.BeginRead())
                {
                    var shapes = geomReader.ShapeInstancesOfEntity(product.EntityLabel);
                    XbimRect3D? bounds = null;
                    foreach (var shape in shapes)
                    {
                        bounds = ExpandBounds(bounds, shape.BoundingBox);
                    }
                    return bounds;
                }
            }
            catch
            {
                return null;
            }
        }

        private XbimRect3D? ExpandBounds(XbimRect3D? current, XbimRect3D newBounds)
        {
            if (!current.HasValue) return newBounds;

            var c = current.Value;
            var minX = Math.Min(c.X, newBounds.X);
            var minY = Math.Min(c.Y, newBounds.Y);
            var minZ = Math.Min(c.Z, newBounds.Z);
            var maxX = Math.Max(c.X + c.SizeX, newBounds.X + newBounds.SizeX);
            var maxY = Math.Max(c.Y + c.SizeY, newBounds.Y + newBounds.SizeY);
            var maxZ = Math.Max(c.Z + c.SizeZ, newBounds.Z + newBounds.SizeZ);

            return new XbimRect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
        }

        private HashSet<int> GetEntityLabelsForStorey(IIfcBuildingStorey storey)
        {
            var labels = new HashSet<int>();
            if (storey == null || Model == null) return labels;

            try
            {
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
                labels.Add(storey.EntityLabel);
            }
            catch { }

            return labels;
        }

        private HashSet<int> GetEntityLabelsForSpace(IIfcSpace space)
        {
            var labels = new HashSet<int>();
            if (space == null || Model == null) return labels;

            try
            {
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
                labels.Add(space.EntityLabel);
            }
            catch { }

            return labels;
        }

        private HashSet<int> GetEntityLabelsIntersectingBbox(XbimRect3D targetBbox)
        {
            var labels = new HashSet<int>();
            if (Model == null) return labels;

            try
            {
                var products = Model.Instances.OfType<IIfcProduct>();
                foreach (var product in products)
                {
                    var productBox = GetBoundingBoxForProduct(product);
                    if (productBox.HasValue && BoundsIntersect(targetBbox, productBox.Value))
                    {
                        labels.Add(product.EntityLabel);
                    }
                }
            }
            catch { }

            return labels;
        }

        private bool BoundsIntersect(XbimRect3D b1, XbimRect3D b2)
        {
            return !(b1.X + b1.SizeX < b2.X || b2.X + b2.SizeX < b1.X ||
                     b1.Y + b1.SizeY < b2.Y || b2.Y + b2.SizeY < b1.Y ||
                     b1.Z + b1.SizeZ < b2.Z || b2.Z + b2.SizeZ < b1.Z);
        }

        #endregion

        #region Reset / Cleanup

        public void ResetView()
        {
            Status = "Resetting view...";
            Rooms.Clear();
            SelectedLevel = null;
            SelectedRoom = null;

            ApplySectionBox?.Invoke(this, new XbimRect3D());
            ElementsFilterChanged?.Invoke(this, null);

            Status = "View reset";
        }

        public void Cleanup()
        {
            Dispose();
        }

        public void Dispose()
        {
            _ifcModel?.Dispose();
            Model = null;
        }

        #endregion
    }
}