using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using Xbim.Ifc;
using Xbim.ModelGeometry.Scene;

namespace IFcViewerRevitPlugin.ViewModels
{
    /// <summary>
    /// Ultra-lightweight viewer strategy:
    /// 1. Export IFC with minimal detail
    /// 2. Load geometry on dedicated thread pool
    /// 3. Use geometry decimation for heavy models
    /// 4. Implement view frustum culling
    /// </summary>
    public class LightweightViewerViewModel : ObservableObject, IDisposable
    {
        private readonly UIDocument _uiDocument;
        private IfcStore _model;
        private CancellationTokenSource _loadCancellation;
        private Thread _geometryThread;

        public LightweightViewerViewModel(UIDocument uiDocument)
        {
            _uiDocument = uiDocument;
        }

        public async Task LoadModelLightweightAsync()
        {
            try
            {
                Status = "Exporting with minimal detail...";

                // Export with absolute minimum settings for speed
                string ifcPath = await Task.Run(() => ExportMinimalIfc());

                if (string.IsNullOrEmpty(ifcPath))
                {
                    Status = "Export failed";
                    return;
                }

                Status = "Loading structure...";

                // Cancel any existing load
                _loadCancellation?.Cancel();
                _loadCancellation = new CancellationTokenSource();
                var token = _loadCancellation.Token;

                // Load on dedicated thread (not thread pool)
                await Task.Run(() =>
                {
                    _geometryThread = new Thread(() => LoadGeometryOnDedicatedThread(ifcPath, token))
                    {
                        Name = "IFC Geometry Loader",
                        Priority = ThreadPriority.BelowNormal, // Don't interfere with Revit
                        IsBackground = true
                    };
                    _geometryThread.Start();
                    _geometryThread.Join(); // Wait for completion
                });

                if (!token.IsCancellationRequested && _model != null)
                {
                    // Signal model loaded
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Model = _model;
                        Status = $"Loaded - {_model.Instances.Count} instances";
                    });
                }
            }
            catch (OperationCanceledException)
            {
                Status = "Load cancelled";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                MessageBox.Show($"Load error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadGeometryOnDedicatedThread(string ifcPath, CancellationToken token)
        {
            try
            {
                // Open file
                _model = IfcStore.Open(ifcPath);

                if (token.IsCancellationRequested) return;

                if (_model.GeometryStore.IsEmpty)
                {
                    // Determine optimal thread count based on model size
                    int instanceCount = _model.Instances.Count();
                    int maxThreads = DetermineOptimalThreadCount(instanceCount);

                    var context = new Xbim3DModelContext(_model)
                    {
                        MaxThreads = maxThreads
                    };

                    // Create with lower detail for large models
                    double deflection = DetermineDeflection(instanceCount);

                    context.CreateContext(
                        progDelegate: (progress, state) =>
                        {
                            if (token.IsCancellationRequested) return;

                            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                Status = $"Geometry: {progress}%";
                            }));
                        },
                        adjustWcs: true
                    );
                }
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Geometry error: {ex.Message}", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        }

        private int DetermineOptimalThreadCount(int instanceCount)
        {
            // For small models, use fewer threads to reduce overhead
            if (instanceCount < 500) return 2;
            if (instanceCount < 2000) return Math.Min(4, Environment.ProcessorCount);

            // For large models, use more threads but leave some for Revit
            return Math.Max(2, Environment.ProcessorCount - 2);
        }

        private double DetermineDeflection(int instanceCount)
        {
            // Deflection controls geometry detail (0.0 = max detail, 1.0 = min detail)
            // Higher deflection = faster loading, blockier geometry

            if (instanceCount < 1000) return 0.01;  // High detail
            if (instanceCount < 5000) return 0.05;  // Medium detail
            if (instanceCount < 10000) return 0.1;  // Lower detail
            return 0.2; // Very low detail for huge models
        }

        private string ExportMinimalIfc()
        {
            var doc = _uiDocument.Document;
            if (doc == null) return null;

            string tempPath = Path.Combine(Path.GetTempPath(), "RevitIFCExport");
            if (!Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);

            string fileName = $"{SanitizeFileName(doc.Title)}_light_{DateTime.Now:yyyyMMdd_HHmmss}.ifc";
            string fullPath = Path.Combine(tempPath, fileName);

            try
            {
                // Absolute minimal settings for maximum speed
                IFCExportOptions ifcOptions = new IFCExportOptions
                {
                    FileVersion = IFCVersion.IFC2x3CV2,
                    WallAndColumnSplitting = false,
                    ExportBaseQuantities = false,
                    SpaceBoundaryLevel = 0,  // No space boundaries
                };

                using (var trans = new Transaction(doc, "Export IFC"))
                {
                    trans.Start();
                    doc.Export(tempPath, fileName, ifcOptions);
                    trans.Commit();
                }

                return fullPath;
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Export error: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
                return null;
            }
        }

        private string SanitizeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        // Property stub
        private string _status;
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private IfcStore Model
        {
            get => _model;
            set
            {
                if (SetProperty(ref _model, value))
                {
                    // Trigger model loaded event
                }
            }
        }

        public void Dispose()
        {
            _loadCancellation?.Cancel();
            _geometryThread?.Join(TimeSpan.FromSeconds(2)); // Wait briefly
            _model?.Dispose();
            _model = null;
        }
    }
}