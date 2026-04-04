using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ServicioReportesOracle.UI.Models
{
    public class UiSettingsModel : INotifyPropertyChanged
    {
        private int _windowWidthPercent = 80;

        public int WindowWidthPercent
        {
            get => _windowWidthPercent;
            set { _windowWidthPercent = value; OnPropertyChanged(); }
        }

        private bool _mostrarBotonCargarHistorico = true;

        public bool MostrarBotonCargarHistorico
        {
            get => _mostrarBotonCargarHistorico;
            set { _mostrarBotonCargarHistorico = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
