using System;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IFcViewerRevitPlugin.Models
{
    public class IfcModelWrapper : IDisposable
    {
        private IfcStore _model;

        public IfcStore Model
        {
            get => _model;
            private set => _model = value;
        }

        public void LoadIfcFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            if (!System.IO.File.Exists(path))
                throw new System.IO.FileNotFoundException($"IFC file not found: {path}");

            try
            {
                // Dispose previous model if exists
                Model?.Dispose();

                // Open the IFC file (read-only)
                Model = IfcStore.Open(path);

                if (Model == null)
                    throw new Exception("Failed to open IFC file");
            }
            catch (Exception ex)
            {
                Model = null;
                throw new Exception($"Error loading IFC file: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            Model?.Dispose();
            Model = null;
        }
    }
}