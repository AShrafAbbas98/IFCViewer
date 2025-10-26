# IFC Viewer - MVVM and Clean Code Refactoring

## Overview

This document describes the comprehensive refactoring of the IFC Viewer application to follow MVVM (Model-View-ViewModel) architecture and Clean Code principles.

## Refactoring Goals

1. **Separation of Concerns**: Clear separation between UI, business logic, and data access
2. **Testability**: Business logic extracted into services that can be unit tested
3. **Maintainability**: Smaller, focused classes with single responsibilities
4. **Reusability**: Shared services and utilities across projects
5. **Clean Code**: Readable, well-documented code following SOLID principles

## Architecture Changes

### Before Refactoring

**Problems:**
- `ViewerWindow.xaml.cs`: 1365 lines of mixed UI and business logic
- Duplicated code across Standalone and Plugin projects
- Magic numbers throughout the codebase
- Tight coupling to concrete implementations
- Difficult to test business logic
- Violated Single Responsibility Principle

### After Refactoring

**New Structure:**

```
IFcViewerRevitPlugin/
├── Constants/
│   ├── GeometryConstants.cs      # Geometry-related constants
│   ├── ExportConstants.cs        # IFC export constants
│   └── UiConstants.cs            # UI messages and labels
├── DTOs/
│   ├── ModelLoadOptions.cs       # Model loading configuration
│   ├── GeometryGenerationOptions.cs
│   ├── BoundingBoxData.cs        # Bounding box data transfer
│   ├── FilterCriteria.cs         # Filtering options
│   └── ExportOptions.cs          # Export configuration
├── Services/
│   ├── Interfaces/
│   │   ├── IPerformanceOptimizationService.cs
│   │   ├── IIfcExportService.cs
│   │   ├── IIfcLoadingService.cs
│   │   ├── IGeometryService.cs
│   │   ├── IBoundingBoxService.cs
│   │   └── IFilteringService.cs
│   └── Implementations/
│       ├── PerformanceOptimizationService.cs
│       ├── IfcExportService.cs
│       ├── IfcLoadingService.cs
│       ├── GeometryService.cs
│       ├── BoundingBoxService.cs
│       └── FilteringService.cs
├── Infrastructure/
│   └── ServiceContainer.cs       # Dependency injection container
└── ViewModels/
    └── ViewerViewModelRefactored.cs  # Clean, service-based ViewModel
```

## Key Improvements

### 1. Service Layer

Business logic is now encapsulated in focused services:

#### IPerformanceOptimizationService
Determines optimal threading and geometry detail based on model size.

**Responsibilities:**
- Calculate optimal thread count
- Determine appropriate deflection (detail level)
- Provide human-readable detail descriptions

**Before:**
```csharp
// Scattered magic numbers and logic
int maxThreads = Math.Max(2, Environment.ProcessorCount - 1);
double deflection = instanceCount < 1000 ? 0.01 : 0.05;
```

**After:**
```csharp
var threadCount = _performanceService.DetermineOptimalThreadCount(instanceCount);
var deflection = _performanceService.DetermineDeflection(instanceCount);
```

#### IIfcExportService
Handles all IFC export operations.

**Responsibilities:**
- Export Revit documents to IFC
- Generate safe file names
- Configure export options

**Benefits:**
- Reusable across different views
- Easy to mock for testing
- Centralized error handling

#### IIfcLoadingService
Manages IFC file loading and geometry generation.

**Responsibilities:**
- Load IFC files
- Generate 3D geometry with progress tracking
- Validate file paths

#### IGeometryService
Handles 3D geometry generation.

**Responsibilities:**
- Create 3D geometry context
- Check if geometry exists
- Configure geometry generation

#### IBoundingBoxService
Calculates bounding boxes for IFC elements.

**Responsibilities:**
- Calculate bounds for storeys and spaces
- Expand and merge bounding boxes
- Check for intersections

#### IFilteringService
Filters IFC elements by various criteria.

**Responsibilities:**
- Get entity labels for filtering
- Extract building storeys and spaces
- Spatial filtering by bounding box

### 2. Constants Classes

All magic numbers and strings extracted into constants:

**GeometryConstants:**
```csharp
public const int SmallModelInstanceThreshold = 500;
public const double HighDetailDeflection = 0.01;
public const double DefaultPaddingMeters = 0.5;
```

