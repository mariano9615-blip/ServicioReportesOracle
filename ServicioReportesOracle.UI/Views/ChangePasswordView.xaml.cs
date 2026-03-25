using System.Windows;
using System.Windows.Controls;
using ServicioReportesOracle.UI.ViewModels;

namespace ServicioReportesOracle.UI.Views
{
    public partial class ChangePasswordView : UserControl
    {
        public ChangePasswordView()
        {
            InitializeComponent();
        }

        // Sincroniza el PasswordBox con el ViewModel (PasswordBox no soporta binding directo)
        private void CurrentPasswordBox_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChangePasswordViewModel vm)
                vm.CurrentPassword = ((PasswordBox)sender).Password;
        }

        private void NewPasswordBox_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChangePasswordViewModel vm)
                vm.NewPassword = ((PasswordBox)sender).Password;
        }

        private void ConfirmPasswordBox_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChangePasswordViewModel vm)
                vm.ConfirmPassword = ((PasswordBox)sender).Password;
        }
    }
}
