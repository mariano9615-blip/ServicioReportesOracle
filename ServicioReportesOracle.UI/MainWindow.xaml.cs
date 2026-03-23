using System.Windows;
using ServicioReportesOracle.UI.ViewModels;

namespace ServicioReportesOracle.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
}
