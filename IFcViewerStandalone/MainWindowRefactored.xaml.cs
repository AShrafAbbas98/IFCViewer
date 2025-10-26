using IFcViewerStandalone.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Xbim.Presentation;

namespace IFcViewerStandalone
{
    /// <summary>
    /// Refactored MainWindow - follows MVVM pattern strictly
    /// Code-behind contains ONLY view-specific logic
    /// All business logic is in the ViewModel
    /// </summary>
    public partial class MainWindowRefactored : Window
    {
        private readonly MainViewModelRefactored _viewModel;

        public MainWindowRefactored(string ifcPath = null)
        {
            InitializeComponent();

            _viewModel = new MainViewModelRefactored();
            DataContext = _viewModel;

            SubscribeToViewModelEvents();
            InitializeViewer();

            // Auto-load if path provided
            if (!string.IsNullOrEmpty(ifcPath))
            {
                Loaded += async (s, e) => await _viewModel.LoadModelFromFileAsync(ifcPath);
            }
        }

        #region Initialization

        private void SubscribeToViewModelEvents()
        {
            _viewModel.ModelLoaded += OnModelLoaded;
            _viewModel.HiddenInstancesChanged += OnHiddenInstancesChanged;
            _viewModel.ViewHomeRequested += OnViewHomeRequested;
        }

        private void InitializeViewer()
        {
            // Viewer initialization can stay in code-behind as it's view-specific
            try
            {
                Viewer.ShowGridLines = false;
                Viewer.ModelOpacity = 1.0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error initializing viewer: {ex.Message}",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Event Handlers - ViewModel

        private void OnModelLoaded(object sender, EventArgs e)
        {
            // Set the model on the viewer control
            Viewer.Model = _viewModel.Model;
        }

        private void OnHiddenInstancesChanged(object sender, IEnumerable<Xbim.Common.IPersistEntity> hiddenInstances)
        {
            Viewer.HiddenInstances = hiddenInstances?.ToList();
            Viewer.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);
        }

        private void OnViewHomeRequested(object sender, EventArgs e)
        {
            Viewer.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => Viewer.ViewHome()));
        }

        #endregion

        #region Cleanup

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            UnsubscribeFromViewModelEvents();
            CleanupViewer();
            CleanupViewModel();
            ForceGarbageCollection();
        }

        private void UnsubscribeFromViewModelEvents()
        {
            _viewModel.ModelLoaded -= OnModelLoaded;
            _viewModel.HiddenInstancesChanged -= OnHiddenInstancesChanged;
            _viewModel.ViewHomeRequested -= OnViewHomeRequested;
        }

        private void CleanupViewer()
        {
            Viewer.Model = null;
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
