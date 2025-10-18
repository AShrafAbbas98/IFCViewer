using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IFcViewerRevitPlugin.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;

namespace IFcViewerRevitPlugin.ViewModels
{
    public partial class ViewerViewModel : ObservableObject
    {
        private readonly IfcModelWrapper _ifcModel = new IfcModelWrapper();
        private readonly UIDocument _uiDocument;

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
            set
            {
                // Only call SetProperty if both _model and value are not null,
                // or if one of them is null (to allow proper change notification)
                bool changed;
                if (_model == null || value == null)
                {
                    changed = SetProperty(ref _model, value);
                }
                else
                {
                    changed = SetProperty(ref _model, value);
                }

                if (changed && value != null)
                {
                    // Notify that model has changed
                    ModelLoaded?.Invoke(this, EventArgs.Empty);
                }
            }
        }


        // Event to notify when model is loaded
        public event EventHandler ModelLoaded;

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

        // Commands
        public IRelayCommand LoadIfcCommand { get; }
        public IRelayCommand<string> FilterByLevelCommand { get; }
        public IRelayCommand<string> FilterByRoomCommand { get; }
        public IRelayCommand ResetViewCommand { get; }

        public ViewerViewModel(UIDocument uiDoc)
        {
            _uiDocument = uiDoc;
            LoadIfcCommand = new RelayCommand(LoadIfc);
            FilterByLevelCommand = new RelayCommand<string>(FilterByLevel);
            FilterByRoomCommand = new RelayCommand<string>(FilterByRoom);
            ResetViewCommand = new RelayCommand(ResetView);
        }

        private void LoadIfc()
        {
            try
            {
                Status = "Exporting to IFC...";
                string ifcPath = ExportRevitToIfc();

                if (string.IsNullOrEmpty(ifcPath) || !File.Exists(ifcPath))
                {
                    Status = "Export failed";
                    return;
                }

                Status = "Loading IFC model...";
                _ifcModel.LoadIfcFile(ifcPath);

                if (_ifcModel.Model == null)
                {
                    Status = "Failed to load model";
                    return;
                }

                // CRITICAL: Create geometry context for 3D visualization
                Status = "Creating 3D geometry...";
                if (_ifcModel.Model.GeometryStore.IsEmpty)
                {
                    try
                    {
                        var context = new Xbim3DModelContext(_ifcModel.Model);
                        // Use single thread for stability in Revit plugin
                        context.MaxThreads = 1;

                        // Create the geometry context
                        context.CreateContext(null, true);

                        Status = "Geometry created successfully";
                    }
                    catch (Exception geomEx)
                    {
                        Status = $"Geometry creation failed: {geomEx.Message}";
                        MessageBox.Show($"Error creating geometry: {geomEx.Message}\n\nThe model may not display correctly.",
                            "Geometry Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                // Now set the model which will trigger the ModelLoaded event
                Model = _ifcModel.Model;

                PopulateLevelsFromIfc();
                Status = $"Model loaded successfully - {Model.Instances.Count} entities";
            }
            catch (Exception ex)
            {
                Status = "Error loading model";
                MessageBox.Show($"Error: {ex.Message}\n\nStack Trace: {ex.StackTrace}",
                    "IFC Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ExportRevitToIfc()
        {
            Document doc = _uiDocument.Document;

            if (doc == null)
            {
                MessageBox.Show("No active document found.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            string tempPath = Path.Combine(Path.GetTempPath(), "RevitIFCExport");

            if (!Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);

            string fileName = $"{doc.Title}_{DateTime.Now:yyyyMMdd_HHmmss}.ifc";
            string fullPath = Path.Combine(tempPath, fileName);

            try
            {
                // Create IFC export options
                IFCExportOptions ifcOptions = new IFCExportOptions
                {
                    FileVersion = IFCVersion.IFC2x3,  // Use IFC2x3 for better Xbim compatibility
                    WallAndColumnSplitting = true,
                    ExportBaseQuantities = true,
                    SpaceBoundaryLevel = 1,  // Important for room boundaries
                };

                using (Transaction trans = new Transaction(doc, "Export IFC"))
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
                MessageBox.Show($"Error exporting to IFC: {ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void PopulateLevelsFromIfc()
        {
            Levels.Clear();

            if (Model == null) return;

            try
            {
                // Get all building storeys (levels) from the IFC model
                var storeys = Model.Instances.OfType<IIfcBuildingStorey>();

                foreach (var storey in storeys)
                {
                    string levelName = storey.Name ?? storey.LongName ?? "Unnamed Level";
                    Levels.Add(levelName);
                }

                if (Levels.Count == 0)
                {
                    Status = "No levels found in model";
                }
            }
            catch (Exception ex)
            {
                Status = $"Error loading levels: {ex.Message}";
            }
        }

        public void FilterByLevel(string level)
        {
            if (string.IsNullOrEmpty(level) || Model == null) return;

            Status = $"Filtering by level: {level}";
            Rooms.Clear();

            try
            {
                // Find the selected storey
                var storey = Model.Instances.OfType<IIfcBuildingStorey>()
                    .FirstOrDefault(s => s.Name == level || s.LongName == level);

                if (storey != null)
                {
                    // Get all spaces (rooms) in this storey
                    var spaces = Model.Instances.OfType<IIfcSpace>()
                        .Where(s => s.Decomposes.Any(rel => rel.RelatingObject == storey));

                    foreach (var space in spaces)
                    {
                        string roomName = space.Name ?? space.LongName ?? "Unnamed Room";
                        Rooms.Add(roomName);
                    }
                }

                Status = $"Level selected: {level} - {Rooms.Count} rooms found";
            }
            catch (Exception ex)
            {
                Status = $"Error filtering by level: {ex.Message}";
            }
        }

        public void FilterByRoom(string room)
        {
            if (string.IsNullOrEmpty(room)) return;
            Status = $"Room selected: {room}";

            // Here you could implement room highlighting or isolation
            // This would require manipulating the 3D view
        }

        public void ResetView()
        {
            Status = "Resetting view...";
            Rooms.Clear();
            SelectedLevel = null;
            SelectedRoom = null;
            Status = "View reset";
        }

        public void Cleanup()
        {
            _ifcModel?.Dispose();
            Model = null;
        }
    }
}