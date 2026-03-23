using System.Windows;
using System.Windows.Controls;
using ServicioReportesOracle.UI.ViewModels;

namespace ServicioReportesOracle.UI.Views
{
    public partial class GeneralConfigView : UserControl
    {
        public GeneralConfigView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is GeneralConfigViewModel vm)
                SmtpPasswordBox.Password = vm.SmtpPassword ?? string.Empty;
        }

        private void SmtpPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is GeneralConfigViewModel vm)
                vm.SmtpPassword = SmtpPasswordBox.Password;
        }
    }
}
