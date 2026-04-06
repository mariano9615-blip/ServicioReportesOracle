using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.ServiceProcess;
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
        private DispatcherTimer _serviceStatusTimer;
        private bool _disposed;

        // WS Status
        private SolidColorBrush _wsColor;
        private string _wsStatusText;
        private string _wsSubtext;
        private int _wsCaidasHoy;
        private string _wsDetalleError;
        private bool _wsHasErrorDetail;

        // Service Status
        private SolidColorBrush _serviceStatusColor;
        private string _serviceStatusText;
        private string _serviceStatusSubtext;

        // Last Run
        private string _lastRunBadge;
        private SolidColorBrush _lastRunBadgeColor;
        private string _lastRunTime;
        private int _lastRunTotalIds;
        private string _lastRunAgoText;
        private DateTime? _lastRunDateTime;
        private int _lastRunNuevos;
        private int _lastRunActualizados;
        private int _lastRunAnulados;
        private int _lastRunSinCambios;

        // Pending
        private int _pendientesCount;
        private SolidColorBrush _pendientesColor;
        private DateTime? _oldestPendienteDate;
        private string _oldestPendienteText;
        private bool _isPendientesPanelOpen;

        // Alerts Today
        private int _alertasCasoA;
        private int _alertasCasoB;
        private int _alertasAnulados;
        private int _alertasTareas;
        private int _alertasHealthCheck;
        private double _barraCasoAWidth;
        private double _barraCasoBWidth;
        private double _barraAnuladosWidth;
        private double _barraTareasWidth;
        private double _barraHealthCheckWidth;
        private bool _hasAlertasToday;
        private string _alertasCasoALastText;
        private string _alertasCasoBLastText;
        private string _alertasAnuladosLastText;

        public ObservableCollection<CorridaDashboardItem> CorridasHoy { get; } = new ObservableCollection<CorridaDashboardItem>();
        public ObservableCollection<PendienteDashboardItem> PendientesDetalle { get; } = new ObservableCollection<PendienteDashboardItem>();

        public ICommand RefreshCommand { get; }
        public ICommand NavigateToServiceControlCommand { get; }
        public ICommand NavigateToMlogisHistorialCommand { get; }
        public ICommand NavigateToAlertasCommand { get; }
        public ICommand TogglePendientesPanelCommand { get; }
        public ICommand ClosePendientesPanelCommand { get; }

        public DashboardViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _logsDir = Path.GetFullPath(Path.Combine(basePath, @"..\ServicioReportesOracle\Logs\json"));
            _wsEstadoPath = Path.Combine(_logsDir, "ws_estado.json");
            _historialPath = Path.Combine(_logsDir, "mlogis_historial.json");
            _historialAyerPath = Path.Combine(_logsDir, "mlogis_historial_ayer.json");
            _pendientesPath = Path.Combine(_logsDir, "comparaciones_pendientes.json");
            _alertasEnviadasPath = Path.Combine(_logsDir, "alertas_smtp_enviadas.json");

            RefreshCommand = new RelayCommand(_ => _ = CargarAsync());
            NavigateToServiceControlCommand = new RelayCommand(_ => EjecutarNavegacion(m => m.NavServiceCommand));
            NavigateToMlogisHistorialCommand = new RelayCommand(_ => EjecutarNavegacion(m => m.NavMlogisHistorialCommand));
            NavigateToAlertasCommand = new RelayCommand(_ => EjecutarNavegacion(m => m.NavAlertasCommand));
            TogglePendientesPanelCommand = new RelayCommand(_ => IsPendientesPanelOpen = !IsPendientesPanelOpen);
            ClosePendientesPanelCommand = new RelayCommand(_ => IsPendientesPanelOpen = false);

            PendientesDetalle.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasPendientesDetalle));
                OnPropertyChanged(nameof(HasNoPendientesDetalle));
            };

            ConfigurarWatcher();
            ConfigurarTimer();

            RefreshServiceStatus();
            _ = CargarAsync();
        }

        #region Properties

        public SolidColorBrush WsColor { get => _wsColor; set { _wsColor = value; OnPropertyChanged(); } }
        public string WsStatusText { get => _wsStatusText; set { _wsStatusText = value; OnPropertyChanged(); } }
        public string WsSubtext { get => _wsSubtext; set { _wsSubtext = value; OnPropertyChanged(); } }
        public int WsCaidasHoy { get => _wsCaidasHoy; set { _wsCaidasHoy = value; OnPropertyChanged(); } }
        public string WsDetalleError { get => _wsDetalleError; set { _wsDetalleError = value; OnPropertyChanged(); } }
        public bool WsHasErrorDetail { get => _wsHasErrorDetail; set { _wsHasErrorDetail = value; OnPropertyChanged(); } }

        public SolidColorBrush ServiceStatusColor { get => _serviceStatusColor; set { _serviceStatusColor = value; OnPropertyChanged(); } }
        public string ServiceStatusText { get => _serviceStatusText; set { _serviceStatusText = value; OnPropertyChanged(); } }
        public string ServiceStatusSubtext { get => _serviceStatusSubtext; set { _serviceStatusSubtext = value; OnPropertyChanged(); } }

        public string LastRunBadge { get => _lastRunBadge; set { _lastRunBadge = value; OnPropertyChanged(); } }
        public SolidColorBrush LastRunBadgeColor { get => _lastRunBadgeColor; set { _lastRunBadgeColor = value; OnPropertyChanged(); } }
        public string LastRunTime { get => _lastRunTime; set { _lastRunTime = value; OnPropertyChanged(); } }
        public int LastRunTotalIds { get => _lastRunTotalIds; set { _lastRunTotalIds = value; OnPropertyChanged(); } }
        public string LastRunAgoText { get => _lastRunAgoText; set { _lastRunAgoText = value; OnPropertyChanged(); } }
        public int LastRunNuevos { get => _lastRunNuevos; set { _lastRunNuevos = value; OnPropertyChanged(); } }
        public int LastRunActualizados { get => _lastRunActualizados; set { _lastRunActualizados = value; OnPropertyChanged(); } }
        public int LastRunAnulados { get => _lastRunAnulados; set { _lastRunAnulados = value; OnPropertyChanged(); } }
        public int LastRunSinCambios { get => _lastRunSinCambios; set { _lastRunSinCambios = value; OnPropertyChanged(); } }

        public int PendientesCount { get => _pendientesCount; set { _pendientesCount = value; OnPropertyChanged(); } }
        public SolidColorBrush PendientesColor { get => _pendientesColor; set { _pendientesColor = value; OnPropertyChanged(); } }
        public string OldestPendienteText { get => _oldestPendienteText; set { _oldestPendienteText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasOldestPendiente)); } }
        public bool HasOldestPendiente => !string.IsNullOrEmpty(OldestPendienteText);
        public bool IsPendientesPanelOpen { get => _isPendientesPanelOpen; set { _isPendientesPanelOpen = value; OnPropertyChanged(); } }
        public bool HasPendientesDetalle => PendientesDetalle.Count > 0;
        public bool HasNoPendientesDetalle => PendientesDetalle.Count == 0;

        public int AlertasCasoA { get => _alertasCasoA; set { _alertasCasoA = value; OnPropertyChanged(); } }
        public int AlertasCasoB { get => _alertasCasoB; set { _alertasCasoB = value; OnPropertyChanged(); } }
        public int AlertasAnulados { get => _alertasAnulados; set { _alertasAnulados = value; OnPropertyChanged(); } }
        public int AlertasTareas { get => _alertasTareas; set { _alertasTareas = value; OnPropertyChanged(); } }
        public int AlertasHealthCheck { get => _alertasHealthCheck; set { _alertasHealthCheck = value; OnPropertyChanged(); } }
        public double BarraCasoAWidth { get => _barraCasoAWidth; set { _barraCasoAWidth = value; OnPropertyChanged(); } }
        public double BarraCasoBWidth { get => _barraCasoBWidth; set { _barraCasoBWidth = value; OnPropertyChanged(); } }
        public double BarraAnuladosWidth { get => _barraAnuladosWidth; set { _barraAnuladosWidth = value; OnPropertyChanged(); } }
        public double BarraTareasWidth { get => _barraTareasWidth; set { _barraTareasWidth = value; OnPropertyChanged(); } }
        public double BarraHealthCheckWidth { get => _barraHealthCheckWidth; set { _barraHealthCheckWidth = value; OnPropertyChanged(); } }
        public bool HasAlertasToday { get => _hasAlertasToday; set { _hasAlertasToday = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoAlertasToday)); } }
        public bool HasNoAlertasToday => !HasAlertasToday;
        
        public string AlertasCasoALastText { get => _alertasCasoALastText; set { _alertasCasoALastText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAlertasCasoALastText)); } }
        public string AlertasCasoBLastText { get => _alertasCasoBLastText; set { _alertasCasoBLastText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAlertasCasoBLastText)); } }
        public string AlertasAnuladosLastText { get => _alertasAnuladosLastText; set { _alertasAnuladosLastText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAlertasAnuladosLastText)); } }
        
        public bool HasAlertasCasoALastText => !string.IsNullOrEmpty(AlertasCasoALastText);
        public bool HasAlertasCasoBLastText => !string.IsNullOrEmpty(AlertasCasoBLastText);
        public bool HasAlertasAnuladosLastText => !string.IsNullOrEmpty(AlertasAnuladosLastText);

        public bool HasCorridasHoy => CorridasHoy.Count > 0;
        public bool HasNoCorridasHoy => CorridasHoy.Count == 0;

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

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    ActualizarTimeAgoTimer();
                    ActualizarWidthsBarras();
                    OnPropertyChanged(nameof(HasCorridasHoy));
                    OnPropertyChanged(nameof(HasNoCorridasHoy));
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
                    int caidasHoy = ws["caidas_hoy"]?.ToObject<int>() ?? 0;
                    string ultimaVezCaido = ws["ultima_vez_caido"]?.ToString();
                    string detalleError = ws["detalle_error"]?.ToString();
                    
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        WsCaidasHoy = caidasHoy;

                        if (estado == "caido" || estado == "auth_error")
                        {
                            if (!string.IsNullOrEmpty(detalleError)) {
                                string trunc = detalleError.Length > 80 ? detalleError.Substring(0, 80) + "..." : detalleError;
                                WsDetalleError = trunc;
                                WsHasErrorDetail = true;
                            } else {
                                WsHasErrorDetail = false;
                            }
                        }
                        else
                        {
                            WsHasErrorDetail = false;
                        }

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
                            if (DateTime.TryParse(ultimaVezCaido, out var dt))
                                WsSubtext = $"Última caída: {dt:HH:mm}";
                            else
                                WsSubtext = "Última caída: desconocida";
                        }
                    });
                }
                else
                {
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        WsColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                        WsStatusText = "-";
                        WsSubtext = "Sin datos";
                        WsCaidasHoy = 0;
                        WsHasErrorDetail = false;
                    });
                }
            }
            catch
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    WsColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                    WsStatusText = "Error al leer";
                    WsSubtext = "-";
                    WsHasErrorDetail = false;
                });
            }
        }

        private void CargarPendientes()
        {
            try
            {
                var detallePendientes = new List<PendienteDashboardItem>();

                if (File.Exists(_pendientesPath))
                {
                    string json = LeerArchivoSeguro(_pendientesPath);
                    var jobj = JObject.Parse(json);
                    var pendientesArr = jobj["pendientes"] as JArray;
                    int pCount = pendientesArr?.Count ?? 0;

                    DateTime? oldest = null;
                    if (pendientesArr != null)
                    {
                        foreach (var p in pendientesArr)
                        {
                            // Robust parsing (v7.3.7 - Aligned with Core v7.3.6)
                            DateTime? dtNullable = p.Value<DateTime?>("primera_vez_visto");
                            if (dtNullable.HasValue && dtNullable.Value > DateTime.MinValue)
                            {
                                DateTime localDt = dtNullable.Value;
                                if (localDt.Kind == DateTimeKind.Utc)
                                    localDt = localDt.ToLocalTime();
                                    
                                if (oldest == null || localDt < oldest.Value) 
                                    oldest = localDt;

                                var item = new PendienteDashboardItem
                                {
                                    Id = p["id"]?.ToString() ?? "-",
                                    Nrocomprobante = p["nrocomprobante"]?.ToString() ?? "-",
                                    PrimeraVezVistoDate = localDt,
                                    PrimeraVezVistoText = localDt.ToString("HH:mm dd/MM"),
                                    CorridaOrigen = (p["corrida_origen"]?.ToString() ?? "-").ToUpperInvariant()
                                };

                                item.EsperandoHace = CalcularEsperandoHace(item.PrimeraVezVistoDate);
                                detallePendientes.Add(item);
                            }
                        }
                    }

                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        PendientesCount = pCount;
                        _oldestPendienteDate = oldest;
                        
                        if (PendientesCount > 0)
                        {
                            PendientesColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); // Amarillo
                        }
                        else
                        {
                            PendientesColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")); // Verde
                        }

                        PendientesDetalle.Clear();
                        foreach (var item in detallePendientes.OrderBy(p => p.PrimeraVezVistoDate ?? DateTime.MaxValue))
                        {
                            PendientesDetalle.Add(item);
                        }
                    });
                }
                else
                {
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        PendientesCount = 0;
                        PendientesColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                        _oldestPendienteDate = null;
                        PendientesDetalle.Clear();
                    });
                }
            }
            catch
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    PendientesCount = 0;
                    PendientesColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                    _oldestPendienteDate = null;
                    PendientesDetalle.Clear();
                });
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

                var corridasHoyRaw = corridas.Where(c => c.FechaEjecucion.Date == DateTime.Today)
                                             .OrderByDescending(c => c.FechaEjecucion)
                                             .ToList();

                int totalAnuladosHoy = 0;
                
                DateTime? lastAnuladoDate = null;
                string lastAnuladoId = null;

                Application.Current?.Dispatcher.InvokeAsync(() =>
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
                            
                            var anuladosEnEstaCorrida = c.Registros.Where(r => r.Anulado).ToList();
                            if (anuladosEnEstaCorrida.Count > 0)
                            {
                                if (lastAnuladoDate == null || c.FechaEjecucion > lastAnuladoDate)
                                {
                                    lastAnuladoDate = c.FechaEjecucion;
                                    lastAnuladoId = anuladosEnEstaCorrida.Last().Id;
                                }
                            }
                        }

                        if (CorridasHoy.Count > 0) {
                            var firstItem = CorridasHoy[0];
                            LastRunNuevos = firstItem.NNuevos;
                            LastRunActualizados = firstItem.NActualizados;
                            LastRunAnulados = firstItem.NAnulados;
                            LastRunSinCambios = firstItem.NSinCambios;
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
                        LastRunNuevos = 0;
                        LastRunActualizados = 0;
                        LastRunAnulados = 0;
                        LastRunSinCambios = 0;
                    }

                    AlertasAnulados = totalAnuladosHoy;
                    
                    if (lastAnuladoDate.HasValue) {
                        AlertasAnuladosLastText = $"Último: {lastAnuladoId} — {lastAnuladoDate.Value:HH:mm}";
                    } else {
                        AlertasAnuladosLastText = "";
                    }
                });
            }
            catch
            {
                Application.Current?.Dispatcher.InvokeAsync(() => 
                {
                    CorridasHoy.Clear();
                    AlertasAnulados = 0;
                    _lastRunDateTime = null;
                    AlertasAnuladosLastText = "";
                });
            }
        }

        private void CargarAlertas()
        {
            try
            {
                int ca = 0;
                int cb = 0;
                int ct = 0;
                int ch = 0;

                DateTime? lastCasoA = null;
                DateTime? lastCasoB = null;
                string idCasoA = null;
                string idCasoB = null;

                if (File.Exists(_alertasEnviadasPath))
                {
                    string json = LeerArchivoSeguro(_alertasEnviadasPath);
                    var obj = JObject.Parse(json);
                    var arr = obj["alertas"] as JArray;

                    if (arr != null)
                    {
                        foreach (var token in arr)
                        {
                            // v7.3.9 — Parseo robusto (ISO 8601) + Manejo UTC por seguridad
                            DateTime? dtNullable = token.Value<DateTime?>("timestamp");
                            if (dtNullable.HasValue)
                            {
                                DateTime dt = dtNullable.Value;
                                if (dt.Kind == DateTimeKind.Utc)
                                    dt = dt.ToLocalTime();

                                if (dt.Date == DateTime.Today)
                                {
                                    string tipo = token["tipo"]?.ToString()?.ToLowerInvariant();
                                    string id = token["id_referencia"]?.ToString();

                                    if (tipo == "oracle_caso_a") {
                                        ca++;
                                        if (lastCasoA == null || dt > lastCasoA) { lastCasoA = dt; idCasoA = id; }
                                    }
                                    else if (tipo == "oracle_caso_b") {
                                        cb++;
                                        if (lastCasoB == null || dt > lastCasoB) { lastCasoB = dt; idCasoB = id; }
                                    }
                                    else if (tipo == "tarea_sql") {
                                        ct++;
                                    }
                                    else if (tipo == "ws_caido" || tipo == "ws_recuperado") {
                                        ch++;
                                    }
                                }
                            }
                        }
                    }
                }

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    AlertasCasoA = ca;
                    AlertasCasoB = cb;
                    AlertasTareas = ct;
                    AlertasHealthCheck = ch;

                    if (lastCasoA.HasValue) AlertasCasoALastText = $"Último: {idCasoA} — {lastCasoA.Value:HH:mm}";
                    else AlertasCasoALastText = "";

                    if (lastCasoB.HasValue) AlertasCasoBLastText = $"Último: {idCasoB} — {lastCasoB.Value:HH:mm}";
                    else AlertasCasoBLastText = "";
                });
            }
            catch
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    AlertasCasoA = 0;
                    AlertasCasoB = 0;
                    AlertasTareas = 0;
                    AlertasHealthCheck = 0;
                    AlertasCasoALastText = "";
                    AlertasCasoBLastText = "";
                });
            }
        }

        private void ActualizarWidthsBarras()
        {
            HasAlertasToday = AlertasCasoA > 0 || AlertasCasoB > 0 || AlertasAnulados > 0 || AlertasTareas > 0 || AlertasHealthCheck > 0;
            if (!HasAlertasToday)
            {
                BarraCasoAWidth = 0;
                BarraCasoBWidth = 0;
                BarraAnuladosWidth = 0;
                BarraTareasWidth = 0;
                BarraHealthCheckWidth = 0;
                return;
            }

            BarraCasoAWidth = AlertasCasoA == 0 ? 0 : Math.Min(200.0, Math.Max(8.0, AlertasCasoA * 40.0));
            BarraCasoBWidth = AlertasCasoB == 0 ? 0 : Math.Min(200.0, Math.Max(8.0, AlertasCasoB * 40.0));
            BarraAnuladosWidth = AlertasAnulados == 0 ? 0 : Math.Min(200.0, Math.Max(8.0, AlertasAnulados * 40.0));
            BarraTareasWidth = AlertasTareas == 0 ? 0 : Math.Min(200.0, Math.Max(8.0, AlertasTareas * 40.0));
            BarraHealthCheckWidth = AlertasHealthCheck == 0 ? 0 : Math.Min(200.0, Math.Max(8.0, AlertasHealthCheck * 40.0));
        }

        private void ActualizarTimeAgoTimer()
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

            if (_oldestPendienteDate.HasValue && _oldestPendienteDate.Value > DateTime.MinValue)
            {
                TimeSpan diff = DateTime.Now - _oldestPendienteDate.Value;
                int minPendiente = (int)Math.Max(0, diff.TotalMinutes);
                
                // Si el cálculo da un valor imposible (ej: > 30 días), algo falló en los datos persistidos o parsing
                if (minPendiente > 43200) // 30 días
                {
                    OldestPendienteText = "Más antiguo: +30 días";
                }
                else
                {
                    OldestPendienteText = $"Más antiguo: hace {minPendiente} min";
                }
            }
            else
            {
                OldestPendienteText = "";
            }

            ActualizarEsperandoHacePendientes();
        }

        private void ActualizarEsperandoHacePendientes()
        {
            foreach (var item in PendientesDetalle)
            {
                item.EsperandoHace = CalcularEsperandoHace(item.PrimeraVezVistoDate);
            }
        }

        private string CalcularEsperandoHace(DateTime? primeraVezVisto)
        {
            if (!primeraVezVisto.HasValue)
            {
                return "-";
            }

            int totalMin = (int)(DateTime.Now - primeraVezVisto.Value).TotalMinutes;
            if (totalMin < 0) totalMin = 0;
            int horas = totalMin / 60;
            int minutos = totalMin % 60;
            return $"{horas}h {minutos}m";
        }

        private void EjecutarNavegacion(Func<MainViewModel, ICommand> selector)
        {
            var main = MainViewModel.Instance;
            if (main == null || selector == null)
            {
                return;
            }

            var command = selector(main);
            if (command == null)
            {
                return;
            }

            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }

        private void RefreshServiceStatus()
        {
            try
            {
                using (var sc = new ServiceController("ServicioReportesOracle"))
                {
                    var status = sc.Status;

                    string statusText;
                    Color color;

                    switch (status)
                    {
                        case ServiceControllerStatus.Running:
                            statusText = "En ejecución";
                            color = (Color)ColorConverter.ConvertFromString("#4CAF50");
                            break;

                        case ServiceControllerStatus.Stopped:
                            statusText = "Detenido";
                            color = (Color)ColorConverter.ConvertFromString("#F44336");
                            break;

                        case ServiceControllerStatus.StartPending:
                            statusText = "Iniciando...";
                            color = (Color)ColorConverter.ConvertFromString("#F59E0B");
                            break;

                        case ServiceControllerStatus.StopPending:
                            statusText = "Deteniéndose...";
                            color = (Color)ColorConverter.ConvertFromString("#F59E0B");
                            break;

                        case ServiceControllerStatus.Paused:
                            statusText = "Pausado";
                            color = (Color)ColorConverter.ConvertFromString("#F59E0B");
                            break;

                        default:
                            statusText = status.ToString();
                            color = (Color)ColorConverter.ConvertFromString("#F59E0B");
                            break;
                    }

                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        ServiceStatusText = statusText;
                        ServiceStatusColor = new SolidColorBrush(color);
                        ServiceStatusSubtext = "ServicioReportesOracle";
                    });
                }
            }
            catch (InvalidOperationException)
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    ServiceStatusText = "No disponible";
                    ServiceStatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                    ServiceStatusSubtext = "Servicio no encontrado";
                });
            }
            catch
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    ServiceStatusText = "No disponible";
                    ServiceStatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                    ServiceStatusSubtext = "Error al leer estado";
                });
            }
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
                lowerInfo == "alertas_smtp_enviadas.json" ||
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

            _serviceStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _serviceStatusTimer.Tick += (s, e) => RefreshServiceStatus();
            _serviceStatusTimer.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _watcher?.Dispose(); } catch { }
            try { _debounceTimer?.Dispose(); } catch { }
            try { _timeAgoTimer?.Stop(); } catch { }
            try { _serviceStatusTimer?.Stop(); } catch { }
            _watcher = null;
            _debounceTimer = null;
            _timeAgoTimer = null;
            _serviceStatusTimer = null;
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
        public int NSinCambios { get; }

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
                    if (Math.Abs((r.PrimeraVezVisto - c.FechaEjecucion).TotalMinutes) < 5)
                        nuevos++;
                }
            }

            NNuevos = nuevos;
            NActualizados = actualizados;
            NAnulados = anulados;
            NSinCambios = NTotal - nuevos - actualizados - anulados;
        }
    }

    public class PendienteDashboardItem : INotifyPropertyChanged
    {
        private string _esperandoHace;

        public string Id { get; set; }
        public string Nrocomprobante { get; set; }
        public string PrimeraVezVistoText { get; set; }
        public DateTime? PrimeraVezVistoDate { get; set; }
        public string CorridaOrigen { get; set; }

        public string EsperandoHace
        {
            get => _esperandoHace;
            set
            {
                _esperandoHace = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
