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
    public class LogsViewModel : INotifyPropertyChanged, IDisposable
    {
        private string _selectedDay;
        private readonly string _logsFolder;
        private bool _isBusy;
        private string _lineInfo;
        private long _lastReadPosition;
        private int _totalLines;
        private string _searchText = "";
        private bool _isSearchVisible;

        private FileSystemWatcher _watcher;
        private Timer _debounceTimer;
        private const int DebounceMs = 400;

        // Evita ejecuciones concurrentes de IncrementalRefreshAsync
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        // Copia maestra de hasta 1.000 líneas en memoria (fuente de verdad para el filtro)
        private readonly List<string> _allLines = new List<string>();

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

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value ?? "";
                OnPropertyChanged();
                AplicarFiltro();
            }
        }

        public bool IsSearchVisible
        {
            get => _isSearchVisible;
            set
            {
                if (_isSearchVisible == value) return;
                _isSearchVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SearchVisibility));
                if (!value) SearchText = "";
            }
        }

        public Visibility SearchVisibility =>
            _isSearchVisible ? Visibility.Visible : Visibility.Collapsed;

        public ICommand RefreshCommand      { get; }
        public ICommand ClearCommand        { get; }
        public ICommand ToggleSearchCommand { get; }
        public ICommand ClearSearchCommand  { get; }

        public LogsViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _logsFolder = Path.GetFullPath(Path.Combine(basePath, "..\\ServicioReportesOracle\\Logs"));

            // Refresh usa carga incremental (sin spinner); cambio de día usa carga completa (con spinner)
            RefreshCommand      = new RelayCommand(_ => _ = IncrementalRefreshAsync());
            ClearCommand        = new RelayCommand(_ => ClearLogs());
            ToggleSearchCommand = new RelayCommand(_ => IsSearchVisible = !IsSearchVisible);
            ClearSearchCommand  = new RelayCommand(_ => { SearchText = ""; IsSearchVisible = false; });

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
                _allLines.Clear();
                Lines.Clear();
                Lines.Add("Ningún día seleccionado.");
                LineInfo = "";
                _lastReadPosition = 0;
                _totalLines = 0;
                return;
            }

            if (!File.Exists(path))
            {
                _allLines.Clear();
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

                _allLines.Clear();
                _allLines.AddRange(displayed);

                _lastReadPosition = result.EndPosition;
                _totalLines       = total;

                AplicarFiltro();
            }
            catch (Exception ex)
            {
                _allLines.Clear();
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
        {
            if (_disposed) return;
            await IncrementalRefreshAsync();
        }

        // ── Carga incremental reutilizable (RefreshCommand + watcher) ─────────
        internal async Task IncrementalRefreshAsync()
        {
            // Skip si ya hay un refresh en curso (evita corrupción de Lines por concurrencia)
            if (!await _refreshLock.WaitAsync(0)) return;
            try
            {
                string path = GetCurrentLogPath();
                if (string.IsNullOrEmpty(path)) return;

                if (!File.Exists(path))
                {
                    _allLines.Clear();
                    Lines.Clear();
                    Lines.Add($"Sin logs para {_selectedDay} — el archivo se creará automáticamente cuando el servicio escriba.");
                    LineInfo = "";
                    _lastReadPosition = 0;
                    _totalLines = 0;
                    return;
                }

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

                // Actualizar la copia maestra
                foreach (var line in result.Lines)
                    _allLines.Add(line);
                while (_allLines.Count > 1000)
                    _allLines.RemoveAt(0);

                if (string.IsNullOrEmpty(_searchText))
                {
                    // Fast path sin filtro: adición incremental (preserva auto-scroll)
                    foreach (var line in result.Lines)
                        Lines.Add(line);
                    while (Lines.Count > 1000)
                        Lines.RemoveAt(0);
                }
                else
                {
                    // Filtro activo: reconstruir vista filtrada
                    AplicarFiltroInterno();
                }

                ActualizarLineInfo();
            }
            catch { /* absorber cualquier excepción para no propagar al hilo UI */ }
            finally
            {
                _refreshLock.Release();
            }
        }

        // ── Aplica el filtro actual sobre _allLines y actualiza Lines + LineInfo ─
        private void AplicarFiltro()
        {
            AplicarFiltroInterno();
            ActualizarLineInfo();
        }

        private void AplicarFiltroInterno()
        {
            Lines.Clear();
            if (string.IsNullOrEmpty(_searchText))
            {
                foreach (var line in _allLines)
                    Lines.Add(line);
            }
            else
            {
                foreach (var line in _allLines)
                {
                    if (line.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                        Lines.Add(line);
                }
            }
        }

        private void ActualizarLineInfo()
        {
            if (string.IsNullOrEmpty(_searchText))
            {
                LineInfo = _totalLines > 1000
                    ? $"Mostrando últimas 1.000 líneas de {_totalLines:N0} totales"
                    : $"Mostrando {_totalLines} líneas";
            }
            else
            {
                LineInfo = Lines.Count > 0
                    ? $"{Lines.Count} coincidencias de {_allLines.Count} líneas en memoria"
                    : $"Sin coincidencias en las {_allLines.Count} líneas en memoria";
            }
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
                if (_disposed) return;
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _watcher?.Dispose(); } catch { }
            try { _debounceTimer?.Dispose(); } catch { }
            _watcher       = null;
            _debounceTimer = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
