using System;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using IFcViewerRevitPlugin.ViewModels;

namespace IFcViewerRevitPlugin.Views
{
    public partial class ViewerWindow : Window
    {
        private readonly ViewerViewModel _viewModel;

        public ViewerWindow(UIDocument uiDoc)
        {
            InitializeComponent();

            _viewModel = new ViewerViewModel(uiDoc);
            DataContext = _viewModel;

            // Subscribe to model loaded event
            _viewModel.ModelLoaded += ViewModel_ModelLoaded;

            // Handle window closing to cleanup resources
            Closing += ViewerWindow_Closing;

            // Set up the drawing control
            DrawingControl.Loaded += DrawingControl_Loaded;
        }

        private void DrawingControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Configure the 3D control settings
                DrawingControl.ShowGridLines = true;  // Show grid for debugging
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing 3D control: {ex.Message}",
                    "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ViewModel_ModelLoaded(object sender, EventArgs e)
        {
            // Use dispatcher to ensure UI thread execution
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_viewModel.Model != null)
                    {
                        // Set the model directly
                        DrawingControl.Model = _viewModel.Model;

                        // Force a refresh and zoom
                        DrawingControl.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Loaded,
                            new Action(() =>
                            {
                                try
                                {
                                    // ViewHome should zoom to show the entire model
                                    DrawingControl.ViewHome();
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"Error zooming to model: {ex.Message}",
                                        "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                            })
                        );
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error displaying model: {ex.Message}\n\n{ex.StackTrace}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Levels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel != null && _viewModel.SelectedLevel != null)
            {
                _viewModel.FilterByLevelCommand.Execute(_viewModel.SelectedLevel);
            }
        }

        private void Rooms_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel != null && _viewModel.SelectedRoom != null)
            {
                _viewModel.FilterByRoomCommand.Execute(_viewModel.SelectedRoom);
            }
        }

        private void ViewerWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Cleanup resources
            _viewModel.ModelLoaded -= ViewModel_ModelLoaded;
            _viewModel?.Cleanup();
        }
    }
}