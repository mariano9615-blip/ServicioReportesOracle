using System.Windows;
using System.Windows.Controls;
using ServicioReportesOracle.UI.ViewModels;

namespace ServicioReportesOracle.UI.Views
{
    public partial class MetricasView : UserControl
    {
        public MetricasView()
        {
            InitializeComponent();
            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MetricasViewModel vm)
                vm.Dispose();
        }
    }
}
