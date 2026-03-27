using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using ServicioOracleReportes;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class MlogisHistorialViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly string _historialPath;
        private bool _isModoCorrida = true;
        private bool _isSearchVisible;
        private string _searchText = "";
        private string _lineInfo = "";
        private CorridaItem _selectedCorrida;
        private bool _disposed;

        private FileSystemWatcher _watcher;
        private Timer _debounceTimer;
        private const int DebounceMs = 2000;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

        private List<MlogisCorrida> _historial = new List<MlogisCorrida>();
        private readonly List<RegistroDisplayItem> _allRegistrosCorrida = new List<RegistroDisplayItem>();
        private readonly List<RegistroDisplayItem> _allRegistrosUnicos  = new List<RegistroDisplayItem>();

        public ObservableCollection<CorridaItem>         Corridas        { get; } = new ObservableCollection<CorridaItem>();
        public ObservableCollection<RegistroDisplayItem> RegistrosCorrida{ get; } = new ObservableCollection<RegistroDisplayItem>();
        public ObservableCollection<RegistroDisplayItem> RegistrosUnicos { get; } = new ObservableCollection<RegistroDisplayItem>();

        // ── Propiedades ───────────────────────────────────────────────────────

        public CorridaItem SelectedCorrida
        {
            get => _selectedCorrida;
            set
            {
                if (_selectedCorrida == value) return;
                _selectedCorrida = value;
                OnPropertyChanged();
                if (_isModoCorrida)
                    RebuildRegistrosCorrida();
            }
        }

        public bool IsModoCorrida
        {
            get => _isModoCorrida;
            set
            {
                if (_isModoCorrida == value) return;
                _isModoCorrida = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LeftPanelVisibility));
                OnPropertyChanged(nameof(DataGridCorridaVisibility));
                OnPropertyChanged(nameof(DataGridUnicoVisibility));
                SearchText = "";
                if (value)
                    RebuildRegistrosCorrida();
                else
                    RebuildRegistrosUnicos();
            }
        }

        public Visibility LeftPanelVisibility      => _isModoCorrida ? Visibility.Visible  : Visibility.Collapsed;
        public Visibility DataGridCorridaVisibility => _isModoCorrida ? Visibility.Visible  : Visibility.Collapsed;
        public Visibility DataGridUnicoVisibility   => _isModoCorrida ? Visibility.Collapsed : Visibility.Visible;

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

        public Visibility SearchVisibility => _isSearchVisible ? Visibility.Visible : Visibility.Collapsed;

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

        public string LineInfo
        {
            get => _lineInfo;
            set { _lineInfo = value; OnPropertyChanged(); }
        }

        // ── Comandos ──────────────────────────────────────────────────────────

        public ICommand RefreshCommand           { get; }
        public ICommand ToggleModoCorridaCommand { get; }
        public ICommand ToggleModoUnicoCommand   { get; }
        public ICommand ToggleSearchCommand      { get; }
        public ICommand ClearSearchCommand       { get; }

        // ── Constructor ───────────────────────────────────────────────────────

        public MlogisHistorialViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _historialPath = Path.GetFullPath(
                Path.Combine(basePath, @"..\ServicioReportesOracle\Logs\mlogis_historial.json"));

            RefreshCommand           = new RelayCommand(_ => _ = CargarAsync());
            ToggleModoCorridaCommand = new RelayCommand(_ => IsModoCorrida = true);
            ToggleModoUnicoCommand   = new RelayCommand(_ => IsModoCorrida = false);
            ToggleSearchCommand      = new RelayCommand(_ => IsSearchVisible = !IsSearchVisible);
            ClearSearchCommand       = new RelayCommand(_ => { SearchText = ""; IsSearchVisible = false; });

            ConfigurarWatcher();
            _ = CargarAsync();
        }

        // ── Carga del archivo ─────────────────────────────────────────────────

        internal async Task CargarAsync()
        {
            if (!await _refreshLock.WaitAsync(0)) return;
            try
            {
                if (!File.Exists(_historialPath))
                {
                    _historial = new List<MlogisCorrida>();
                    RebuildAll();
                    LineInfo = "Archivo mlogis_historial.json no encontrado";
                    return;
                }

                MlogisHistorial parsed = null;
                try
                {
                    string json = await Task.Run(() =>
                    {
                        using (var fs = new FileStream(_historialPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new System.IO.StreamReader(fs))
                            return reader.ReadToEnd();
                    });
                    parsed = JsonConvert.DeserializeObject<MlogisHistorial>(json);
                }
                catch (Exception ex)
                {
                    MainViewModel.Instance?.ShowNotification("Error leyendo historial SOAP: " + ex.Message, "Error");
                    LineInfo = "Error al leer archivo";
                    return;
                }

                _historial = parsed?.Corridas ?? new List<MlogisCorrida>();
                RebuildAll();
            }
            catch { /* absorber para no propagar al hilo UI */ }
            finally
            {
                _refreshLock.Release();
            }
        }

        // ── Reconstrucción de colecciones ─────────────────────────────────────

        private void RebuildAll()
        {
            var sorted = _historial.OrderByDescending(c => c.FechaEjecucion).ToList();

            // Preservar selección actual
            DateTime? prevFecha = _selectedCorrida?.Corrida?.FechaEjecucion;

            Corridas.Clear();
            foreach (var c in sorted)
                Corridas.Add(new CorridaItem(c));

            CorridaItem toSelect = null;
            if (prevFecha.HasValue)
                toSelect = Corridas.FirstOrDefault(x => x.Corrida.FechaEjecucion == prevFecha.Value);
            if (toSelect == null)
                toSelect = Corridas.FirstOrDefault();

            // Forzar trigger aunque sea el mismo objeto
            _selectedCorrida = null;
            SelectedCorrida = toSelect;

            if (!_isModoCorrida)
                RebuildRegistrosUnicos();
        }

        private void RebuildRegistrosCorrida()
        {
            _allRegistrosCorrida.Clear();
            if (_selectedCorrida?.Corrida?.Registros != null)
            {
                foreach (var r in _selectedCorrida.Corrida.Registros)
                    _allRegistrosCorrida.Add(new RegistroDisplayItem(r));
            }
            AplicarFiltroCorrida();
            ActualizarLineInfo();
        }

        private void RebuildRegistrosUnicos()
        {
            _allRegistrosUnicos.Clear();

            // Aplanar y deduplicar por ID; conservar el de ultima_vez_visto más reciente
            var byId = new Dictionary<string, (MlogisRegistro Reg, int Count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var corrida in _historial)
            {
                if (corrida.Registros == null) continue;
                foreach (var r in corrida.Registros)
                {
                    if (string.IsNullOrEmpty(r.Id)) continue;
                    if (byId.TryGetValue(r.Id, out var ex))
                    {
                        var best = r.UltimaVezVisto > ex.Reg.UltimaVezVisto ? r : ex.Reg;
                        byId[r.Id] = (best, ex.Count + 1);
                    }
                    else
                    {
                        byId[r.Id] = (r, 1);
                    }
                }
            }

            foreach (var kv in byId.Values)
                _allRegistrosUnicos.Add(new RegistroDisplayItem(kv.Reg) { CantidadCorridas = kv.Count });

            AplicarFiltroUnico();
            ActualizarLineInfo();
        }

        // ── Filtrado ──────────────────────────────────────────────────────────

        private void AplicarFiltro()
        {
            if (_isModoCorrida) AplicarFiltroCorrida(); else AplicarFiltroUnico();
            ActualizarLineInfo();
        }

        private void AplicarFiltroCorrida()
        {
            RegistrosCorrida.Clear();
            foreach (var r in Filtrar(_allRegistrosCorrida))
                RegistrosCorrida.Add(r);
        }

        private void AplicarFiltroUnico()
        {
            RegistrosUnicos.Clear();
            foreach (var r in Filtrar(_allRegistrosUnicos))
                RegistrosUnicos.Add(r);
        }

        private IEnumerable<RegistroDisplayItem> Filtrar(IEnumerable<RegistroDisplayItem> src)
        {
            if (string.IsNullOrEmpty(_searchText)) return src;
            return src.Where(r =>
                r.Id.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.NroComprobante.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.Ctg.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ActualizarLineInfo()
        {
            int corridas = _historial.Count;
            if (_isModoCorrida)
            {
                int total = _allRegistrosCorrida.Count;
                LineInfo = string.IsNullOrEmpty(_searchText)
                    ? $"{total} registros | {corridas} corridas"
                    : $"{RegistrosCorrida.Count} coincidencias de {total} registros";
            }
            else
            {
                int total = _allRegistrosUnicos.Count;
                LineInfo = string.IsNullOrEmpty(_searchText)
                    ? $"{total} IDs únicos | {corridas} corridas"
                    : $"{RegistrosUnicos.Count} coincidencias de {total}";
            }
        }

        // ── FileSystemWatcher + debounce ──────────────────────────────────────

        private void ConfigurarWatcher()
        {
            try { _watcher?.Dispose(); } catch { }
            _debounceTimer?.Dispose();
            _watcher       = null;
            _debounceTimer = null;

            string dir = Path.GetDirectoryName(_historialPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            _debounceTimer = new Timer(_ =>
            {
                if (_disposed) return;
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => _ = CargarAsync()));
            }, null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(dir)
            {
                Filter              = Path.GetFileName(_historialPath),
                NotifyFilter        = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _watcher?.Dispose(); }       catch { }
            try { _debounceTimer?.Dispose(); } catch { }
            _watcher       = null;
            _debounceTimer = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ─── Modelos de presentación ──────────────────────────────────────────────

    public class CorridaItem
    {
        public MlogisCorrida Corrida   { get; }
        public string        Hora      { get; }
        public string        Tipo      { get; }   // "FULL" | "DELTA"
        public string        CountText { get; }
        public bool          IsEmpty   { get; }

        public CorridaItem(MlogisCorrida c)
        {
            Corrida   = c;
            Hora      = c.FechaEjecucion.ToString("HH:mm");
            Tipo      = (c.Tipo ?? "").ToUpper().Contains("FULL") ? "FULL" : "DELTA";
            int n     = c.Registros?.Count ?? 0;
            CountText = $"{n} IDs";
            IsEmpty   = n == 0;
        }
    }

    public class RegistroDisplayItem
    {
        public string Id              { get; set; } = "";
        public string NroComprobante  { get; set; } = "";
        public string Ctg             { get; set; } = "";
        public string PrimeraVezVisto { get; set; } = "";
        public string UltimaVezVisto  { get; set; } = "";
        public string FecUpd          { get; set; } = "";
        public bool   Anulado         { get; set; }
        public string Cambios         { get; set; } = "";
        public int?   CantidadCorridas { get; set; }

        public RegistroDisplayItem() { }

        public RegistroDisplayItem(MlogisRegistro r)
        {
            Id              = r.Id             ?? "";
            NroComprobante  = r.NroComprobante ?? "";
            Ctg             = r.Ctg            ?? "";
            PrimeraVezVisto = r.PrimeraVezVisto.ToString("HH:mm");
            UltimaVezVisto  = r.UltimaVezVisto.ToString("HH:mm");
            FecUpd          = DateTime.TryParseExact(r.FecUpd, "MM/dd/yyyy", CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out var fecUpdDt)
                                ? fecUpdDt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                                : (r.FecUpd ?? "");
            Anulado         = r.Anulado;
            Cambios         = r.CambiosDetectados?.Count > 0
                ? string.Join(", ", r.CambiosDetectados.Select(c => c.Campo))
                : "";
        }
    }
}
