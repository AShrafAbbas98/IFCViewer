using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using IFcViewerRevitPlugin.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Xbim.Common.Geometry;
using Xbim.Presentation;

namespace IFcViewerRevitPlugin.Views
{
    /// <summary>
    /// Refactored ViewerWindow following MVVM pattern
    /// Code-behind contains ONLY view-specific logic
    /// All business logic is in the ViewModel
    /// </summary>
    public partial class ViewerWindowRefactored : Window
    {
        private readonly ViewerViewModelRefactored _viewModel;
        private ExternalEvent _externalEvent;
        private ViewerExternalEventHandlerRefactored _eventHandler;

        public ViewerWindowRefactored(Document document)
        {
            InitializeComponent();

            _viewModel = new ViewerViewModelRefactored(document.Application.ActiveUIDocument);
            DataContext = _viewModel;

            SubscribeToViewModelEvents();
            InitializeDrawingControl();

            Closing += OnWindowClosing;
        }

        #region Public Methods

        public void SetExternalEvent(ExternalEvent externalEvent, ViewerExternalEventHandlerRefactored eventHandler)
        {
            _externalEvent = externalEvent;
            _eventHandler = eventHandler;
        }

        #endregion

        #region Initialization

        private void SubscribeToViewModelEvents()
        {
            _viewModel.ModelLoaded += OnModelLoaded;
            _viewModel.ApplySectionBox += OnApplySectionBox;
            _viewModel.ElementsFilterChanged += OnElementsFilterChanged;
        }

        private void InitializeDrawingControl()
        {
            try
            {
                DrawingControl.Loaded += DrawingControl_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error initializing 3D control: {ex.Message}",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Event Handlers - Drawing Control

        private void DrawingControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfigureDrawingControl();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error configuring 3D control: {ex.Message}",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ConfigureDrawingControl()
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

        #endregion

        #region Event Handlers - ViewModel

        private void OnModelLoaded(object sender, EventArgs e)
        {
            DrawingControl.Model = _viewModel.Model;
            ZoomToFit();
        }

        private void OnApplySectionBox(object sender, XbimRect3D bbox)
        {
            // Apply section box visualization if needed
            // This could be implemented to show a visual bounding box
        }

        private void OnElementsFilterChanged(object sender, IEnumerable<int> visibleLabels)
        {
            ApplyElementFilter(visibleLabels);
        }

        #endregion

        #region Private Methods - Filtering

        private void ApplyElementFilter(IEnumerable<int> visibleLabels)
        {
            if (_viewModel.Model == null)
            {
                return;
            }

            try
            {
                if (visibleLabels == null)
                {
                    // Show all elements
                    DrawingControl.HiddenInstances = null;
                }
                else
                {
                    // Hide elements not in the visible list
                    var allProducts = _viewModel.Model.Instances.OfType<Xbim.Ifc4.Interfaces.IIfcProduct>();
                    var hiddenElements = allProducts
                        .Where(p => !visibleLabels.Contains(p.EntityLabel))
                        .Cast<Xbim.Common.IPersistEntity>()
                        .ToList();

                    DrawingControl.HiddenInstances = hiddenElements;
                }

                ReloadModel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Filter error: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods - View Operations

        private void ZoomToFit()
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    try
                    {
                        DrawingControl.ViewHome();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ViewHome error: {ex.Message}");
                    }
                }));
        }

        private void ReloadModel()
        {
            DrawingControl.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);
        }

        #endregion

        #region Cleanup

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UnsubscribeFromViewModelEvents();
            CleanupDrawingControl();
            CleanupViewModel();
            ForceGarbageCollection();
        }

        private void UnsubscribeFromViewModelEvents()
        {
            _viewModel.ModelLoaded -= OnModelLoaded;
            _viewModel.ApplySectionBox -= OnApplySectionBox;
            _viewModel.ElementsFilterChanged -= OnElementsFilterChanged;
        }

        private void CleanupDrawingControl()
        {
            try
            {
                DrawingControl.Model = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
        }

        private void CleanupViewModel()
        {
            _viewModel?.Dispose();
        }

        private void ForceGarbageCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        #endregion
    }
}