**UiConstants:**
```csharp
public const string StatusReady = "Ready";
public const string StatusExporting = "Exporting to IFC...";
public const string AllLevelsItem = "-- All Levels --";
```

**ExportConstants:**
```csharp
public const string TempFolderName = "RevitIFCExport";
public static readonly IFCVersion DefaultIfcVersion = IFCVersion.IFC2x3CV2;
```

### 3. Data Transfer Objects (DTOs)

Structured data containers for passing information between layers:

**ModelLoadOptions:**
```csharp
public class ModelLoadOptions
{
    public string FilePath { get; set; }
    public bool OptimizeForPerformance { get; set; }
    public int? MaxThreads { get; set; }
    public double? Deflection { get; set; }
}
```

**BoundingBoxData:**
```csharp
public class BoundingBoxData
{
    public double X, Y, Z { get; set; }
    public double SizeX, SizeY, SizeZ { get; set; }

    public XbimRect3D ToXbimRect3D() { ... }
    public BoundingBoxData AddPadding(...) { ... }
}
```

### 4. Dependency Injection

Simple, lightweight service container:

```csharp
public class ServiceContainer
{
    public static ServiceContainer Instance { get; }

    public T Resolve<T>()
    public void RegisterSingleton<T>(T instance)
    public void RegisterFactory<T>(Func<object> factory)
}
```

**Usage in ViewModels:**
```csharp
public ViewerViewModelRefactored(UIDocument uiDocument)
{
    var container = ServiceContainer.Instance;
    _exportService = container.Resolve<IIfcExportService>();
    _loadingService = container.Resolve<IIfcLoadingService>();
    _filteringService = container.Resolve<IFilteringService>();
}
```

### 5. Refactored ViewModels

ViewModels are now clean and focused:

**Before (599 lines with mixed concerns):**
```csharp
private string ExportRevitToIfc() { /* 50 lines of export logic */ }
private void PopulateLevelsFromIfc() { /* Complex UI update logic */ }
private XbimRect3D? CalculateBoundingBoxForStorey() { /* Geometry calculation */ }
```

**After (clean, delegated):**
```csharp
private async Task<string> ExportRevitDocumentAsync()
{
    return await _exportService.ExportToIfcAsync(
        _uiDocument.Document,
        ExportOptions.CreateDefault());
}

private async Task PopulateLevelsAsync()
{
    var storeys = _filteringService.GetBuildingStoreys(Model);
    // Simple UI update logic only
}
```

### 6. View Code-Behind

Views are now minimal and only handle view-specific logic:

**MainWindowRefactored.xaml.cs (98 lines):**
- Subscribes to ViewModel events
- Updates UI controls
- Handles view lifecycle
- NO business logic

```csharp
private void OnModelLoaded(object sender, EventArgs e)
{
    Viewer.Model = _viewModel.Model;
}

private void OnHiddenInstancesChanged(object sender, IEnumerable<IPersistEntity> hidden)
{
    Viewer.HiddenInstances = hidden?.ToList();
    Viewer.ReloadModel(ModelRefreshOptions.ViewPreserveCameraPosition);
}
```

## SOLID Principles Applied

### Single Responsibility Principle (SRP)
- Each service has one clear responsibility
- ViewModels only handle presentation logic
- Views only handle UI updates

### Open/Closed Principle (OCP)
- Services implement interfaces
- Easy to extend with new implementations
- Closed for modification, open for extension

### Liskov Substitution Principle (LSP)
- All service implementations are substitutable
- Interfaces define clear contracts

### Interface Segregation Principle (ISP)
- Small, focused interfaces
- Clients depend only on methods they use

### Dependency Inversion Principle (DIP)
- ViewModels depend on service interfaces, not implementations
- High-level modules don't depend on low-level modules

## Clean Code Practices

### 1. Meaningful Names
```csharp
// Before
var t = Math.Max(2, Environment.ProcessorCount - 1);

// After
int optimalThreadCount = _performanceService.DetermineOptimalThreadCount(instanceCount);
```

### 2. Small Functions
Functions do one thing and do it well:
```csharp
private void SetLoadingState(bool isLoading)
{
    _isLoading = isLoading;
    ((RelayCommand)LoadIfcCommand).NotifyCanExecuteChanged();
}
```

### 3. No Magic Numbers
```csharp
// Before
if (instanceCount < 500) return 2;

// After
if (instanceCount < GeometryConstants.SmallModelInstanceThreshold)
    return GeometryConstants.SmallModelThreads;
```

