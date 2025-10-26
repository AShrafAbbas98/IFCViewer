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

        // Main model cache
        private IfcStore _mainModel;
        private string _mainIfcPath;

        // Current view model
        private IfcStore _currentModel;

        // Room-specific caches
        private Dictionary<string, string> _roomIfcCache; // RoomName -> IFC file path
        private Dictionary<string, IfcStore> _roomModelCache; // RoomName -> Loaded IfcStore

        private bool _isLoading;
        private Dictionary<string, IIfcBuildingStorey> _levelCache;
        private Dictionary<string, IIfcSpace> _roomCache;
        private bool _isUpdatingSelection;

        // Track current context
        private bool _isViewingRoom;
        private string _currentRoomName;

        public ViewerWindow(Document document)
        {
            InitializeComponent();
            _document = document;
            _levelCache = new Dictionary<string, IIfcBuildingStorey>();
            _roomCache = new Dictionary<string, IIfcSpace>();
            _roomIfcCache = new Dictionary<string, string>();
            _roomModelCache = new Dictionary<string, IfcStore>();
            _isUpdatingSelection = false;
            _isViewingRoom = false;

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
                        await LoadMainModelAsync(ifcPath);
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

        private async Task LoadMainModelAsync(string ifcPath)
        {
            if (_isLoading || string.IsNullOrEmpty(ifcPath)) return;

            _isLoading = true;

            try
            {
                StatusText.Text = "Loading main IFC model...";
                ProgressBar.IsIndeterminate = true;

                // Store main IFC path for later use
                _mainIfcPath = ifcPath;

                // Dispose previous models
                _mainModel?.Dispose();
                _currentModel?.Dispose();

                // Clear room caches (we'll rebuild them)
                foreach (var cachedModel in _roomModelCache.Values)
                {
                    cachedModel?.Dispose();
                }
                _roomModelCache.Clear();

                _levelCache?.Clear();
                _roomCache?.Clear();

                // Load main model
                await Task.Run(() =>
                {
                    _mainModel = IfcStore.Open(ifcPath);

                    if (_mainModel.GeometryStore.IsEmpty)
                    {
                        int instanceCount = _mainModel.Instances.Count();
                        int maxThreads = DetermineOptimalThreads(instanceCount);
                        double deflection = DetermineDeflection(instanceCount);

                        var context = new Xbim3DModelContext(_mainModel)
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

                // Set as current model
                _currentModel = _mainModel;
                DrawingControl.Model = _currentModel;

                // Reset view state
                _isViewingRoom = false;
                _currentRoomName = null;
                BackToMainButton.Visibility = Visibility.Collapsed;

                // Populate levels
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

                StatusText.Text = $"Loaded: {Path.GetFileName(ifcPath)} ({_mainModel.Instances.Count():N0} instances)";
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

        private void RoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelection || _mainModel == null || RoomCombo.SelectedIndex < 0) return;

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

            // Capture the selected item
            string selectedRoom = RoomCombo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selectedRoom))
            {
                Task.Run(async () => await LoadRoomScopedViewAsync(selectedRoom));
            }
        }

        #region Clipping Plane Methods

        private void ClearClippingPlanes()
        {
            try
            {
                DrawingControl.ClearCutPlane();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing clipping planes: {ex.Message}");
            }
        }

        private void SetRoomClippingPlane(IIfcSpace space)
        {
            if (space == null || _mainModel == null) return;

            try
            {
                // Get the bounding box of the room
                var roomBounds = GetBoundingBoxForProduct(space);
                if (!roomBounds.HasValue) return;

                var bounds = roomBounds.Value;

                // Add padding to include walls and adjacent elements
                double padding = 0.5; // meters
                var paddedBounds = new XbimRect3D(
                    bounds.X - padding, bounds.Y - padding, bounds.Z - padding,
                    bounds.SizeX + padding * 2, bounds.SizeY + padding * 2, bounds.SizeZ + padding * 2
                );

                // Create clipping planes for all 6 sides of the bounding box
                SetBoundingBoxClippingPlanes(paddedBounds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting room clipping: {ex.Message}");
            }
        }

        private void SetBoundingBoxClippingPlanes(XbimRect3D bounds)
        {
            try
            {
                // Clear any existing clipping planes
                ClearClippingPlanes();

                // Create clipping planes for each face of the bounding box
                // This creates a "scope box" effect by clipping everything outside

                // Front plane (Y min)
                DrawingControl.SetCutPlane(
                    bounds.X, bounds.Y, bounds.Z,
                    0, 1, 0  // Normal pointing inward (positive Y)
                );

                // Back plane (Y max)  
                DrawingControl.SetCutPlane(
                    bounds.X, bounds.Y + bounds.SizeY, bounds.Z,
                    0, -1, 0  // Normal pointing inward (negative Y)
                );

                // Left plane (X min)
                DrawingControl.SetCutPlane(
                    bounds.X, bounds.Y, bounds.Z,
                    1, 0, 0  // Normal pointing inward (positive X)
                );

                // Right plane (X max)
                DrawingControl.SetCutPlane(
                    bounds.X + bounds.SizeX, bounds.Y, bounds.Z,
                    -1, 0, 0  // Normal pointing inward (negative X)
                );

                // Bottom plane (Z min)
                DrawingControl.SetCutPlane(
                    bounds.X, bounds.Y, bounds.Z,
                    0, 0, 1  // Normal pointing upward (positive Z)
                );

                // Top plane (Z max)
                DrawingControl.SetCutPlane(
                    bounds.X, bounds.Y, bounds.Z + bounds.SizeZ,
                    0, 0, -1  // Normal pointing downward (negative Z)
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting bounding box clipping: {ex.Message}");
            }
        }

        private void SetSingleClippingPlane(double px, double py, double pz, double nx, double ny, double nz)
        {
            try
            {
                ClearClippingPlanes();
                DrawingControl.SetCutPlane(px, py, pz, nx, ny, nz);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting single clipping plane: {ex.Message}");
            }
        }

        #endregion
        private async Task LoadRoomScopedViewAsync(string roomDisplayName)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = $"Loading room view: {roomDisplayName}...";
                    ProgressBar.Visibility = Visibility.Visible;
                    ProgressBar.IsIndeterminate = true;
                });

                // Make sure we're using the main model
                if (_currentModel != _mainModel)
                {
                    _currentModel = _mainModel;
                    DrawingControl.Model = _currentModel;
                }

                if (!_roomCache.TryGetValue(roomDisplayName, out var space))
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = "Room not found in cache";
                        ProgressBar.Visibility = Visibility.Collapsed;
                    });
                    return;
                }

                // Use the single clipping plane approach
                Dispatcher.Invoke(() =>
                {
                    // First, try the hidden instances approach (more reliable)
                    //CreateRoomSectionBoxUsingHiddenInstances(space);

                    // Alternative: Try single clipping plane
                     CreateRoomSectionBox(space);

                    _isViewingRoom = true;
                    _currentRoomName = roomDisplayName;
                    BackToMainButton.Visibility = Visibility.Visible;
                    ProgressBar.Visibility = Visibility.Collapsed;
                });

            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = $"Error loading room view: {ex.Message}";
                    ProgressBar.Visibility = Visibility.Collapsed;
                });
            }
        }
        #region Hidden Instances Section Box

        private void CreateRoomSectionBoxUsingHiddenInstances(IIfcSpace space)
        {
            if (space == null || _mainModel == null) return;

            try
            {
                // Get room bounds
                var roomBounds = GetPreciseRoomBounds(space);
                if (!roomBounds.HasValue) return;

                var bounds = roomBounds.Value;

                // Get all products in the model
                var allProducts = _mainModel.Instances.OfType<IIfcProduct>().ToList();
                var productsInside = new List<Xbim.Common.IPersistEntity>();
                var productsOutside = new List<Xbim.Common.IPersistEntity>();

                foreach (var product in allProducts)
                {
                    var productBounds = GetBoundingBoxForProduct(product);
                    if (productBounds.HasValue)
                    {
                        if (IsInsideBounds(productBounds.Value, bounds))
                        {
                            productsInside.Add(product);
                        }
                        else
                        {
                            productsOutside.Add(product);
                        }
                    }
                    else
                    {
                        // If we can't determine bounds, include it
                        productsInside.Add(product);
                    }
                }

                // Hide everything outside the section box
                DrawingControl.HiddenInstances = productsOutside;
                DrawingControl.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);

                // Zoom to the section box
                ZoomToSectionBox(bounds);

                StatusText.Text = $"Room '{space.Name}' - {productsInside.Count} elements visible, {productsOutside.Count} hidden";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error creating section box: {ex.Message}";
            }
        }

        private bool IsInsideBounds(XbimRect3D elementBounds, XbimRect3D sectionBounds)
        {
            // Check if element is completely or partially inside the section bounds
            return !(elementBounds.X + elementBounds.SizeX < sectionBounds.X ||
                     elementBounds.X > sectionBounds.X + sectionBounds.SizeX ||
                     elementBounds.Y + elementBounds.SizeY < sectionBounds.Y ||
                     elementBounds.Y > sectionBounds.Y + sectionBounds.SizeY ||
                     elementBounds.Z + elementBounds.SizeZ < sectionBounds.Z ||
                     elementBounds.Z > sectionBounds.Z + sectionBounds.SizeZ);
        }

        #endregion
        private void DebugRoomBounds(IIfcSpace space)
        {
            try
            {
                var bounds = GetPreciseRoomBounds(space);
                if (bounds.HasValue)
                {
                    var b = bounds.Value;
                    System.Diagnostics.Debug.WriteLine($"Room '{space.Name}' bounds:");
                    System.Diagnostics.Debug.WriteLine($"  Min: ({b.X:F2}, {b.Y:F2}, {b.Z:F2})");
                    System.Diagnostics.Debug.WriteLine($"  Max: ({b.X + b.SizeX:F2}, {b.Y + b.SizeY:F2}, {b.Z + b.SizeZ:F2})");
                    System.Diagnostics.Debug.WriteLine($"  Size: ({b.SizeX:F2}, {b.SizeY:F2}, {b.SizeZ:F2})");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Could not determine bounds for room '{space.Name}'");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error debugging room bounds: {ex.Message}");
            }
        }

        private async Task LoadRoomScopedViewFallback(string roomDisplayName)
        {
            // Fallback to the original room export approach
            if (!_roomCache.TryGetValue(roomDisplayName, out var space)) return;

            string roomIfcPath = null;

            await Dispatcher.InvokeAsync(() =>
            {
                _eventHandler.SetRoomContext(_document, space, async (ifcPath) =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        roomIfcPath = ifcPath;
                        await LoadRoomModelAsync(roomDisplayName, ifcPath);
                    });
                });
                _externalEvent.Raise();
            });
        }
        private async Task LoadRoomModelAsync(string roomName, string ifcPath)
        {
            try
            {
                StatusText.Text = $"Loading room IFC: {roomName}...";

                // Load the room-scoped IFC file
                IfcStore roomModel = null;

                await Task.Run(() =>
                {
                    roomModel = IfcStore.Open(ifcPath);

                    if (roomModel.GeometryStore.IsEmpty)
                    {
                        int instanceCount = roomModel.Instances.Count();
                        // Use fewer threads for smaller room models
                        int maxThreads = Math.Min(3, Environment.ProcessorCount - 1);

                        var context = new Xbim3DModelContext(roomModel)
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
                                        StatusText.Text = $"Creating room geometry: {progress}%";
                                    }
                                    catch { }
                                }), System.Windows.Threading.DispatcherPriority.Normal);
                            },
                            adjustWcs: true
                        );
                    }
                });

                // Cache the room model
                _roomIfcCache[roomName] = ifcPath;
                _roomModelCache[roomName] = roomModel;

                // Set as current model
                _currentModel = roomModel;
                DrawingControl.Model = _currentModel;
                DrawingControl.ViewHome();

                _isViewingRoom = true;
                _currentRoomName = roomName;
                BackToMainButton.Visibility = Visibility.Visible;

                StatusText.Text = $"Room '{roomName}' loaded - {_currentModel.Instances.Count():N0} instances";
                ProgressBar.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading room model: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = $"Error loading room: {ex.Message}";
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void BackToMain_Click(object sender, RoutedEventArgs e)
        {
            if (_mainModel == null) return;

            try
            {
                // Clear section box
                ClearSectionBox();

                // Show all elements
                DrawingControl.HiddenInstances = null;
                DrawingControl.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);

                // Switch back to main model
                _currentModel = _mainModel;
                DrawingControl.Model = _currentModel;
                DrawingControl.ViewHome();

                _isViewingRoom = false;
                _currentRoomName = null;
                BackToMainButton.Visibility = Visibility.Collapsed;

                StatusText.Text = $"Main model - {_currentModel.Instances.Count():N0} instances";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error returning to main view: {ex.Message}";
            }
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            if (_mainModel == null) return;

            try
            {
                // Clear section box and hidden instances
                ClearSectionBox();
                DrawingControl.HiddenInstances = null;
                DrawingControl.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);

                // Return to main model if viewing room
                if (_isViewingRoom)
                {
                    BackToMain_Click(null, null);
                }

                _isUpdatingSelection = true;
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
        private void ZoomToBounds(XbimRect3D bounds)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Convert XbimRect3D to Rect3D for the viewer
                    var rect3D = new Rect3D(
                        bounds.X, bounds.Y, bounds.Z,
                        bounds.SizeX, bounds.SizeY, bounds.SizeZ
                    );

                    // This will depend on your specific DrawingControl3D implementation
                    // Most xbim viewers have a Zoom method that takes bounds
                    var zoomMethod = DrawingControl.GetType().GetMethod("Zoom", new[] { typeof(Rect3D) });
                    if (zoomMethod != null)
                    {
                        zoomMethod.Invoke(DrawingControl, new object[] { rect3D });
                    }
                    else
                    {
                        // Fallback: use ViewHome and let the clipping do the work
                        DrawingControl.ViewHome();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Zoom to bounds error: {ex.Message}");
            }
        }

        // ... (Keep all other existing methods: DetermineOptimalThreads, DetermineDeflection, 
        // GetDetailDescription, PopulateLevelsAndRoomsAsync, LevelCombo_SelectionChanged, 
        // FilterByLevel, GetBoundingBoxForProduct, ExpandBounds, BoundsIntersect, 
        // ZoomToBounds, ResetView_Click, ViewerWindow_Closing)

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
            if (_mainModel == null) return;

            await Task.Run(() =>
            {
                var storeys = _mainModel.Instances.OfType<IIfcBuildingStorey>().ToList();

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
            if (_isUpdatingSelection || _mainModel == null || LevelCombo.SelectedIndex < 0) return;

            // Return to main model if viewing a room
            if (_isViewingRoom)
            {
                BackToMain_Click(null, null);
            }

            if (LevelCombo.SelectedIndex == 0)
            {
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

                var spaces = _mainModel.Instances.OfType<IIfcSpace>()
                    .Where(s =>
                    {
                        if (s.Decomposes.Any(rel => rel.RelatingObject == storey))
                            return true;

                        foreach (var relContained in storey.ContainsElements)
                        {
                            if (relContained.RelatedElements.Contains(s))
                                return true;
                        }

                        return false;
                    })
                    .ToList();

                if (!spaces.Any())
                {
                    var storeyBounds = GetBoundingBoxForProduct(storey);
                    if (storeyBounds.HasValue)
                    {
                        spaces = _mainModel.Instances.OfType<IIfcSpace>()
                            .Where(s =>
                            {
                                var spaceBounds = GetBoundingBoxForProduct(s);
                                return spaceBounds.HasValue && BoundsIntersect(storeyBounds.Value, spaceBounds.Value);
                            })
                            .ToList();
                    }
                }

                var allProducts = _mainModel.Instances.OfType<IIfcProduct>().ToList();
                var hiddenList = allProducts
                    .Where(p => !visibleLabels.Contains(p.EntityLabel))
                    .Cast<Xbim.Common.IPersistEntity>()
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        _isUpdatingSelection = true;

                        // Clear any clipping planes from room view
                        ClearClippingPlanes();

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

                        RoomCombo.SelectedIndex = 0;
                        RoomCombo.IsEnabled = roomCount > 0;

                        _isUpdatingSelection = false;

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

        private XbimRect3D? GetBoundingBoxForProduct(IIfcProduct product)
        {
            try
            {
                if (product == null || _mainModel == null) return null;

                using (var geomReader = _mainModel.GeometryStore.BeginRead())
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

        private void TestRoomSection_Click(object sender, RoutedEventArgs e)
        {
            if (RoomCombo.SelectedIndex > 0 && _roomCache.ContainsKey(RoomCombo.SelectedItem.ToString()))
            {
                var roomName = RoomCombo.SelectedItem.ToString();
                var space = _roomCache[roomName];
                CreateRoomSectionBoxUsingHiddenInstances(space);
            }
            else
            {
                StatusText.Text = "Please select a room first";
            }
        }

        private void ClearSection_Click(object sender, RoutedEventArgs e)
        {
            ClearSectionBox();
            DrawingControl.HiddenInstances = null;
            DrawingControl.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);
            StatusText.Text = "Section box cleared";
        }
        private void ClearClip_Click(object sender, RoutedEventArgs e)
        {
            ClearClippingPlanes();
            StatusText.Text = "Clipping planes cleared";
        }

        private void ClipAbove_Click(object sender, RoutedEventArgs e)
        {
            // Example: clip everything above Z=0
            SetSingleClippingPlane(0, 0, 0, 0, 0, -1);
            StatusText.Text = "Clipping above Z=0";
        }

        private void ViewerWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                DrawingControl.Model = null;

                _mainModel?.Dispose();
                _mainModel = null;

                foreach (var cachedModel in _roomModelCache.Values)
                {
                    cachedModel?.Dispose();
                }
                _roomModelCache.Clear();

                _currentModel = null;
                _levelCache?.Clear();
                _roomCache?.Clear();
                _roomIfcCache?.Clear();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }


        #region Advanced Clipping Methods

        private void SetRoomSectionBox(IIfcSpace space)
        {
            if (space == null || _mainModel == null) return;

            try
            {
                // Get precise room bounds from IFC geometry
                var roomBounds = GetPreciseRoomBounds(space);
                if (!roomBounds.HasValue) return;

                var bounds = roomBounds.Value;

                // Create section box using xbim's clipping capabilities
                SetXbimSectionBox(bounds);

                // Zoom to the section box
                //ZoomToBounds(bounds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting room section box: {ex.Message}");
            }
        }

        private XbimRect3D? GetPreciseRoomBounds(IIfcSpace space)
        {
            try
            {
                if (space == null) return null;

                // Method 1: Try to get bounds from IFC representation
                var bounds = GetBoundingBoxForProduct(space);
                if (bounds.HasValue) return bounds;

                // Method 2: Calculate from contained elements
                return CalculateRoomBoundsFromContents(space);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting precise room bounds: {ex.Message}");
                return null;
            }
        }

        private XbimRect3D? CalculateRoomBoundsFromContents(IIfcSpace space)
        {
            try
            {
                XbimRect3D? totalBounds = null;

                // Get all elements contained in this space
                var containedElements = _mainModel.Instances
                    .Where<IIfcRelContainedInSpatialStructure>(rel => rel.RelatingStructure == space)
                    .SelectMany(rel => rel.RelatedElements)
                    .OfType<IIfcProduct>()
                    .ToList();

                // Also get bounding elements (walls, etc.)
                var boundingElements = _mainModel.Instances
                    .Where<IIfcRelSpaceBoundary>(rel => rel.RelatingSpace == space)
                    .Select(rel => rel.RelatedBuildingElement)
                    .OfType<IIfcProduct>()
                    .ToList();

                var allElements = containedElements.Union(boundingElements).Distinct();

                foreach (var element in allElements)
                {
                    var elementBounds = GetBoundingBoxForProduct(element);
                    if (elementBounds.HasValue)
                    {
                        totalBounds = totalBounds.HasValue
                            ? ExpandBounds(totalBounds.Value, elementBounds.Value)
                            : elementBounds.Value;
                    }
                }

                if (totalBounds.HasValue)
                {
                    // Add padding
                    var b = totalBounds.Value;
                    double padding = 0.5;
                    return new XbimRect3D(
                        b.X - padding, b.Y - padding, b.Z - padding,
                        b.SizeX + padding * 2, b.SizeY + padding * 2, b.SizeZ + padding * 2
                    );
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating room bounds from contents: {ex.Message}");
                return null;
            }
        }

        private void SetXbimSectionBox(XbimRect3D bounds)
        {
            try
            {
                // Clear any existing clipping
                ClearClippingPlanes();

                // Create a section box using 6 clipping planes
                // This creates a proper "scope box" effect

                // Front plane (min Y)
                DrawingControl.SetCutPlane(
                    bounds.X, bounds.Y, bounds.Z,
                    0, 1, 0  // Normal pointing in positive Y direction
                );

                // Back plane (max Y)
                DrawingControl.SetCutPlane(
                    bounds.X, bounds.Y + bounds.SizeY, bounds.Z,
                    0, -1, 0  // Normal pointing in negative Y direction
                );

                // Left plane (min X)
                DrawingControl.SetCutPlane(
                    bounds.X, bounds.Y, bounds.Z,
                    1, 0, 0  // Normal pointing in positive X direction
                );

                // Right plane (max X)
                DrawingControl.SetCutPlane(
                    bounds.X + bounds.SizeX, bounds.Y, bounds.Z,
                    -1, 0, 0  // Normal pointing in negative X direction
                );

                // Bottom plane (min Z)
                DrawingControl.SetCutPlane(
                    bounds.X, bounds.Y, bounds.Z,
                    0, 0, 1  // Normal pointing upward
                );

                // Top plane (max Z)
                DrawingControl.SetCutPlane(
                    bounds.X, bounds.Y, bounds.Z + bounds.SizeZ,
                    0, 0, -1  // Normal pointing downward
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting xbim section box: {ex.Message}");
            }
        }

        #endregion

        #region Simplified Section Box Clipping

        private void ClearSectionBox()
        {
            try
            {
                DrawingControl.ClearCutPlane();
                StatusText.Text = "Section box cleared";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing section box: {ex.Message}");
            }
        }

        private void CreateRoomSectionBox(IIfcSpace space)
        {
            if (space == null || _mainModel == null) return;

            try
            {
                // Get room bounds
                var roomBounds = GetPreciseRoomBounds(space);
                if (!roomBounds.HasValue)
                {
                    StatusText.Text = "Could not determine room boundaries";
                    return;
                }

                var bounds = roomBounds.Value;

                // Debug the coordinates first
                DebugCoordinateSystem(space);

                // Test different approaches
                // TestDifferentClippingApproaches(space);

                // Use the full section box approach
                SetBoundingBoxAsSectionBox(bounds);

                // Zoom to fit the section box
                ZoomToSectionBox(bounds);

                StatusText.Text = $"Room '{space.Name}' - Section Box Active";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error creating section box: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Section box error: {ex.Message}");
            }
        }
        private void SetBoundingBoxAsSectionBox(XbimRect3D bounds)
        {
            try
            {
                // Clear any existing clipping
                ClearSectionBox();

                // Calculate the center of the bounding box
                double centerX = bounds.X + bounds.SizeX / 2;
                double centerY = bounds.Y + bounds.SizeY / 2;
                double centerZ = bounds.Z + bounds.SizeZ / 2;

                // Option 1: Clip from the front (most common view)
                // This will clip everything behind the front face of the box
                SetSingleClippingPlane(
                    centerX, bounds.Y, centerZ,    // Point on the front face
                    0, 1, 0                        // Normal pointing into the box (positive Y)
                );

                // Option 2: If you want to clip from a specific direction based on current view
                // You could determine the best clipping direction based on camera position

                System.Diagnostics.Debug.WriteLine($"Single plane section box: " +
                    $"Center({centerX:F2}, {centerY:F2}, {centerZ:F2}) " +
                    $"Size({bounds.SizeX:F2}, {bounds.SizeY:F2}, {bounds.SizeZ:F2})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting bounding box as section box: {ex.Message}");
            }
        }
        private void DebugCoordinateSystem(IIfcSpace space)
        {
            try
            {
                var bounds = GetPreciseRoomBounds(space);
                if (!bounds.HasValue) return;

                var b = bounds.Value;

                System.Diagnostics.Debug.WriteLine("=== ROOM BOUNDS DEBUG ===");
                System.Diagnostics.Debug.WriteLine($"Room: {space.Name}");
                System.Diagnostics.Debug.WriteLine($"Min: X={b.X:F2}, Y={b.Y:F2}, Z={b.Z:F2}");
                System.Diagnostics.Debug.WriteLine($"Max: X={b.X + b.SizeX:F2}, Y={b.Y + b.SizeY:F2}, Z={b.Z + b.SizeZ:F2}");
                System.Diagnostics.Debug.WriteLine($"Size: X={b.SizeX:F2}, Y={b.SizeY:F2}, Z={b.SizeZ:F2}");

                // Test different clipping plane positions
                System.Diagnostics.Debug.WriteLine("=== CLIPPING PLANE POSITIONS ===");
                System.Diagnostics.Debug.WriteLine($"Front: ({b.X}, {b.Y}, {b.Z}) with normal (0, 1, 0)");
                System.Diagnostics.Debug.WriteLine($"Back: ({b.X}, {b.Y + b.SizeY}, {b.Z}) with normal (0, -1, 0)");
                System.Diagnostics.Debug.WriteLine($"Left: ({b.X}, {b.Y}, {b.Z}) with normal (1, 0, 0)");
                System.Diagnostics.Debug.WriteLine($"Right: ({b.X + b.SizeX}, {b.Y}, {b.Z}) with normal (-1, 0, 0)");
                System.Diagnostics.Debug.WriteLine($"Bottom: ({b.X}, {b.Y}, {b.Z}) with normal (0, 0, 1)");
                System.Diagnostics.Debug.WriteLine($"Top: ({b.X}, {b.Y}, {b.Z + b.SizeZ}) with normal (0, 0, -1)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Debug coordinate system error: {ex.Message}");
            }
        }

        private void TestDifferentClippingApproaches(IIfcSpace space)
        {
            var bounds = GetPreciseRoomBounds(space);
            if (!bounds.HasValue) return;

            var b = bounds.Value;

            // Test 1: Front clipping
            ClearSectionBox();
            SetSingleClippingPlane(b.X, b.Y, b.Z, 0, 1, 0);
            StatusText.Text = "Testing: Front clipping (Y min)";

            // Wait a bit then test next approach
            Task.Delay(3000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Test 2: Back clipping  
                    ClearSectionBox();
                    SetSingleClippingPlane(b.X, b.Y + b.SizeY, b.Z, 0, -1, 0);
                    StatusText.Text = "Testing: Back clipping (Y max)";
                });

                Task.Delay(3000).ContinueWith(__ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Test 3: Multiple planes
                        ClearSectionBox();
                        SetBoundingBoxAsSectionBox(b);
                        StatusText.Text = "Testing: Full section box (6 planes)";
                    });
                });
            });
        }

        private void ZoomToSectionBox(XbimRect3D bounds)
        {
            try
            {
                // Add some padding for better visualization
                var paddedBounds = new XbimRect3D(
                    bounds.X - 1.0, bounds.Y - 1.0, bounds.Z - 0.5,
                    bounds.SizeX + 2.0, bounds.SizeY + 2.0, bounds.SizeZ + 1.0
                );

                // Use the existing ZoomToBounds method
                ZoomToBounds(paddedBounds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error zooming to section box: {ex.Message}");
            }
        }

        #endregion
    }
}