using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;
using Xbim.Presentation;
using Visibility = System.Windows.Visibility;
namespace IFcViewerRevitPlugin.Views
{
    public partial class ViewerWindow : Window
    {
        private readonly Document _document;
        private ExternalEvent _externalEvent;
        private ViewerExternalEventHandler _eventHandler;
        private IfcStore _model;
        private bool _isLoading;
        private Dictionary<string, IIfcBuildingStorey> _levelCache;
        private Dictionary<string, IIfcSpace> _roomCache;
        private bool _isUpdatingSelection; // Prevent recursive selection changes

        public ViewerWindow(Document document)
        {
            InitializeComponent();
            _document = document;
            _levelCache = new Dictionary<string, IIfcBuildingStorey>();
            _roomCache = new Dictionary<string, IIfcSpace>();
            _isUpdatingSelection = false;

            Closing += ViewerWindow_Closing;
            DrawingControl.Loaded += DrawingControl_Loaded;
        }

        public void SetExternalEvent(ExternalEvent externalEvent, ViewerExternalEventHandler eventHandler)
        {
            _externalEvent = externalEvent;
            _eventHandler = eventHandler;
        }

        private void DrawingControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                DrawingControl.ShowGridLines = false;
                DrawingControl.ModelOpacity = 1.0;

                // Performance optimizations
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                    DrawingControl,
                    System.Windows.Media.BitmapScalingMode.LowQuality);

