using System.Windows.Controls;
using ServicioReportesOracle.UI.ViewModels;

namespace ServicioReportesOracle.UI.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
        }

        private void UserControl_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is DashboardViewModel vm)
            {
                vm.Dispose();
            }
        }
    }
}
