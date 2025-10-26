using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IFcViewerRevitPlugin.Constants;
using IFcViewerRevitPlugin.DTOs;
using IFcViewerRevitPlugin.Infrastructure;
using IFcViewerRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IFcViewerRevitPlugin.ViewModels
{
    /// <summary>
    /// Refactored ViewerViewModel following MVVM and Clean Code principles
    /// Uses dependency injection and service layer for business logic
    /// </summary>
    public class ViewerViewModelRefactored : ObservableObject, IDisposable
    {
        #region Fields

        private readonly UIDocument _uiDocument;
        private readonly IIfcExportService _exportService;
        private readonly IIfcLoadingService _loadingService;
        private readonly IFilteringService _filteringService;
        private readonly IBoundingBoxService _boundingBoxService;
        private readonly IPerformanceOptimizationService _performanceService;

        private IfcStore _model;
        private bool _isLoading;

        #endregion

        #region Constructor

        public ViewerViewModelRefactored(UIDocument uiDocument)
        {
            _uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));

            // Resolve services from container
            var container = ServiceContainer.Instance;
            _exportService = container.Resolve<IIfcExportService>();
            _loadingService = container.Resolve<IIfcLoadingService>();
            _filteringService = container.Resolve<IFilteringService>();
            _boundingBoxService = container.Resolve<IBoundingBoxService>();
            _performanceService = container.Resolve<IPerformanceOptimizationService>();

            InitializeCommands();
            Status = UiConstants.StatusReady;
        }

        private void InitializeCommands()
        {
            LoadIfcCommand = new RelayCommand(async () => await LoadIfcAsync(), () => !_isLoading);
            FilterByLevelCommand = new RelayCommand<string>(async level => await FilterByLevelAsync(level));
            FilterByRoomCommand = new RelayCommand<string>(async room => await FilterByRoomAsync(room));
            ResetViewCommand = new RelayCommand(ResetView);
        }

        #endregion

        #region Events

        public event EventHandler ModelLoaded;
        public event EventHandler<XbimRect3D> ApplySectionBox;
        public event EventHandler<IEnumerable<int>> ElementsFilterChanged;

        #endregion

        #region Properties

        private string _status;
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public IfcStore Model
        {
            get => _model;
            private set
            {
                if (SetProperty(ref _model, value) && value != null)
                {
                    OnModelLoaded();
                }
            }
        }

        private string _selectedLevel;
        public string SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                if (SetProperty(ref _selectedLevel, value) && !string.IsNullOrEmpty(value))
                {
                    _ = FilterByLevelAsync(value);
                }
            }
        }

        private string _selectedRoom;
        public string SelectedRoom
        {
            get => _selectedRoom;
            set
            {
                if (SetProperty(ref _selectedRoom, value) && !string.IsNullOrEmpty(value))
                {
                    _ = FilterByRoomAsync(value);
                }
            }
        }

        public ObservableCollection<string> Levels { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Rooms { get; } = new ObservableCollection<string>();

        #endregion

        #region Commands

        public IRelayCommand LoadIfcCommand { get; private set; }
        public IRelayCommand<string> FilterByLevelCommand { get; private set; }
        public IRelayCommand<string> FilterByRoomCommand { get; private set; }
        public IRelayCommand ResetViewCommand { get; private set; }

        #endregion

        #region Public Methods

        public async Task LoadIfcAsync()
        {
            if (_isLoading) return;

            SetLoadingState(true);

            try
            {
                var ifcPath = await ExportRevitDocumentAsync();

                if (string.IsNullOrEmpty(ifcPath))
                {
                    Status = UiConstants.StatusExportFailed;
                    return;
                }

                await LoadModelFromFileAsync(ifcPath);
                await PopulateLevelsAsync();

                UpdateStatusWithModelInfo();
            }
            catch (Exception ex)
            {
                HandleLoadError(ex);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        public void ResetView()
        {
            Status = "Resetting view...";
            ClearCollections();
            ClearSelection();
            RaiseResetEvents();
            Status = "View reset";
        }

        public void Dispose()
        {
            Model?.Dispose();
            Model = null;
        }

        #endregion

        #region Private Methods - Export

        private async Task<string> ExportRevitDocumentAsync()
        {
            Status = UiConstants.StatusExporting;

            try
            {
                return await _exportService.ExportToIfcAsync(
                    _uiDocument.Document,
                    ExportOptions.CreateDefault());
            }
            catch (Exception ex)
            {
                ShowErrorDialog($"Error exporting to IFC: {ex.Message}", UiConstants.ExportErrorDialogTitle);
                return null;
            }
        }

        #endregion

        #region Private Methods - Loading

        private async Task LoadModelFromFileAsync(string ifcPath)
        {
            Status = UiConstants.StatusLoadingModel;

            var loadOptions = new ModelLoadOptions
            {
                FilePath = ifcPath,
                OptimizeForPerformance = true,
                AdjustWorldCoordinateSystem = true
            };

            var loadedModel = await _loadingService.LoadIfcFileAsync(
                ifcPath,
                loadOptions,
                progress => UpdateGeometryProgress(progress));

            Model = loadedModel;
        }

        private void UpdateGeometryProgress(int progress)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                Status = string.Format(UiConstants.StatusGeometryProgress, progress);
            }));
        }

        #endregion

        #region Private Methods - Levels and Rooms

        private async Task PopulateLevelsAsync()
        {
            await Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                ClearCollections();
            });

            if (Model == null) return;

            await Task.Run(() =>
            {
                var storeys = _filteringService.GetBuildingStoreys(Model);

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var storey in storeys)
                    {
                        string name = GetDisplayNameForStorey(storey);
                        Levels.Add(name);
                    }

                    if (Levels.Count == 0)
                    {
                        Status = UiConstants.StatusNoLevelsFound;
                    }
                });
            });
        }

        private string GetDisplayNameForStorey(IIfcBuildingStorey storey)
        {
            return storey.Name
                ?? storey.LongName
                ?? string.Format(UiConstants.DefaultLevelNameFormat, storey.EntityLabel);
        }

        private string GetDisplayNameForSpace(IIfcSpace space)
        {
            return space.Name
                ?? space.LongName
                ?? string.Format(UiConstants.DefaultSpaceNameFormat, space.EntityLabel);
        }

        #endregion

        #region Private Methods - Filtering

        private async Task FilterByLevelAsync(string levelName)
        {
            if (string.IsNullOrEmpty(levelName) || Model == null) return;

            Status = string.Format(UiConstants.StatusFilteringByLevel, levelName);
            Rooms.Clear();

            await Task.Run(() =>
            {
                var storey = FindStoreyByName(levelName);

                if (storey == null)
                {
                    SetStatus(string.Format(UiConstants.StatusLevelNotFound, levelName));
                    return;
                }

                var spaces = _filteringService.GetSpacesInStorey(storey, Model);
                PopulateRoomsFromSpaces(spaces);

                var bbox = _boundingBoxService.CalculateBoundingBoxForStorey(storey, Model);
                if (bbox != null)
                {
                    RaiseSectionBoxEvent(bbox);
                }

                var labels = _filteringService.GetEntityLabelsForStorey(storey, Model);
                RaiseFilterChangedEvent(labels);

                UpdateLevelFilterStatus(levelName, labels.Count);
            });

            SelectedLevel = levelName;
            SelectedRoom = null;
        }

        private async Task FilterByRoomAsync(string roomName)
        {
            if (string.IsNullOrEmpty(roomName) || Model == null) return;

            await Task.Run(() =>
            {
                var space = FindSpaceByName(roomName);

                if (space == null)
                {
                    SetStatus(string.Format(UiConstants.StatusRoomNotFound, roomName));
                    return;
                }

                var bbox = _boundingBoxService.CalculateBoundingBoxForSpace(space, Model);
                if (bbox != null)
                {
                    RaiseSectionBoxEvent(bbox);
                }

                var labels = _filteringService.GetEntityLabelsForSpace(space, Model);

                if (labels.Count < 2 && bbox != null)
                {
                    labels = _filteringService.GetEntityLabelsIntersectingBbox(bbox, Model);
                    SetStatus($"Room '{roomName}': Using spatial filter");
                }
                else
                {
                    SetStatus($"Room '{roomName}': {labels.Count} elements");
                }

                RaiseFilterChangedEvent(labels);
            });

            SelectedRoom = roomName;
        }

        #endregion

        #region Private Methods - Helpers

        private IIfcBuildingStorey FindStoreyByName(string name)
        {
            return Model.Instances.OfType<IIfcBuildingStorey>()
                .FirstOrDefault(s => s.Name == name || s.LongName == name);
        }

        private IIfcSpace FindSpaceByName(string name)
        {
            return Model.Instances.OfType<IIfcSpace>()
                .FirstOrDefault(s => s.Name == name || s.LongName == name);
        }

        private void PopulateRoomsFromSpaces(List<IIfcSpace> spaces)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var space in spaces)
                {
                    string roomName = GetDisplayNameForSpace(space);
                    Rooms.Add(roomName);
                }
            });
        }

        private void ClearCollections()
        {
            Levels.Clear();
            Rooms.Clear();
        }

        private void ClearSelection()
        {
            SelectedLevel = null;
            SelectedRoom = null;
        }

        private void SetLoadingState(bool isLoading)
        {
            _isLoading = isLoading;
            ((RelayCommand)LoadIfcCommand).NotifyCanExecuteChanged();
        }

        private void UpdateStatusWithModelInfo()
        {
            if (Model != null)
            {
                Status = string.Format(UiConstants.StatusModelLoaded, Model.Instances.Count);
            }
        }

        private void UpdateLevelFilterStatus(string levelName, int elementCount)
        {
            SetStatus($"Level '{levelName}': {elementCount} elements");
        }

        private void SetStatus(string status)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Status = status;
            });
        }

        #endregion

        #region Private Methods - Events

        private void OnModelLoaded()
        {
            ModelLoaded?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseSectionBoxEvent(BoundingBoxData bbox)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ApplySectionBox?.Invoke(this, bbox.ToXbimRect3D());
            });
        }

        private void RaiseFilterChangedEvent(HashSet<int> labels)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ElementsFilterChanged?.Invoke(this, labels);
            });
        }

        private void RaiseResetEvents()
        {
            ApplySectionBox?.Invoke(this, new XbimRect3D());
            ElementsFilterChanged?.Invoke(this, null);
        }

        #endregion

        #region Private Methods - Error Handling

        private void HandleLoadError(Exception ex)
        {
            Status = UiConstants.StatusErrorLoadingModel;
            ShowErrorDialog(
                $"Error: {ex.Message}\n\nStack Trace: {ex.StackTrace}",
                UiConstants.IFCLoadErrorDialogTitle);
        }

        private void ShowErrorDialog(string message, string title)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        #endregion
    }
}
