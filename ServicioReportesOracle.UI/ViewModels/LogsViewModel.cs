using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class LogsViewModel : INotifyPropertyChanged
    {
        private string _selectedDay;
        private readonly string _logsFolder;
        private readonly DispatcherTimer _timer;
        private bool _isBusy;
        private string _lineInfo;

        private static readonly string[] DiasOrden   = { "Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo" };
        private static readonly string[] DiasNombres = { "Domingo", "Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado" };

        public ObservableCollection<string> AvailableDays { get; } = new ObservableCollection<string>(DiasOrden);
        public ObservableCollection<string> Lines { get; } = new ObservableCollection<string>();

        public string SelectedDay
        {
            get => _selectedDay;
            set
            {
                if (_selectedDay == value) return;
                _selectedDay = value;
                OnPropertyChanged();
                LoadLogs();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(LoadingVisibility)); }
        }

        public System.Windows.Visibility LoadingVisibility =>
            _isBusy ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string LineInfo
        {
            get => _lineInfo;
            set { _lineInfo = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ClearCommand   { get; }

        public LogsViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _logsFolder = Path.GetFullPath(Path.Combine(basePath, "..\\ServicioReportesOracle\\Logs"));

            RefreshCommand = new RelayCommand(_ => LoadLogs());
            ClearCommand   = new RelayCommand(_ => ClearLogs());

            _selectedDay = DiasNombres[(int)DateTime.Today.DayOfWeek];

            LoadLogs();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (s, e) => LoadLogs();
            _timer.Start();
        }

        private string GetCurrentLogPath()
        {
            if (string.IsNullOrEmpty(_selectedDay)) return null;
            return Path.Combine(_logsFolder, $"Log_{_selectedDay}.txt");
        }

        private async void LoadLogs()
        {
            try
            {
                string path = GetCurrentLogPath();
                if (path == null)
                {
                    Lines.Clear();
                    Lines.Add("Ningún día seleccionado.");
                    LineInfo = "";
                    return;
                }

                if (!File.Exists(path))
                {
                    Lines.Clear();
                    Lines.Add($"Sin logs para {_selectedDay} — el archivo se creará automáticamente cuando el servicio escriba.");
                    LineInfo = "";
                    return;
                }

                IsBusy = true;
                try
                {
                    List<string> allLines = await Task.Run(() =>
                    {
                        var result = new List<string>();
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new StreamReader(fs))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                                result.Add(line);
                        }
                        return result;
                    });

                    // Tomar las últimas 1000 líneas
                    int total     = allLines.Count;
                    int skip      = Math.Max(0, total - 1000);
                    var displayed = allLines.Skip(skip).ToList();

                    Lines.Clear();
                    foreach (var line in displayed)
                        Lines.Add(line);

                    LineInfo = total > 1000
                        ? $"Mostrando últimas 1.000 líneas de {total:N0} totales"
                        : $"Mostrando {total} líneas";
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                Lines.Clear();
                Lines.Add("Error leyendo logs: " + ex.Message);
                LineInfo = "";
                IsBusy = false;
            }
        }

        private void ClearLogs()
        {
            try
            {
                string path = GetCurrentLogPath();
                if (path == null) return;
                File.WriteAllText(path, $"--- Log limpiado el {DateTime.Now} ---" + Environment.NewLine);
                LoadLogs();
            }
            catch (Exception ex)
            {
                Lines.Clear();
                Lines.Add("Error al limpiar logs: " + ex.Message);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
