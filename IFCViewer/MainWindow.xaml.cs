using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Xbim.Ifc;
using Xbim.ModelGeometry.Scene;

namespace IFCViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;

            openFile();

        }

        void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ModelProvider.Refresh();
        }

        private ObjectDataProvider ModelProvider
        {
            get
            {
                return MainFrame.DataContext as ObjectDataProvider;
            }
        }

        public void openFile()
        {
            var model = IfcStore.Open(@"C:\Users\Mas\Downloads\test1.ifc");
            var context = new Xbim3DModelContext(model);
            context.CreateContext();
            ModelProvider.ObjectInstance = model;
        }
    }
}
