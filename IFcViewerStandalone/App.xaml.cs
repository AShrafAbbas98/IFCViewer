using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace IFcViewerStandalone
{
    /// <summary>
    /// Standalone WPF Application for IFC Viewing
    /// This runs in its own process, completely independent of Revit
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Get IFC file path from command line arguments
            string ifcPath = null;
            if (e.Args.Length > 0)
            {
                ifcPath = e.Args[0];
            }

            // Create and show main window
            var mainWindow = new MainWindow(ifcPath);
            mainWindow.Show();
        }
    }
}
