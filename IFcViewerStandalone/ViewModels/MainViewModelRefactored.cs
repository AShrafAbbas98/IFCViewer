using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;
using Xbim.Presentation;

namespace IFcViewerStandalone.ViewModels
{
    /// <summary>
    /// Refactored MainViewModel following MVVM and Clean Code principles
    /// Simplified version for standalone application
    /// </summary>
    public class MainViewModelRefactored : ObservableObject, IDisposable
    {
        #region Constants

        private const string StatusReady = "Ready";
        private const string StatusLoading = "Loading IFC file...";
        private const string StatusCreatingGeometry = "Creating geometry: {0}%";
        private const string StatusModelLoaded = "Loaded: {0} ({1} instances)";
        private const string StatusShowingAllLevels = "Showing all levels";
        private const string StatusShowingLevel = "Showing level: {0} ({1} elements)";
        private const string StatusViewReset = "View reset";
        private const string StatusError = "Error loading model";

        private const string AllLevelsItem = "-- All Levels --";
        private const string DefaultLevelNameFormat = "Level_{0}";

        #endregion

        #region Fields

        private IfcStore _model;
        private bool _isLoading;

        #endregion

        #region Constructor

        public MainViewModelRefactored()
        {
            InitializeCommands();
            Status = StatusReady;
        }

        private void InitializeCommands()
        {
            LoadModelCommand = new RelayCommand(async () => await LoadModelAsync(), () => !_isLoading);
            ResetViewCommand = new RelayCommand(ResetView);
        }

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
            private set => SetProperty(ref _model, value);
        }

        private string _selectedLevel;
        public string SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                if (SetProperty(ref _selectedLevel, value))
                {
                    OnLevelSelectionChanged();
                }
            }
        }

        public ObservableCollection<string> Levels { get; } = new ObservableCollection<string>();

        #endregion

        #region Commands

        public IRelayCommand LoadModelCommand { get; private set; }
        public IRelayCommand ResetViewCommand { get; private set; }

        #endregion

        #region Events

        public event EventHandler ModelLoaded;
        public event EventHandler<IEnumerable<Xbim.Common.IPersistEntity>> HiddenInstancesChanged;
        public event EventHandler ViewHomeRequested;

        #endregion

        #region Public Methods

        public async Task LoadModelAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "IFC Files (*.ifc)|*.ifc|All Files (*.*)|*.*",
                Title = "Select IFC File"
            };

            if (dialog.ShowDialog() != true) return;

            await LoadModelFromFileAsync(dialog.FileName);
        }

        public async Task LoadModelFromFileAsync(string filePath)
        {
            if (_isLoading) return;

            SetLoadingState(true);

            try
            {
                Status = StatusLoading;

                Model?.Dispose();

                await Task.Run(() => LoadIfcFileWithGeometry(filePath));

                await PopulateLevelsAsync();

                RaiseViewHomeRequested();

                UpdateStatusWithModelInfo(filePath);
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
            if (Model == null) return;

            RaiseHiddenInstancesChanged(null);
            SelectedLevel = AllLevelsItem;
            RaiseViewHomeRequested();
            Status = StatusViewReset;
        }

        public void Dispose()
        {
            Model?.Dispose();
            Model = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        #endregion

        #region Private Methods - Loading

        private void LoadIfcFileWithGeometry(string filePath)
        {
            Model = IfcStore.Open(filePath);

            if (Model.GeometryStore.IsEmpty)
            {
                CreateGeometry();
            }
        }

        private void CreateGeometry()
        {
            var context = new Xbim3DModelContext(Model)
            {
                MaxThreads = DetermineOptimalThreadCount()
            };

            context.CreateContext(
                progDelegate: OnGeometryProgress,
                adjustWcs: true
            );
        }

        private int DetermineOptimalThreadCount()
        {
            // Use all available cores for standalone application
            return Environment.ProcessorCount;
        }

        private void OnGeometryProgress(int progress, object state)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                Status = string.Format(StatusCreatingGeometry, progress);
            }));
        }

        #endregion

        #region Private Methods - Levels

        private async Task PopulateLevelsAsync()
        {
            if (Model == null) return;

            await Task.Run(() =>
            {
                var storeys = Model.Instances.OfType<IIfcBuildingStorey>().ToList();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Levels.Clear();
                    Levels.Add(AllLevelsItem);

                    foreach (var storey in storeys)
                    {
                        string name = GetDisplayName(storey);
                        Levels.Add(name);
                    }

                    SelectedLevel = AllLevelsItem;
                });
            });
        }

        private void OnLevelSelectionChanged()
        {
            if (Model == null || string.IsNullOrEmpty(SelectedLevel)) return;

            if (SelectedLevel == AllLevelsItem)
            {
                ShowAllLevels();
            }
            else
            {
                Task.Run(() => FilterByLevel(SelectedLevel));
            }
        }

        private void ShowAllLevels()
        {
            RaiseHiddenInstancesChanged(null);
            Status = StatusShowingAllLevels;
        }

        private void FilterByLevel(string levelName)
        {
            try
            {
                var storey = FindStoreyByName(levelName);
                if (storey == null) return;

                var visibleLabels = GetVisibleLabelsForStorey(storey);
                var hiddenElements = GetHiddenElements(visibleLabels);

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    RaiseHiddenInstancesChanged(hiddenElements);
                    Status = string.Format(StatusShowingLevel, levelName, visibleLabels.Count);
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Status = $"Error filtering: {ex.Message}";
                });
            }
        }

        #endregion

        #region Private Methods - Helpers

        private IIfcBuildingStorey FindStoreyByName(string name)
        {
            return Model.Instances.OfType<IIfcBuildingStorey>()
                .FirstOrDefault(s => s.Name == name || s.LongName == name);
        }

        private HashSet<int> GetVisibleLabelsForStorey(IIfcBuildingStorey storey)
        {
            var labels = new HashSet<int> { storey.EntityLabel };

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

            return labels;
        }

        private List<Xbim.Common.IPersistEntity> GetHiddenElements(HashSet<int> visibleLabels)
        {
            var allProducts = Model.Instances.OfType<IIfcProduct>().ToList();
            return allProducts
                .Where(p => !visibleLabels.Contains(p.EntityLabel))
                .Cast<Xbim.Common.IPersistEntity>()
                .ToList();
        }

        private string GetDisplayName(IIfcBuildingStorey storey)
        {
            return storey.Name
                ?? storey.LongName
                ?? string.Format(DefaultLevelNameFormat, storey.EntityLabel);
        }

        private void SetLoadingState(bool isLoading)
        {
            _isLoading = isLoading;
            ((RelayCommand)LoadModelCommand).NotifyCanExecuteChanged();
        }

        private void UpdateStatusWithModelInfo(string filePath)
        {
            if (Model != null)
            {
                string fileName = Path.GetFileName(filePath);
                int instanceCount = Model.Instances.Count();
                Status = string.Format(StatusModelLoaded, fileName, instanceCount);
            }
        }

        #endregion

        #region Private Methods - Events

        private void RaiseHiddenInstancesChanged(IEnumerable<Xbim.Common.IPersistEntity> hiddenInstances)
        {
            HiddenInstancesChanged?.Invoke(this, hiddenInstances);
        }

        private void RaiseViewHomeRequested()
        {
            ViewHomeRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Private Methods - Error Handling

        private void HandleLoadError(Exception ex)
        {
            Status = StatusError;
            MessageBox.Show(
                $"Error loading model: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        #endregion
    }
}
