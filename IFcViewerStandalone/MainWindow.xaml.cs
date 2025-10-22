using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using Xbim.Ifc;
using Xbim.ModelGeometry.Scene;
using Xbim.Presentation;
using Xbim.Ifc4.Interfaces;
using Xbim.Common.Geometry;
using System.Windows.Controls;

namespace IFcViewerStandalone
{
    /// <summary>
    /// Standalone viewer window - runs in separate process from Revit
    /// Much better performance for heavy models
    /// </summary>
    public partial class MainWindow : Window
    {
        private IfcStore _model;
        private string _ifcPath;
        private bool _isLoading;

        public MainWindow(string ifcPath)
        {
            InitializeComponent();
            _ifcPath = ifcPath;

            // Auto-load if path provided
            if (!string.IsNullOrEmpty(ifcPath) && File.Exists(ifcPath))
            {
                Loaded += async (s, e) => await LoadModelAsync(ifcPath);
            }
        }

        private async void LoadModel_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "IFC Files (*.ifc)|*.ifc|All Files (*.*)|*.*",
                Title = "Select IFC File"
            };

            if (dialog.ShowDialog() == true)
            {
                await LoadModelAsync(dialog.FileName);
            }
        }

        private async Task LoadModelAsync(string filePath)
        {
            if (_isLoading) return;
            _isLoading = true;

            try
            {
                StatusText.Text = "Loading IFC file...";
                LoadButton.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = true;

                // Dispose previous model
                _model?.Dispose();

                // Load on background thread
                await Task.Run(() =>
                {
                    _model = IfcStore.Open(filePath);

                    if (_model.GeometryStore.IsEmpty)
                    {
                        // Use ALL available threads for maximum performance
                        var context = new Xbim3DModelContext(_model)
                        {
                            MaxThreads = Environment.ProcessorCount // Use all cores!
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
                                    ProgressBar.Value = progress;
                                    StatusText.Text = $"Creating geometry: {progress}%";
                                }));
                            },
                            adjustWcs: true
                        );
                    }
                });

                // Set model on UI thread
                Viewer.Model = _model;

                // Populate levels
                await PopulateLevelsAsync();

                // Zoom to model
                Viewer.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => Viewer.ViewHome()));

                StatusText.Text = $"Loaded: {Path.GetFileName(filePath)} ({_model.Instances.Count} instances)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading model: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error loading model";
            }
            finally
            {
                _isLoading = false;
                LoadButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task PopulateLevelsAsync()
        {
            if (_model == null) return;

            await Task.Run(() =>
            {
                var storeys = _model.Instances.OfType<IIfcBuildingStorey>().ToList();

                Dispatcher.Invoke(() =>
                {
                    LevelCombo.Items.Clear();
                    LevelCombo.Items.Add("-- All Levels --");

                    foreach (var storey in storeys)
                    {
                        string name = storey.Name ?? storey.LongName ?? $"Level_{storey.EntityLabel}";
                        LevelCombo.Items.Add(name);
                    }

                    LevelCombo.SelectedIndex = 0;
                });
            });
        }

        private void LevelCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LevelCombo.SelectedIndex <= 0 || _model == null)
            {
                // Show all
                Viewer.HiddenInstances = null;
                Viewer.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);
                StatusText.Text = "Showing all levels";
                return;
            }

            Task.Run(() => FilterByLevel(LevelCombo.SelectedItem.ToString()));
        }

        private void FilterByLevel(string levelName)
        {
            try
            {
                var storey = _model.Instances.OfType<IIfcBuildingStorey>()
                    .FirstOrDefault(s => s.Name == levelName || s.LongName == levelName);

                if (storey == null) return;

                // Get all products on this level
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

                // Get all products to hide
                var allProducts = _model.Instances.OfType<IIfcProduct>().ToList();
                var hiddenList = allProducts
                    .Where(p => !visibleLabels.Contains(p.EntityLabel))
                    .Cast<Xbim.Common.IPersistEntity>()
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    Viewer.HiddenInstances = hiddenList;
                    Viewer.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);
                    StatusText.Text = $"Showing level: {levelName} ({visibleLabels.Count} elements)";
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

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            if (_model == null) return;

            Viewer.HiddenInstances = null;
            Viewer.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);
            LevelCombo.SelectedIndex = 0;
            Viewer.ViewHome();
            StatusText.Text = "View reset";
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Cleanup
            Viewer.Model = null;
            _model?.Dispose();
            _model = null;

            // Aggressive garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
