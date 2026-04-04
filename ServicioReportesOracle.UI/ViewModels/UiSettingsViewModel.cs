using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ServicioReportesOracle.UI.Helpers;
using ServicioReportesOracle.UI.Models;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class UiSettingsViewModel : INotifyPropertyChanged
    {
        private readonly UiSettingsService _service = new UiSettingsService();
        private int _selectedWidthPercent;
        private bool _mostrarBotonCargarHistorico;

        public ObservableCollection<int> WidthPercentOptions { get; } =
            new ObservableCollection<int> { 70, 80, 90, 100 };

        public int SelectedWidthPercent
        {
            get => _selectedWidthPercent;
            set { _selectedWidthPercent = value; OnPropertyChanged(); }
        }

        public bool MostrarBotonCargarHistorico
        {
            get => _mostrarBotonCargarHistorico;
            set { _mostrarBotonCargarHistorico = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand ApplyNowCommand { get; }

        public UiSettingsViewModel()
        {
            var settings = _service.Load();
            _selectedWidthPercent = settings.WindowWidthPercent;
            _mostrarBotonCargarHistorico = settings.MostrarBotonCargarHistorico;

            SaveCommand    = new RelayCommand(_ => Save());
            ApplyNowCommand = new RelayCommand(_ => ApplyNow());
        }

        private void Save()
        {
            _service.Save(new UiSettingsModel
            {
                WindowWidthPercent = _selectedWidthPercent,
                MostrarBotonCargarHistorico = _mostrarBotonCargarHistorico
            });
            MainViewModel.Instance.ShowNotification(
                "Configuración guardada. Se aplicará al reiniciar la aplicación.", "Success");
        }

        private void ApplyNow()
        {
            var win = Application.Current.MainWindow;
            win.Width = SystemParameters.PrimaryScreenWidth * (_selectedWidthPercent / 100.0);
            win.Left  = (SystemParameters.PrimaryScreenWidth - win.Width) / 2;
            MainViewModel.Instance.ShowNotification(
                $"Ancho aplicado: {_selectedWidthPercent}%", "Success");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