                System.Windows.Media.RenderOptions.SetEdgeMode(
                    DrawingControl,
                    System.Windows.Media.EdgeMode.Aliased);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing 3D control: {ex.Message}", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadModel_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            try
            {
                StatusText.Text = "Requesting IFC export from Revit...";
                LoadButton.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = true;

                _eventHandler.SetContext(_document, async (ifcPath) =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await LoadIfcFileAsync(ifcPath);
                    });
                });

                var result = _externalEvent.Raise();

                if (result != ExternalEventRequest.Accepted)
                {
                    MessageBox.Show("Failed to queue export request. Please try again.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    LoadButton.IsEnabled = true;
                    ProgressBar.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                LoadButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadIfcFileAsync(string ifcPath)
        {
            if (_isLoading || string.IsNullOrEmpty(ifcPath)) return;

            _isLoading = true;

            try
            {
                StatusText.Text = "Loading IFC model...";
                ProgressBar.IsIndeterminate = true;

                // Dispose previous model
                _model?.Dispose();
                _levelCache?.Clear();
                _roomCache?.Clear();

                // Load and create geometry on background thread
                await Task.Run(() =>
                {
                    _model = IfcStore.Open(ifcPath);

                    if (_model.GeometryStore.IsEmpty)
                    {
                        int instanceCount = _model.Instances.Count();
                        int maxThreads = DetermineOptimalThreads(instanceCount);
                        double deflection = DetermineDeflection(instanceCount);

                        var context = new Xbim3DModelContext(_model)
                        {
                            MaxThreads = maxThreads
                        };

                        Dispatcher.Invoke(() =>
                        {
                            ProgressBar.IsIndeterminate = false;
                            ProgressBar.Maximum = 100;
                        });

                        context.CreateContext(
                            progDelegate: (progress, state) =>
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try
                                    {
                                        ProgressBar.Value = progress;
                                        StatusText.Text = $"Creating geometry: {progress}% (detail: {GetDetailDescription(deflection)}, threads: {maxThreads})";
                                    }
                                    catch { }
                                }), System.Windows.Threading.DispatcherPriority.Normal);
                            },
                            adjustWcs: true
                        );
                    }
                });

                // Set model on UI thread
                DrawingControl.Model = _model;

                // Populate levels and rooms
                await PopulateLevelsAndRoomsAsync();

                // Zoom to fit
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        DrawingControl.ViewHome();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ViewHome error: {ex.Message}");
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                StatusText.Text = $"Loaded: {Path.GetFileName(ifcPath)} ({_model.Instances.Count():N0} instances)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading model: {ex.Message}\n\n{ex.StackTrace}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error loading model";
            }
            finally
            {
                _isLoading = false;
                LoadButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
                ProgressBar.IsIndeterminate = false;
            }
        }

        private int DetermineOptimalThreads(int instanceCount)
        {
            int availableCores = Math.Max(1, Environment.ProcessorCount - 2);

            if (instanceCount < 500) return Math.Min(2, availableCores);
            if (instanceCount < 2000) return Math.Min(3, availableCores);
            if (instanceCount < 10000) return Math.Min(4, availableCores);

            return Math.Min(6, availableCores);
        }

        private double DetermineDeflection(int instanceCount)
        {
            if (instanceCount < 1000) return 0.01;
            if (instanceCount < 5000) return 0.05;
            if (instanceCount < 10000) return 0.1;
            if (instanceCount < 20000) return 0.15;
            return 0.25;
        }

        private string GetDetailDescription(double deflection)
        {
            if (deflection <= 0.01) return "High";
            if (deflection <= 0.05) return "Medium-High";
            if (deflection <= 0.1) return "Medium";
            if (deflection <= 0.15) return "Medium-Low";
            return "Low";
        }

        private async Task PopulateLevelsAndRoomsAsync()
        {
            if (_model == null) return;

            await Task.Run(() =>
            {
                var storeys = _model.Instances.OfType<IIfcBuildingStorey>().ToList();

                var sortedStoreys = storeys
                    .OrderBy(s =>
                    {
                        try
                        {
                            if (s.Elevation.HasValue)
                                return s.Elevation.Value.Value;
                            return double.NegativeInfinity;
                        }
                        catch
                        {
                            return double.NegativeInfinity;
                        }
                    })
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    LevelCombo.Items.Clear();
                    RoomCombo.Items.Clear();
                    _levelCache.Clear();

                    LevelCombo.Items.Add("-- All Levels --");

                    foreach (var storey in sortedStoreys)
                    {
                        string name = storey.Name ?? storey.LongName ?? $"Level_{storey.EntityLabel}";

                        // Add elevation info if available
                        if (storey.Elevation.HasValue)
                        {
                            name = $"{name} ({storey.Elevation.Value.Value:F2}m)";
                        }

                        LevelCombo.Items.Add(name);
                        _levelCache[name] = storey;
                    }

                    LevelCombo.SelectedIndex = 0;
                });
            });
        }

        private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelection || _model == null || LevelCombo.SelectedIndex < 0) return;

            if (LevelCombo.SelectedIndex == 0)
            {
                // Show all levels
                DrawingControl.HiddenInstances = null;
                DrawingControl.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);

                _isUpdatingSelection = true;
                RoomCombo.Items.Clear();
                RoomCombo.IsEnabled = false;
                _roomCache.Clear();
                _isUpdatingSelection = false;

                StatusText.Text = "Showing all levels";
                return;
            }

            // Capture the selected item on UI thread before going to background
            string selectedLevel = LevelCombo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selectedLevel))
            {
                Task.Run(() => FilterByLevel(selectedLevel));
            }
        }

        private void FilterByLevel(string levelDisplayName)
        {
            try
            {
                if (!_levelCache.TryGetValue(levelDisplayName, out var storey))
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = "Level not found in cache";
                    });
                    return;
                }

                // Get all elements on this level
                var visibleLabels = new HashSet<int> { storey.EntityLabel };

                foreach (var rel in storey.ContainsElements)
                {
                    foreach (var related in rel.RelatedElements)
                    {
                        if (related is IIfcProduct product)
                        {
                            visibleLabels.Add(product.EntityLabel);
                        }
                    }
                }

                // Get spaces/rooms on this level - try multiple relationship types
                var spaces = _model.Instances.OfType<IIfcSpace>()
                    .Where(s =>
                    {
                        // Check if space is on this storey through decomposition
                        if (s.Decomposes.Any(rel => rel.RelatingObject == storey))
                            return true;

                        // Alternative: Check if space is contained in this storey
                        foreach (var relContained in storey.ContainsElements)
                        {
                            if (relContained.RelatedElements.Contains(s))
                                return true;
                        }

                        return false;
                    })
                    .ToList();

                // If no spaces found through relationships, try spatial query (fallback)
                if (!spaces.Any())
                {
                    var storeyBounds = GetBoundingBoxForProduct(storey);
                    if (storeyBounds.HasValue)
                    {
                        spaces = _model.Instances.OfType<IIfcSpace>()
                            .Where(s =>
                            {
                                var spaceBounds = GetBoundingBoxForProduct(s);
                                return spaceBounds.HasValue && BoundsIntersect(storeyBounds.Value, spaceBounds.Value);
                            })
                            .ToList();
                    }
                }

                // Hide all elements not in visible set
                var allProducts = _model.Instances.OfType<IIfcProduct>().ToList();
                var hiddenList = allProducts
                    .Where(p => !visibleLabels.Contains(p.EntityLabel))
                    .Cast<Xbim.Common.IPersistEntity>()
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Update rooms list
                        _isUpdatingSelection = true;

                        RoomCombo.Items.Clear();
                        _roomCache.Clear();
                        RoomCombo.Items.Add("-- All Rooms --");

                        int roomCount = 0;
                        foreach (var space in spaces)
                        {
                            string roomName = space.Name ?? space.LongName ?? $"Space_{space.EntityLabel}";
                            RoomCombo.Items.Add(roomName);
                            _roomCache[roomName] = space;
                            roomCount++;
                        }

                        // Set selection and enable
                        RoomCombo.SelectedIndex = 0;
                        RoomCombo.IsEnabled = roomCount > 0;

                        _isUpdatingSelection = false;

                        // Apply visibility filter
                        DrawingControl.HiddenInstances = hiddenList;
                        DrawingControl.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);

                        string levelName = levelDisplayName.Split('(')[0].Trim();
                        StatusText.Text = $"Level: {levelName} - {visibleLabels.Count:N0} visible, {hiddenList.Count:N0} hidden, {roomCount} rooms";
                    }
                    catch (Exception innerEx)
                    {
                        _isUpdatingSelection = false;
                        StatusText.Text = $"Error updating UI: {innerEx.Message}";
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = $"Error filtering: {ex.Message}";
                });
            }
        }

        private void RoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelection || _model == null || RoomCombo.SelectedIndex < 0) return;

            if (RoomCombo.SelectedIndex == 0)
            {
                // Reset to show all elements of current level
                if (LevelCombo.SelectedIndex > 0)
                {
                    string selectedLevel = LevelCombo.SelectedItem?.ToString();
                    if (!string.IsNullOrEmpty(selectedLevel))
                    {
                        Task.Run(() => FilterByLevel(selectedLevel));
                    }
                }
                return;
            }

            // Capture the selected item on UI thread before going to background
            string selectedRoom = RoomCombo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selectedRoom))
            {
                Task.Run(() => FilterByRoom(selectedRoom));
            }
        }

        private void FilterByRoom(string roomDisplayName)
        {
            try
            {
                if (!_roomCache.TryGetValue(roomDisplayName, out var space))
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = "Room not found in cache";
                    });
                    return;
                }

                // Get bounding box for the room
                var roomBounds = GetBoundingBoxForProduct(space);

                if (!roomBounds.HasValue)
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = "Could not calculate room bounds";
                    });
                    return;
                }

                // Find all elements that intersect with the room bounds
                var visibleLabels = new HashSet<int> { space.EntityLabel };

                // Check elements contained in space
                foreach (var rel in space.ContainsElements)
                {
                    foreach (var related in rel.RelatedElements)
                    {
                        if (related is IIfcProduct product)
                        {
                            visibleLabels.Add(product.EntityLabel);
                        }
                    }
                }

                // Fallback: Use spatial intersection if no contained elements
                if (visibleLabels.Count < 2)
                {
                    var allProducts = _model.Instances.OfType<IIfcProduct>().ToList();

                    foreach (var product in allProducts)
                    {
                        var productBounds = GetBoundingBoxForProduct(product);
                        if (productBounds.HasValue && BoundsIntersect(roomBounds.Value, productBounds.Value))
                        {
                            visibleLabels.Add(product.EntityLabel);
                        }
                    }
                }

                // Hide elements not in room
                var allProductsForHiding = _model.Instances.OfType<IIfcProduct>().ToList();
                var hiddenList = allProductsForHiding
                    .Where(p => !visibleLabels.Contains(p.EntityLabel))
                    .Cast<Xbim.Common.IPersistEntity>()
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    DrawingControl.HiddenInstances = hiddenList;
                    DrawingControl.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);

                    // Zoom to room bounds
                    ZoomToBounds(roomBounds.Value);

                    StatusText.Text = $"Room: {roomDisplayName} - {visibleLabels.Count:N0} visible, {hiddenList.Count:N0} hidden";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = $"Error filtering room: {ex.Message}";
                });
            }
        }

        private XbimRect3D? GetBoundingBoxForProduct(IIfcProduct product)
        {
            try
            {
                if (product == null || _model == null) return null;

                using (var geomReader = _model.GeometryStore.BeginRead())
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

        private bool BoundsIntersect(XbimRect3D b1, XbimRect3D b2)
        {
            return !(b1.X + b1.SizeX < b2.X || b2.X + b2.SizeX < b1.X ||
                     b1.Y + b1.SizeY < b2.Y || b2.Y + b2.SizeY < b1.Y ||
                     b1.Z + b1.SizeZ < b2.Z || b2.Z + b2.SizeZ < b1.Z);
        }

        private void ZoomToBounds(XbimRect3D bounds)
        {
            try
            {
                // Add padding to bounds
                var padding = 1.5; // 50% extra space around
                var paddedBounds = new XbimRect3D(
                    bounds.X - bounds.SizeX * 0.25,
                    bounds.Y - bounds.SizeY * 0.25,
                    bounds.Z - bounds.SizeZ * 0.25,
                    bounds.SizeX * padding,
                    bounds.SizeY * padding,
                    bounds.SizeZ * padding
                );

                // Convert to WPF Rect3D for camera positioning
                var center = new Point3D(
                    paddedBounds.X + paddedBounds.SizeX / 2,
                    paddedBounds.Y + paddedBounds.SizeY / 2,
                    paddedBounds.Z + paddedBounds.SizeZ / 2
                );

                var maxSize = Math.Max(Math.Max(paddedBounds.SizeX, paddedBounds.SizeY), paddedBounds.SizeZ);

                // FIXED: Use Viewport.Camera (the correct API)
                DrawingControl.Viewport.Camera.Position = new Point3D(
                    center.X + maxSize,
                    center.Y + maxSize,
                    center.Z + maxSize
                );

                DrawingControl.Viewport.Camera.LookDirection = new Vector3D(
                    -maxSize, -maxSize, -maxSize
                );

                // Optional: Adjust FOV for better framing (if PerspectiveCamera)
                if (DrawingControl.Viewport.Camera is PerspectiveCamera perspectiveCam)
                {
                    perspectiveCam.FieldOfView = 60;
                }

                // Force refresh
                DrawingControl.InvalidateVisual();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error zooming to bounds: {ex.Message}");
            }
        }
        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            if (_model == null) return;

            try
            {
                _isUpdatingSelection = true;

                DrawingControl.HiddenInstances = null;
                DrawingControl.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);

                LevelCombo.SelectedIndex = 0;
                RoomCombo.Items.Clear();
                RoomCombo.IsEnabled = false;
                _roomCache.Clear();

                _isUpdatingSelection = false;

                DrawingControl.ViewHome();
                StatusText.Text = "View reset - showing all elements";
            }
            catch (Exception ex)
            {
                _isUpdatingSelection = false;
                StatusText.Text = $"Error resetting view: {ex.Message}";
            }
        }

        private void ViewerWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                DrawingControl.Model = null;
                _model?.Dispose();
                _model = null;
                _levelCache?.Clear();
                _roomCache?.Clear();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }
    }
}