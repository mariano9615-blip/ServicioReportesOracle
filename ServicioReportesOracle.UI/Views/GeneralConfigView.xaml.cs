using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ServicioReportesOracle.UI.ViewModels;

namespace ServicioReportesOracle.UI.Views
{
    public partial class GeneralConfigView : UserControl
    {
        // Evita reentrada entre PropertyChanged del VM y PasswordChanged del control
        private bool _updatingPasswordBox;

        public GeneralConfigView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is GeneralConfigViewModel oldVm)
                oldVm.PropertyChanged -= OnVmPropertyChanged;

            if (e.NewValue is GeneralConfigViewModel vm)
            {
                // Sincronizar valor actual (puede ser vacío si el async load no terminó)
                _updatingPasswordBox = true;
                SmtpPasswordBox.Password = vm.SmtpPassword ?? string.Empty;
                _updatingPasswordBox = false;

                // Resetear visibilidad al cambiar de ViewModel (navegación)
                vm.SmtpPasswordVisible = false;
                vm.SmtpPasswordPlainText = null;

                // Suscribir para capturar el valor cuando el async load termine
                vm.PropertyChanged += OnVmPropertyChanged;
            }
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GeneralConfigViewModel.SmtpPassword) && !_updatingPasswordBox)
            {
                if (DataContext is GeneralConfigViewModel vm)
                {
                    _updatingPasswordBox = true;
                    SmtpPasswordBox.Password = vm.SmtpPassword ?? string.Empty;
                    _updatingPasswordBox = false;
                }
            }
        }

        private void SmtpPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_updatingPasswordBox && DataContext is GeneralConfigViewModel vm)
                vm.SmtpPassword = SmtpPasswordBox.Password;
        }
    }
}