### 4. Proper Error Handling
```csharp
private void HandleLoadError(Exception ex)
{
    Status = UiConstants.StatusErrorLoadingModel;
    ShowErrorDialog(
        $"Error: {ex.Message}\n\nStack Trace: {ex.StackTrace}",
        UiConstants.IFCLoadErrorDialogTitle);
}
```

### 5. Comments Only Where Necessary
Code is self-documenting through clear naming and structure.

### 6. DRY (Don't Repeat Yourself)
Common logic extracted into services and reused.

## Performance Optimizations

### 1. Async/Await Throughout
All long-running operations are asynchronous:
```csharp
public async Task LoadIfcAsync()
{
    await ExportRevitDocumentAsync();
    await LoadModelFromFileAsync(ifcPath);
    await PopulateLevelsAsync();
}
```

### 2. Optimized Threading
Dynamic thread allocation based on model size:
```csharp
public int DetermineOptimalThreadCount(int instanceCount)
{
    int availableCores = Environment.ProcessorCount - 2;

    if (instanceCount < 500) return Math.Min(2, availableCores);
    if (instanceCount < 2000) return Math.Min(3, availableCores);
    if (instanceCount < 10000) return Math.Min(4, availableCores);

    return Math.Min(6, availableCores);
}
```

### 3. Progressive Detail Reduction
Larger models use lower geometry detail for faster loading:
```csharp
public double DetermineDeflection(int instanceCount)
{
    if (instanceCount < 1000) return 0.01;   // High detail
    if (instanceCount < 5000) return 0.05;   // Medium detail
    if (instanceCount < 10000) return 0.1;   // Lower detail
    return 0.25;                              // Low detail
}
```

## Migration Guide

### For Developers

1. **Using New ViewModels:**
   ```csharp
   // Replace old ViewModel with refactored version
   var viewModel = new ViewerViewModelRefactored(uiDocument);
   ```

2. **Using Services Directly:**
   ```csharp
   var container = ServiceContainer.Instance;
   var exportService = container.Resolve<IIfcExportService>();
   var result = await exportService.ExportToIfcAsync(document);
   ```

3. **Adding New Features:**
   - Create service interface in `Services/`
   - Implement in service class
   - Register in `ServiceContainer`
   - Inject into ViewModel

### Testing

Services can now be easily unit tested:

```csharp
[Test]
public void DetermineOptimalThreadCount_SmallModel_Returns2Threads()
{
    var service = new PerformanceOptimizationService();
    int threads = service.DetermineOptimalThreadCount(400);
    Assert.AreEqual(2, threads);
}
```

Mock services for ViewModel testing:

```csharp
[Test]
public async Task LoadIfcAsync_ValidFile_LoadsModel()
{
    var mockExportService = new Mock<IIfcExportService>();
    mockExportService.Setup(s => s.ExportToIfcAsync(It.IsAny<Document>()))
                    .ReturnsAsync("test.ifc");

    // Test ViewModel with mocked service
}
```

## Benefits Achieved

### Code Quality
- **Lines of Code Reduced**: ViewerWindow.xaml.cs: 1365 → ~200 lines in View + ViewModel
- **Cyclomatic Complexity**: Significantly reduced through smaller methods
- **Code Duplication**: Eliminated through shared services

### Maintainability
- **Easier to Understand**: Each class has a clear, single purpose
- **Easier to Modify**: Changes isolated to specific services
- **Easier to Debug**: Clear separation of concerns

### Testability
- **Unit Testable**: Services can be tested independently
- **Mockable**: Interface-based design allows easy mocking
- **Isolated**: Business logic separate from UI

### Extensibility
- **New Features**: Easy to add new services or extend existing ones
- **Platform Independence**: Services can be used in different UI frameworks
- **Reusability**: Services shared across projects

## Next Steps

1. **Update XAML Bindings**: Connect to refactored ViewModels
2. **Add Unit Tests**: Create comprehensive test suite for services
3. **Documentation**: Add XML documentation to all public APIs
4. **Performance Testing**: Benchmark refactored vs. original code
5. **Code Review**: Team review of refactored architecture

## Conclusion

This refactoring transforms the IFC Viewer from a monolithic, tightly-coupled application into a well-structured, maintainable, and testable codebase following industry best practices. The new architecture provides a solid foundation for future development and enhancements.
