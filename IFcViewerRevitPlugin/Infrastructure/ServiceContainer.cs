using IFcViewerRevitPlugin.Services;
using System;
using System.Collections.Generic;

namespace IFcViewerRevitPlugin.Infrastructure
{
    /// <summary>
    /// Simple service container for dependency injection
    /// Implements a basic IoC container without external dependencies
    /// </summary>
    public class ServiceContainer
    {
        private static ServiceContainer _instance;
        private static readonly object _lock = new object();
        private readonly Dictionary<Type, object> _singletons = new Dictionary<Type, object>();
        private readonly Dictionary<Type, Func<object>> _factories = new Dictionary<Type, Func<object>>();

        private ServiceContainer()
        {
            RegisterServices();
        }

        public static ServiceContainer Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ServiceContainer();
                        }
                    }
                }
                return _instance;
            }
        }

        private void RegisterServices()
        {
            // Register singletons
            RegisterSingleton<IPerformanceOptimizationService>(new PerformanceOptimizationService());
            RegisterSingleton<IGeometryService>(new GeometryService());
            RegisterSingleton<IIfcExportService>(new IfcExportService());
            RegisterSingleton<IBoundingBoxService>(new BoundingBoxService());

            // Revit-specific services
            RegisterSingleton<IRevitDocumentService>(new RevitDocumentService());
            RegisterSingleton<IRevitViewService>(new RevitViewService());
            RegisterSingleton<IExportCacheService>(new ExportCacheService());

            // Register factories for services that need dependencies
            RegisterFactory<IFilteringService>(() =>
                new FilteringService(Resolve<IBoundingBoxService>()));

            RegisterFactory<IIfcLoadingService>(() =>
                new IfcLoadingService(
                    Resolve<IGeometryService>(),
                    Resolve<IPerformanceOptimizationService>()));
        }

        public void RegisterSingleton<T>(T instance)
        {
            _singletons[typeof(T)] = instance;
        }

        public void RegisterFactory<T>(Func<object> factory)
        {
            _factories[typeof(T)] = factory;
        }

        public T Resolve<T>()
        {
            var type = typeof(T);

            // Check singletons first
            if (_singletons.ContainsKey(type))
            {
                return (T)_singletons[type];
            }

            // Try factories
            if (_factories.ContainsKey(type))
            {
                var instance = _factories[type]();
                _singletons[type] = instance; // Cache it
                return (T)instance;
            }

            throw new InvalidOperationException($"Service of type {type.Name} is not registered");
        }

        /// <summary>
        /// Clears all registered services (useful for testing)
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }
    }
}
