using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class LogsViewModel : INotifyPropertyChanged
    {
        private string _selectedDay;
        private readonly string _logsFolder;
        private bool _isBusy;
        private string _lineInfo;
        private long _lastReadPosition;
        private int _totalLines;

        private FileSystemWatcher _watcher;
        private Timer _debounceTimer;
        private const int DebounceMs = 400;

        private static readonly string[] DiasOrden   = { "Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo" };
        private static readonly string[] DiasNombres = { "Domingo", "Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado" };

        public ObservableCollection<string> AvailableDays { get; } = new ObservableCollection<string>(DiasOrden);
        public ObservableCollection<string> Lines         { get; } = new ObservableCollection<string>();

        public string SelectedDay
        {
            get => _selectedDay;
            set
            {
                if (_selectedDay == value) return;
                _selectedDay = value;
                OnPropertyChanged();
                _ = CargarLogInicialAsync();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(LoadingVisibility)); }
        }

        public Visibility LoadingVisibility =>
            _isBusy ? Visibility.Visible : Visibility.Collapsed;

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

            // Refresh usa carga incremental (sin spinner); cambio de día usa carga completa (con spinner)
            RefreshCommand = new RelayCommand(_ => _ = IncrementalRefreshAsync());
            ClearCommand   = new RelayCommand(_ => ClearLogs());

            _selectedDay = DiasNombres[(int)DateTime.Today.DayOfWeek];
            _ = CargarLogInicialAsync();
        }

        private string GetCurrentLogPath()
        {
            if (string.IsNullOrEmpty(_selectedDay)) return null;
            return Path.Combine(_logsFolder, $"Log_{_selectedDay}.txt");
        }

        // ── Carga inicial completa (muestra IsBusy) ───────────────────────────
        internal async Task CargarLogInicialAsync()
        {
            ConfigurarWatcher();

            string path = GetCurrentLogPath();

            if (path == null)
            {
                Lines.Clear();
                Lines.Add("Ningún día seleccionado.");
                LineInfo = "";
                _lastReadPosition = 0;
                _totalLines = 0;
                return;
            }

            if (!File.Exists(path))
            {
                Lines.Clear();
                Lines.Add($"Sin logs para {_selectedDay} — el archivo se creará automáticamente cuando el servicio escriba.");
                LineInfo = "";
                _lastReadPosition = 0;
                _totalLines = 0;
                return;
            }

            IsBusy = true;
            try
            {
                var result = await Task.Run(() => LeerDesde(path, 0));

                int total     = result.Lines.Count;
                int skip      = Math.Max(0, total - 1000);
                var displayed = result.Lines.Skip(skip).ToList();

                Lines.Clear();
                foreach (var line in displayed)
                    Lines.Add(line);

                _lastReadPosition = result.EndPosition;
                _totalLines       = total;

                LineInfo = total > 1000
                    ? $"Mostrando últimas 1.000 líneas de {total:N0} totales"
                    : $"Mostrando {total} líneas";
            }
            catch (Exception ex)
            {
                Lines.Clear();
                Lines.Add("Error leyendo logs: " + ex.Message);
                LineInfo = "";
                _lastReadPosition = 0;
                _totalLines = 0;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ── Append-only: solo líneas nuevas, sin IsBusy ──────────────────────
        // Usado por FileSystemWatcher (async void requerido por el dispatcher)
        private async void AppendNuevasLineasAsync()
            => await IncrementalRefreshAsync();

        // ── Carga incremental reutilizable (RefreshCommand + watcher) ─────────
        internal async Task IncrementalRefreshAsync()
        {
            string path = GetCurrentLogPath();
            if (string.IsNullOrEmpty(path)) return;

            if (!File.Exists(path))
            {
                Lines.Clear();
                Lines.Add($"Sin logs para {_selectedDay} — el archivo se creará automáticamente cuando el servicio escriba.");
                LineInfo = "";
                _lastReadPosition = 0;
                _totalLines = 0;
                return;
            }

            try
            {
                long fileLen;
                try { fileLen = new FileInfo(path).Length; } catch { return; }

                if (fileLen < _lastReadPosition)
                {
                    // Archivo rotado/truncado → recarga completa con spinner
                    await CargarLogInicialAsync();
                    return;
                }
                if (fileLen == _lastReadPosition) return;

                long readFrom = _lastReadPosition;
                var result    = await Task.Run(() => LeerDesde(path, readFrom));

                if (result.Lines.Count == 0) return;

                _lastReadPosition  = result.EndPosition;
                _totalLines       += result.Lines.Count;

                foreach (var line in result.Lines)
                    Lines.Add(line);

                // Mantener límite de 1000 líneas visibles
                while (Lines.Count > 1000)
                    Lines.RemoveAt(0);

                LineInfo = _totalLines > 1000
                    ? $"Mostrando últimas 1.000 líneas de {_totalLines:N0} totales"
                    : $"Mostrando {_totalLines} líneas";
            }
            catch { /* archivo momentáneamente bloqueado por el servicio */ }
        }

        // ── FileSystemWatcher + debounce ──────────────────────────────────────
        private void ConfigurarWatcher()
        {
            try { _watcher?.Dispose(); } catch { }
            _debounceTimer?.Dispose();
            _watcher       = null;
            _debounceTimer = null;

            string path = GetCurrentLogPath();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(_logsFolder)) return;

            _debounceTimer = new Timer(_ =>
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(AppendNuevasLineasAsync));
            }, null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(_logsFolder)
            {
                Filter              = Path.GetFileName(path),
                NotifyFilter        = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
        }

        // ── Helper: lee líneas desde una posición del archivo ─────────────────
        private static (List<string> Lines, long EndPosition) LeerDesde(string path, long fromPosition)
        {
            var lines  = new List<string>();
            long endPos = fromPosition;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length <= fromPosition) return (lines, fromPosition);
                if (fromPosition > 0) fs.Seek(fromPosition, SeekOrigin.Begin);
                using (var reader = new StreamReader(fs))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        lines.Add(line);
                    endPos = fs.Position;
                }
            }
            return (lines, endPos);
        }

        private void ClearLogs()
        {
            try
            {
                string path = GetCurrentLogPath();
                if (path == null) return;
                File.WriteAllText(path, $"--- Log limpiado el {DateTime.Now} ---" + Environment.NewLine);
                _ = CargarLogInicialAsync();
            }
            catch (Exception ex)
            {
                Lines.Clear();
                Lines.Add("Error al limpiar logs: " + ex.Message);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
