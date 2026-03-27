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
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ServicioOracleReportes;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class DashboardViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly string _logsDir;
        private readonly string _wsEstadoPath;
        private readonly string _historialPath;
        private readonly string _historialAyerPath;
        private readonly string _pendientesPath;
        private readonly string _alertasEnviadasPath;

        private FileSystemWatcher _watcher;
        private Timer _debounceTimer;
        private const int DebounceMs = 2000;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private DispatcherTimer _timeAgoTimer;
        private bool _disposed;

        // WS Status
        private SolidColorBrush _wsColor;
        private string _wsStatusText;
        private string _wsSubtext;
        private int _wsCaidasHoy;

        // Last Run
        private string _lastRunBadge;
        private SolidColorBrush _lastRunBadgeColor;
        private string _lastRunTime;
        private int _lastRunTotalIds;
        private string _lastRunAgoText;
        private DateTime? _lastRunDateTime;

        // Pending
        private int _pendientesCount;
        private SolidColorBrush _pendientesColor;

        // Alerts Today
        private int _alertasCasoA;
        private int _alertasCasoB;
        private int _alertasAnulados;
        private double _barraCasoAWidth;
        private double _barraCasoBWidth;
        private double _barraAnuladosWidth;
        private bool _hasAlertasToday;

        public ObservableCollection<CorridaDashboardItem> CorridasHoy { get; } = new ObservableCollection<CorridaDashboardItem>();

        public ICommand RefreshCommand { get; }

        public DashboardViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _logsDir = Path.GetFullPath(Path.Combine(basePath, @"..\ServicioReportesOracle\Logs"));
            _wsEstadoPath = Path.Combine(_logsDir, "ws_estado.json");
            _historialPath = Path.Combine(_logsDir, "mlogis_historial.json");
            _historialAyerPath = Path.Combine(_logsDir, "mlogis_historial_ayer.json");
            _pendientesPath = Path.Combine(_logsDir, "comparaciones_pendientes.json");
            _alertasEnviadasPath = Path.Combine(_logsDir, "alertas_oracle_enviadas.json");

            RefreshCommand = new RelayCommand(_ => _ = CargarAsync());

            ConfigurarWatcher();
            ConfigurarTimer();

            _ = CargarAsync();
        }

        #region Properties

        public SolidColorBrush WsColor { get => _wsColor; set { _wsColor = value; OnPropertyChanged(); } }
        public string WsStatusText { get => _wsStatusText; set { _wsStatusText = value; OnPropertyChanged(); } }
        public string WsSubtext { get => _wsSubtext; set { _wsSubtext = value; OnPropertyChanged(); } }
        public int WsCaidasHoy { get => _wsCaidasHoy; set { _wsCaidasHoy = value; OnPropertyChanged(); } }

        public string LastRunBadge { get => _lastRunBadge; set { _lastRunBadge = value; OnPropertyChanged(); } }
        public SolidColorBrush LastRunBadgeColor { get => _lastRunBadgeColor; set { _lastRunBadgeColor = value; OnPropertyChanged(); } }
        public string LastRunTime { get => _lastRunTime; set { _lastRunTime = value; OnPropertyChanged(); } }
        public int LastRunTotalIds { get => _lastRunTotalIds; set { _lastRunTotalIds = value; OnPropertyChanged(); } }
        public string LastRunAgoText { get => _lastRunAgoText; set { _lastRunAgoText = value; OnPropertyChanged(); } }

        public int PendientesCount { get => _pendientesCount; set { _pendientesCount = value; OnPropertyChanged(); } }
        public SolidColorBrush PendientesColor { get => _pendientesColor; set { _pendientesColor = value; OnPropertyChanged(); } }

        public int AlertasCasoA { get => _alertasCasoA; set { _alertasCasoA = value; OnPropertyChanged(); } }
        public int AlertasCasoB { get => _alertasCasoB; set { _alertasCasoB = value; OnPropertyChanged(); } }
        public int AlertasAnulados { get => _alertasAnulados; set { _alertasAnulados = value; OnPropertyChanged(); } }
        public double BarraCasoAWidth { get => _barraCasoAWidth; set { _barraCasoAWidth = value; OnPropertyChanged(); } }
        public double BarraCasoBWidth { get => _barraCasoBWidth; set { _barraCasoBWidth = value; OnPropertyChanged(); } }
        public double BarraAnuladosWidth { get => _barraAnuladosWidth; set { _barraAnuladosWidth = value; OnPropertyChanged(); } }
        public bool HasAlertasToday { get => _hasAlertasToday; set { _hasAlertasToday = value; OnPropertyChanged(); } }

        public bool HasCorridasHoy => CorridasHoy.Count > 0;

        #endregion

        private async Task CargarAsync()
        {
            if (!await _refreshLock.WaitAsync(0)) return;
            try
            {
                await Task.Run(() =>
                {
                    CargarWsEstado();
                    CargarPendientes();
                    CargarHistorial();
                    CargarAlertas(); // Requiere info del historial (corridas) para anulados o lee de alertas_enviadas
                });

                ActualizarTimeAgoTimer();
                ActualizarWidthsBarras();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(HasCorridasHoy));
                });
            }
            catch { /* absorber */ }
            finally
            {
                _refreshLock.Release();
            }
        }

        private void CargarWsEstado()
        {
            try
            {
                if (File.Exists(_wsEstadoPath))
                {
                    string json = LeerArchivoSeguro(_wsEstadoPath);
                    var ws = JObject.Parse(json);
                    string estado = ws["ultimo_estado"]?.ToString();
                    WsCaidasHoy = ws["caidas_hoy"]?.ToObject<int>() ?? 0;
                    
                    if (estado == "ok")
                    {
                        WsColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                        WsStatusText = "Operativo";
                    }
                    else if (estado == "caido")
                    {
                        WsColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                        WsStatusText = "Caído";
                    }
                    else if (estado == "auth_error")
                    {
                        WsColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                        WsStatusText = "Error de autenticación";
                    }
                    else
                    {
                        WsColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                        WsStatusText = "Desconocido";
                    }

                    if (WsCaidasHoy == 0)
                    {
                        WsSubtext = "Sin caídas registradas hoy";
                    }
                    else
                    {
                        string ultimaVezCaido = ws["ultima_vez_caido"]?.ToString();
                        if (DateTime.TryParse(ultimaVezCaido, out var dt))
                            WsSubtext = $"Última caída: {dt:HH:mm}";
                        else
                            WsSubtext = "Última caída: desconocida";
                    }
                }
                else
                {
                    WsColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                    WsStatusText = "-";
                    WsSubtext = "Sin datos";
                    WsCaidasHoy = 0;
                }
            }
            catch
            {
                WsColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                WsStatusText = "Error al leer";
                WsSubtext = "-";
            }
        }

        private void CargarPendientes()
        {
            try
            {
                if (File.Exists(_pendientesPath))
                {
                    string json = LeerArchivoSeguro(_pendientesPath);
                    var jobj = JObject.Parse(json);
                    var pendientesArr = jobj["pendientes"] as JArray;
                    PendientesCount = pendientesArr?.Count ?? 0;
                }
                else
                {
                    PendientesCount = 0;
                }

                if (PendientesCount > 0)
                    PendientesColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); // Amarillo
                else
                    PendientesColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")); // Verde
            }
            catch
            {
                PendientesCount = 0;
                PendientesColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
            }
        }

        private void CargarHistorial()
        {
            try
            {
                var corridas = new List<MlogisCorrida>();
                if (File.Exists(_historialPath))
                {
                    string json = LeerArchivoSeguro(_historialPath);
                    var parsed = JsonConvert.DeserializeObject<MlogisHistorial>(json);
                    if (parsed?.Corridas != null)
                        corridas.AddRange(parsed.Corridas);
                }

                // Filtrar solo las de hoy (en caso de que aún no se haya rotado si no hubo primera corrida)
                var corridasHoyRaw = corridas.Where(c => c.FechaEjecucion.Date == DateTime.Today)
                                             .OrderByDescending(c => c.FechaEjecucion)
                                             .ToList();

                int totalAnuladosHoy = 0;

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    CorridasHoy.Clear();
                    if (corridasHoyRaw.Count > 0)
                    {
                        _lastRunDateTime = corridasHoyRaw[0].FechaEjecucion;
                        LastRunTime = _lastRunDateTime.Value.ToString("HH:mm");
                        LastRunTotalIds = corridasHoyRaw[0].Registros?.Count ?? 0;
                        string tipoFirst = (corridasHoyRaw[0].Tipo ?? "").ToUpper();
                        LastRunBadge = tipoFirst.Contains("FULL") ? "FULL" : "DELTA";
                        LastRunBadgeColor = LastRunBadge == "FULL" 
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"))  // Azul
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280")); // Gris

                        foreach (var c in corridasHoyRaw)
                        {
                            var item = new CorridaDashboardItem(c);
                            CorridasHoy.Add(item);
                            totalAnuladosHoy += item.NAnulados;
                        }
                    }
                    else
                    {
                        _lastRunDateTime = null;
                        LastRunTime = "-";
                        LastRunTotalIds = 0;
                        LastRunBadge = "-";
                        LastRunAgoText = "Sin corridas registradas hoy";
                        LastRunBadgeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                    }
                });

                // Los anulados detectados de hoy provienen del historial
                AlertasAnulados = totalAnuladosHoy;
            }
            catch
            {
                Application.Current?.Dispatcher.Invoke(() => CorridasHoy.Clear());
                AlertasAnulados = 0;
                _lastRunDateTime = null;
            }
        }

        private void CargarAlertas()
        {
            try
            {
                int ca = 0;
                int cb = 0;

                if (File.Exists(_alertasEnviadasPath))
                {
                    string json = LeerArchivoSeguro(_alertasEnviadasPath);
                    var arr = JArray.Parse(json);
                    
                    foreach (var token in arr)
                    {
                        string timestamp = token["timestamp"]?.ToString();
                        if (DateTime.TryParse(timestamp, out var dt) && dt.Date == DateTime.Today)
                        {
                            string tipo = token["tipo_caso"]?.ToString();
                            if (tipo == "A") ca++;
                            else if (tipo == "B") cb++;
                        }
                    }
                }

                AlertasCasoA = ca;
                AlertasCasoB = cb;
            }
            catch
            {
                AlertasCasoA = 0;
                AlertasCasoB = 0;
            }
        }

        private void ActualizarWidthsBarras()
        {
            HasAlertasToday = AlertasCasoA > 0 || AlertasCasoB > 0 || AlertasAnulados > 0;
            if (!HasAlertasToday)
            {
                BarraCasoAWidth = 0;
                BarraCasoBWidth = 0;
                BarraAnuladosWidth = 0;
                return;
            }

            double maxVal = Math.Max(AlertasCasoA, Math.Max(AlertasCasoB, AlertasAnulados));
            double maxWidth = 300.0;
            
            BarraCasoAWidth = maxVal == 0 ? 0 : (AlertasCasoA / maxVal) * maxWidth;
            BarraCasoBWidth = maxVal == 0 ? 0 : (AlertasCasoB / maxVal) * maxWidth;
            BarraAnuladosWidth = maxVal == 0 ? 0 : (AlertasAnulados / maxVal) * maxWidth;
        }

        private void ActualizarTimeAgoTimer()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_lastRunDateTime.HasValue && _lastRunDateTime.Value.Date == DateTime.Today)
                {
                    int min = (int)(DateTime.Now - _lastRunDateTime.Value).TotalMinutes;
                    if (min < 0) min = 0;
                    LastRunAgoText = $"hace {min} minutos";
                }
                else
                {
                    LastRunAgoText = "Sin corridas registradas hoy";
                }
            });
        }

        private string LeerArchivoSeguro(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
                return reader.ReadToEnd();
        }

        private void ConfigurarWatcher()
        {
            try { _watcher?.Dispose(); } catch { }
            _debounceTimer?.Dispose();
            _watcher = null;
            _debounceTimer = null;

            if (string.IsNullOrEmpty(_logsDir) || !Directory.Exists(_logsDir)) return;

            _debounceTimer = new Timer(_ =>
            {
                if (_disposed) return;
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => _ = CargarAsync()));
            }, null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(_logsDir)
            {
                Filter = "*.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            
            _watcher.Changed += (s, e) => WatcherTrigger(e.Name);
            _watcher.Created += (s, e) => WatcherTrigger(e.Name);
        }

        private void WatcherTrigger(string name)
        {
            if (name == null) return;
            string lowerInfo = name.ToLower();
            if (lowerInfo == "ws_estado.json" || 
                lowerInfo.StartsWith("mlogis_historial") || 
                lowerInfo == "comparaciones_pendientes.json" ||
                lowerInfo == "alertas_oracle_enviadas.json")
            {
                _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
            }
        }

        private void ConfigurarTimer()
        {
            _timeAgoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _timeAgoTimer.Tick += (s, e) => ActualizarTimeAgoTimer();
            _timeAgoTimer.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _watcher?.Dispose(); } catch { }
            try { _debounceTimer?.Dispose(); } catch { }
            try { _timeAgoTimer?.Stop(); } catch { }
            _watcher = null;
            _debounceTimer = null;
            _timeAgoTimer = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CorridaDashboardItem
    {
        public string Hora { get; }
        public string TipoBadge { get; }
        public SolidColorBrush BadgeColor { get; }
        public int NTotal { get; }
        public int NNuevos { get; }
        public int NActualizados { get; }
        public int NAnulados { get; }

        public CorridaDashboardItem(MlogisCorrida c)
        {
            Hora = c.FechaEjecucion.ToString("HH:mm");
            string tipo = (c.Tipo ?? "").ToUpper();
            TipoBadge = tipo.Contains("FULL") ? "FULL" : "DELTA";
            BadgeColor = TipoBadge == "FULL" 
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")) 
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));

            var registros = c.Registros ?? new List<MlogisRegistro>();
            NTotal = registros.Count;
            
            int nuevos = 0, actualizados = 0, anulados = 0;
            foreach (var r in registros)
            {
                if (r.Anulado)
                {
                    anulados++;
                }
                else if (r.CambiosDetectados?.Count > 0)
                {
                    actualizados++;
                }
                else
                {
                    // Es nuevo si se vió por primera vez en esta corrida (margen 5 min)
                    if (Math.Abs((r.PrimeraVezVisto - c.FechaEjecucion).TotalMinutes) < 5)
                        nuevos++;
                }
            }

            NNuevos = nuevos;
            NActualizados = actualizados;
            NAnulados = anulados;
        }
    }
}
