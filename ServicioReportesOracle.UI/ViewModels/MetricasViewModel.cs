using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ServicioOracleReportes;
using ServicioReportesOracle.UI.Models;
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
using System.Windows.Media;

namespace ServicioReportesOracle.UI.ViewModels
{
    /// <summary>Dato de una barra individual en el gráfico de barras.</summary>
    public class BarItem
    {
        /// <summary>Altura en píxeles (máximo = MaxBarHeightPx de BuildBarItems).</summary>
        public double BarHeightPx { get; set; }

        /// <summary>Valor real para el tooltip.</summary>
        public string Tooltip { get; set; }

        /// <summary>Color de la barra (heredado del gráfico).</summary>
        public Brush Fill { get; set; }

        /// <summary>True si el valor supera el percentil 95 (outlier truncado visualmente).</summary>
        public bool IsOutlier { get; set; }
    }

    /// <summary>Punto en serie temporal para gráfico de línea.</summary>
    public class LinePoint
    {
        public DateTime Fecha { get; set; }
        public double Valor { get; set; }
        public string Label { get; set; }
        public string Tooltip { get; set; }
    }

    public class MetricasViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly string _logsDir;
        private readonly string _historialPath;
        private readonly string _historialAyerPath;
        private readonly string _alertasPath;
        private readonly string _historicoMensualPath;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private FileSystemWatcher _watcherHistorial;
        private FileSystemWatcher _watcherAlertas;
        private FileSystemWatcher _watcherMensual;
        private FileSystemWatcher _watcherSettings;
        private Timer _debounceTimer;
        private const int DebounceMs = 2000;
        private bool _disposed;

        // ── Barras IDs ──────────────────────────────────────────────────────────
        private ObservableCollection<BarItem> _idsBarItems = new ObservableCollection<BarItem>();
        private ObservableCollection<BarItem> _duracionBarItems = new ObservableCollection<BarItem>();

        // ── KPIs ────────────────────────────────────────────────────────────────
        private string _corridasHoyVsAyer = "0 hoy / 0 ayer";
        private int _alertasHoy;
        private int _fullHoy;
        private int _deltaHoy;
        private double _fullPorcentaje;
        private double _deltaPorcentaje;
        private double _fullBarWidth;
        private double _deltaBarWidth;
        private string _idsBarLabel    = "Sin datos";
        private string _duracionBarLabel = "Sin datos";
        private bool _tieneDatosCore;
        private bool _tieneDatosUI;

        // Series temporales (últimos 30 días)
        private ObservableCollection<LinePoint> _tendenciaIds      = new ObservableCollection<LinePoint>();
        private ObservableCollection<LinePoint> _tendenciaCorridas = new ObservableCollection<LinePoint>();

        // KPIs mensuales
        private int _promedioDiarioIds;
        private int _totalMesIds;
        private int _maxDiaIds;
        private int _diasConAlertas;
        private double _promedioCorridasDia;
        private string _tituloMetricas = "Métricas (48 horas)";

        // Carga histórica
        private bool _estaCargandoHistorico;
        private int _progresoCargarHistorico;
        private string _textoProgresoHistorico = "Listo para cargar";
        private bool _mostrarBotonCargarHistorico = true;

        public ICommand RefreshCommand { get; }

        // ── Propiedades bindables ────────────────────────────────────────────────
        public ObservableCollection<BarItem> IdsBarItems
        {
            get => _idsBarItems;
            set { _idsBarItems = value; OnPropertyChanged(); }
        }

        public ObservableCollection<BarItem> DuracionBarItems
        {
            get => _duracionBarItems;
            set { _duracionBarItems = value; OnPropertyChanged(); }
        }

        public string CorridasHoyVsAyer
        {
            get => _corridasHoyVsAyer;
            set { _corridasHoyVsAyer = value; OnPropertyChanged(); }
        }

        public int AlertasHoy
        {
            get => _alertasHoy;
            set
            {
                _alertasHoy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorAlertas));
            }
        }

        public Brush ColorAlertas => AlertasHoy == 0
            ? ResolveBrush("SuccessBrush", "OnSurfaceBrush")
            : ResolveBrush("WarningBrush", "OnSurfaceBrush");

        public int FullHoy
        {
            get => _fullHoy;
            set { _fullHoy = value; OnPropertyChanged(); }
        }

        public int DeltaHoy
        {
            get => _deltaHoy;
            set { _deltaHoy = value; OnPropertyChanged(); }
        }

        public double FullPorcentaje
        {
            get => _fullPorcentaje;
            set { _fullPorcentaje = value; OnPropertyChanged(); }
        }

        public double DeltaPorcentaje
        {
            get => _deltaPorcentaje;
            set { _deltaPorcentaje = value; OnPropertyChanged(); }
        }

        public double FullBarWidth
        {
            get => _fullBarWidth;
            set { _fullBarWidth = value; OnPropertyChanged(); }
        }

        public double DeltaBarWidth
        {
            get => _deltaBarWidth;
            set { _deltaBarWidth = value; OnPropertyChanged(); }
        }

        public string IdsBarLabel
        {
            get => _idsBarLabel;
            set { _idsBarLabel = value; OnPropertyChanged(); }
        }

        public string DuracionBarLabel
        {
            get => _duracionBarLabel;
            set { _duracionBarLabel = value; OnPropertyChanged(); }
        }

        public bool TieneDatosCore
        {
            get => _tieneDatosCore;
            set { _tieneDatosCore = value; OnPropertyChanged(); }
        }

        public bool TieneDatosUI
        {
            get => _tieneDatosUI;
            set { _tieneDatosUI = value; OnPropertyChanged(); }
        }

        public ObservableCollection<LinePoint> TendenciaIds
        {
            get => _tendenciaIds;
            set { _tendenciaIds = value; OnPropertyChanged(); }
        }

        public ObservableCollection<LinePoint> TendenciaCorridas
        {
            get => _tendenciaCorridas;
            set { _tendenciaCorridas = value; OnPropertyChanged(); }
        }

        public int PromedioDiarioIds
        {
            get => _promedioDiarioIds;
            set { _promedioDiarioIds = value; OnPropertyChanged(); }
        }

        public int TotalMesIds
        {
            get => _totalMesIds;
            set { _totalMesIds = value; OnPropertyChanged(); }
        }

        public int MaxDiaIds
        {
            get => _maxDiaIds;
            set { _maxDiaIds = value; OnPropertyChanged(); }
        }

        public int DiasConAlertas
        {
            get => _diasConAlertas;
            set { _diasConAlertas = value; OnPropertyChanged(); }
        }

        public double PromedioCorridasDia
        {
            get => _promedioCorridasDia;
            set { _promedioCorridasDia = value; OnPropertyChanged(); }
        }

        private int _minDiaTendencia;
        private int _maxDiaTendencia;

        public int MinDiaTendencia
        {
            get => _minDiaTendencia;
            set { _minDiaTendencia = value; OnPropertyChanged(); }
        }

        public int MaxDiaTendencia
        {
            get => _maxDiaTendencia;
            set { _maxDiaTendencia = value; OnPropertyChanged(); }
        }

        public string TituloMetricas
        {
            get => _tituloMetricas;
            set { _tituloMetricas = value; OnPropertyChanged(); }
        }

        public bool EstaCargandoHistorico
        {
            get => _estaCargandoHistorico;
            set { _estaCargandoHistorico = value; OnPropertyChanged(); }
        }

        public int ProgresoCargarHistorico
        {
            get => _progresoCargarHistorico;
            set { _progresoCargarHistorico = value; OnPropertyChanged(); }
        }

        public string TextoProgresoHistorico
        {
            get => _textoProgresoHistorico;
            set { _textoProgresoHistorico = value; OnPropertyChanged(); }
        }

        public bool MostrarBotonCargarHistorico
        {
            get => _mostrarBotonCargarHistorico;
            set { _mostrarBotonCargarHistorico = value; OnPropertyChanged(); }
        }

        public ICommand CargarHistoricoCommand { get; }

        public MetricasViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _logsDir = Path.GetFullPath(Path.Combine(basePath, @"..\ServicioReportesOracle\Logs\json"));
            _historialPath     = Path.Combine(_logsDir, "mlogis_historial.json");
            _historialAyerPath = Path.Combine(_logsDir, "mlogis_historial_ayer.json");
            _alertasPath          = Path.Combine(_logsDir, "alertas_oracle_enviadas.json");
            _historicoMensualPath = Path.Combine(_logsDir, "mlogis_historico_mensual.json");

            RefreshCommand = new RelayCommand(_ => _ = CargarAsync());
            CargarHistoricoCommand = new RelayCommand(_ => _ = CargarHistoricoAsync());

            // Leer config UI
            try
            {
                string uiSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui_settings.json");
                if (File.Exists(uiSettingsPath))
                {
                    var settings = JsonConvert.DeserializeObject<UiSettingsModel>(File.ReadAllText(uiSettingsPath));
                    MostrarBotonCargarHistorico = settings?.MostrarBotonCargarHistorico ?? true;
                }
            }
            catch { }

            ConfigurarWatcher();
            _ = CargarAsync();
        }

        internal async Task CargarAsync()
        {
            if (!await _refreshLock.WaitAsync(0)) return;
            try
            {
                var hoy  = await LeerHistorialAsync(_historialPath);
                var ayer = await LeerHistorialAsync(_historialAyerPath);
                var combinadas = hoy.Concat(ayer)
                                    .OrderBy(c => c.FechaEjecucion)
                                    .ToList();

                int skip     = Math.Max(0, combinadas.Count - 20);
                var ultimas20 = combinadas.Skip(skip).ToList();

                var idsSerie      = ultimas20.Select(c => (double)(c.Registros?.Count ?? 0)).ToList();
                var duracionSerie = ultimas20.Select(c => c.DuracionSegundos).ToList();

                int totalHoy    = hoy.Count;
                int totalAyer   = ayer.Count;
                int fullHoy     = hoy.Count(c => (c.Tipo ?? "").IndexOf("FULL", StringComparison.OrdinalIgnoreCase) >= 0);
                int deltaHoy    = hoy.Count - fullHoy;
                int totalTipoHoy = Math.Max(1, fullHoy + deltaHoy);
                double fullPct  = (fullHoy  * 100.0) / totalTipoHoy;
                double deltaPct = 100.0 - fullPct;

                int alertasHoy = await ContarAlertasHoyAsync();

                // ── Cargar métricas mensuales ──────────────────────────────────────
                var metricasMensuales = await LeerHistoricoMensualAsync();

                var tendenciaIds = metricasMensuales
                    .Select(m => new { m.Fecha, m.TotalRegistrosPico, m.TotalCorridas })
                    .Where(m => DateTime.TryParse(m.Fecha, CultureInfo.InvariantCulture,
                                                  DateTimeStyles.None, out _))
                    .OrderBy(m => m.Fecha)
                    .Select(m =>
                    {
                        DateTime.TryParse(m.Fecha, CultureInfo.InvariantCulture,
                                          DateTimeStyles.None, out DateTime dt);
                        return new LinePoint
                        {
                            Fecha   = dt,
                            Valor   = m.TotalRegistrosPico,
                            Label   = dt.ToString("dd/MM"),
                            Tooltip = $"{m.TotalRegistrosPico} IDs pico - {dt:dd/MM/yyyy}"
                        };
                    })
                    .ToList();

                // Normalizar Valor a altura en píxeles para el gráfico (max 140px, min 20%)
                if (tendenciaIds.Count > 0)
                {
                    const double maxPx = 140.0;
                    double maxVal = tendenciaIds.Max(p => p.Valor);
                    double minVal = tendenciaIds.Min(p => p.Valor);
                    if (Math.Abs(maxVal - minVal) < 0.01)
                    {
                        foreach (var pt in tendenciaIds) pt.Valor = maxPx * 0.5;
                    }
                    else
                    {
                        foreach (var pt in tendenciaIds)
                        {
                            double ratio = (pt.Valor - minVal) / (maxVal - minVal);
                            pt.Valor = (ratio * maxPx * 0.8) + (maxPx * 0.2);
                        }
                    }
                }

                var tendenciaCorridas = metricasMensuales
                    .Where(m => DateTime.TryParse(m.Fecha, CultureInfo.InvariantCulture,
                                                  DateTimeStyles.None, out _))
                    .OrderBy(m => m.Fecha)
                    .Select(m =>
                    {
                        DateTime.TryParse(m.Fecha, CultureInfo.InvariantCulture,
                                          DateTimeStyles.None, out DateTime dt);
                        return new LinePoint
                        {
                            Fecha   = dt,
                            Valor   = m.TotalCorridas,
                            Label   = dt.ToString("dd/MM"),
                            Tooltip = $"{m.TotalCorridas} corridas - {dt:dd/MM/yyyy}"
                        };
                    })
                    .ToList();

                // Calcular min/max para escala visual
                int minDia = metricasMensuales.Count > 0
                    ? metricasMensuales.Min(m => m.TotalRegistrosPico)
                    : 0;
                int maxDia = metricasMensuales.Count > 0
                    ? metricasMensuales.Max(m => m.TotalRegistrosPico)
                    : 0;

                int promedioDiarioIds    = metricasMensuales.Count > 0
                    ? (int)metricasMensuales.Average(m => m.TotalRegistrosPico) : 0;
                int totalMesIds          = metricasMensuales.Sum(m => m.TotalRegistrosPico);
                int maxDiaIds            = metricasMensuales.Count > 0
                    ? metricasMensuales.Max(m => m.TotalRegistrosPico) : 0;
                int diasConAlertas       = metricasMensuales.Count(m => m.AlertasOracleEnviadas > 0);
                double promedioCorridasDia = metricasMensuales.Count > 0
                    ? metricasMensuales.Average(m => m.TotalCorridas) : 0;

                string tituloMetricas = metricasMensuales.Count > 0
                    ? $"Métricas ({metricasMensuales.Count} días)"
                    : "Métricas (48 horas)";

                // Resolver brushes (debe hacerse en UI thread)
                Brush primaryBrush = null;
                Brush warningBrush = null;

                await Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    primaryBrush = ResolveBrush("PrimaryBrush", "OnSurfaceBrush");
                    warningBrush = ResolveBrush("WarningBrush", "OnSurfaceBrush");
                });

                var idsBarItems      = BuildBarItems(idsSerie,     primaryBrush, v => $"{v:0} IDs", 82, warningBrush);
                var duracionBarItems = BuildBarItems(duracionSerie, warningBrush, v => $"{v:0.0}s");

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    IdsBarItems     = new ObservableCollection<BarItem>(idsBarItems);
                    DuracionBarItems = new ObservableCollection<BarItem>(duracionBarItems);

                    TieneDatosCore = idsBarItems.Count > 0;
                    TieneDatosUI   = duracionBarItems.Count > 0;

                    CorridasHoyVsAyer = $"{totalHoy} hoy / {totalAyer} ayer";
                    FullHoy     = fullHoy;
                    DeltaHoy    = deltaHoy;
                    FullPorcentaje  = Math.Round(fullPct, 1);
                    DeltaPorcentaje = Math.Round(deltaPct, 1);
                    FullBarWidth    = Math.Round(260 * (fullPct  / 100.0), 2);
                    DeltaBarWidth   = Math.Round(260 * (deltaPct / 100.0), 2);
                    AlertasHoy = alertasHoy;

                    IdsBarLabel = idsSerie.Count == 0
                        ? "Sin corridas en la ventana"
                        : $"Min {idsSerie.Min():0} / Max {idsSerie.Max():0} IDs";

                    DuracionBarLabel = duracionSerie.Count == 0
                        ? "Sin datos de duración"
                        : $"Min {duracionSerie.Min():0.1}s / Max {duracionSerie.Max():0.1}s";

                    // Actualizar propiedades mensuales
                    TendenciaIds       = new ObservableCollection<LinePoint>(tendenciaIds);
                    TendenciaCorridas  = new ObservableCollection<LinePoint>(tendenciaCorridas);
                    PromedioDiarioIds  = promedioDiarioIds;
                    TotalMesIds        = totalMesIds;
                    MaxDiaIds          = maxDiaIds;
                    DiasConAlertas     = diasConAlertas;
                    PromedioCorridasDia = Math.Round(promedioCorridasDia, 1);
                    TituloMetricas     = tituloMetricas;
                    MinDiaTendencia    = minDia;
                    MaxDiaTendencia    = maxDia;
                });
            }
            catch
            {
                // absorber para no propagar al hilo UI
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <summary>
        /// Construye la colección de BarItem con alturas pre-calculadas en píxeles
        /// (la barra del valor máximo siempre ocupa <paramref name="maxHeightPx"/> px).
        /// </summary>
        private static List<BarItem> BuildBarItems(
            IReadOnlyList<double> valores,
            Brush fill,
            Func<double, string> tooltipFmt,
            double maxHeightPx = 82,
            Brush outlierFill = null)
        {
            var result = new List<BarItem>();
            if (valores == null || valores.Count == 0) return result;

            // Calcular P95 para detectar outliers
            var sorted = valores.OrderBy(v => v).ToList();
            int p95Idx = Math.Max(0, (int)Math.Ceiling(sorted.Count * 0.95) - 1);
            double p95 = sorted[p95Idx];
            double min = sorted[0];

            // Si el rango P95-min es cero, usar altura fija
            if (Math.Abs(p95 - min) < 0.01)
            {
                foreach (double v in valores)
                {
                    result.Add(new BarItem
                    {
                        BarHeightPx = maxHeightPx * 0.5,
                        Tooltip     = tooltipFmt(v),
                        Fill        = fill,
                        IsOutlier   = false
                    });
                }
                return result;
            }

            // Calcular alturas proporcionales con mínimo 20%; outliers truncados en P95
            foreach (double v in valores)
            {
                bool isOutlier = v > p95;
                double cap    = isOutlier ? p95 : v;
                double ratio  = (cap - min) / (p95 - min);
                double altura = (ratio * maxHeightPx * 0.8) + (maxHeightPx * 0.2);
                result.Add(new BarItem
                {
                    BarHeightPx = Math.Max(10, altura),
                    Tooltip     = tooltipFmt(v),
                    Fill        = isOutlier ? (outlierFill ?? fill) : fill,
                    IsOutlier   = isOutlier
                });
            }

            return result;
        }

        private async Task<List<MlogisCorrida>> LeerHistorialAsync(string path)
        {
            if (!File.Exists(path)) return new List<MlogisCorrida>();
            try
            {
                string json = await Task.Run(() =>
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                        return sr.ReadToEnd();
                });
                var parsed = JsonConvert.DeserializeObject<MlogisHistorial>(json);
                return parsed?.Corridas ?? new List<MlogisCorrida>();
            }
            catch
            {
                return new List<MlogisCorrida>();
            }
        }

        private async Task<List<MetricaDiaria>> LeerHistoricoMensualAsync()
        {
            if (!File.Exists(_historicoMensualPath)) return new List<MetricaDiaria>();
            try
            {
                string json = await Task.Run(() =>
                {
                    using (var fs = new FileStream(_historicoMensualPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                        return sr.ReadToEnd();
                });
                var parsed = JsonConvert.DeserializeObject<MlogisHistoricoMensual>(json);
                return parsed?.Dias ?? new List<MetricaDiaria>();
            }
            catch
            {
                return new List<MetricaDiaria>();
            }
        }

        private async Task<int> ContarAlertasHoyAsync()
        {
            if (!File.Exists(_alertasPath)) return 0;
            try
            {
                string json = await Task.Run(() =>
                {
                    using (var fs = new FileStream(_alertasPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                        return sr.ReadToEnd();
                });

                // Soporte para ambos formatos: array plano [] o { "alertas": [] }
                JArray arr = null;
                string trimmed = json.TrimStart();
                if (trimmed.StartsWith("{"))
                {
                    var obj = JObject.Parse(json);
                    arr = obj["alertas"] as JArray;
                }
                else if (trimmed.StartsWith("["))
                {
                    arr = JArray.Parse(json);
                }

                if (arr == null) return 0;

                int count = 0;
                foreach (var token in arr)
                {
                    // Verificamos ambos nombres de campo posibles (timestamp o ultima_vez_alertado)
                    string tsStr = (token["timestamp"] ?? token["ultima_vez_alertado"])?.ToString();
                    
                    if (DateTime.TryParse(tsStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime ts))
                    {
                        if (ts.Date == DateTime.Today)
                            count++;
                    }
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static Brush ResolveBrush(string primaryKey, string fallbackKey)
        {
            var app = Application.Current;
            if (app == null) return Brushes.White;

            return app.TryFindResource(primaryKey) as Brush
                   ?? app.TryFindResource(fallbackKey) as Brush
                   ?? Brushes.White;
        }

        private void ConfigurarWatcher()
        {
            try { _watcherHistorial?.Dispose(); } catch { }
            try { _watcherAlertas?.Dispose(); } catch { }
            try { _watcherMensual?.Dispose(); } catch { }
            try { _watcherSettings?.Dispose(); } catch { }
            _debounceTimer?.Dispose();
            _watcherHistorial = null;
            _watcherAlertas   = null;
            _watcherMensual   = null;
            _watcherSettings  = null;
            _debounceTimer    = null;

            if (string.IsNullOrWhiteSpace(_logsDir) || !Directory.Exists(_logsDir))
                return;

            _debounceTimer = new Timer(_ =>
            {
                if (_disposed) return;
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => _ = CargarAsync()));
            }, null, Timeout.Infinite, Timeout.Infinite);

            _watcherHistorial = new FileSystemWatcher(_logsDir)
            {
                Filter       = "mlogis_historial*.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcherHistorial.Changed += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
            _watcherHistorial.Created += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);

            _watcherAlertas = new FileSystemWatcher(_logsDir)
            {
                Filter       = "alertas_oracle_enviadas.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcherAlertas.Changed += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
            _watcherAlertas.Created += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);

            _watcherMensual = new FileSystemWatcher(_logsDir)
            {
                Filter       = "mlogis_historico_mensual.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcherMensual.Changed += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
            _watcherMensual.Created += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);

            // Watcher para ui_settings.json (detectar cambios en MostrarBotonCargarHistorico)
            var uiSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui_settings.json");
            var uiSettingsDir  = Path.GetDirectoryName(uiSettingsPath);
            if (!string.IsNullOrWhiteSpace(uiSettingsDir) && Directory.Exists(uiSettingsDir))
            {
                _watcherSettings = new FileSystemWatcher(uiSettingsDir)
                {
                    Filter       = "ui_settings.json",
                    NotifyFilter = NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _watcherSettings.Changed += (s, e) =>
                {
                    try
                    {
                        var settings = JsonConvert.DeserializeObject<UiSettingsModel>(File.ReadAllText(uiSettingsPath));
                        Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            MostrarBotonCargarHistorico = settings?.MostrarBotonCargarHistorico ?? true;
                        });
                    }
                    catch { }
                };
            }
        }

        private async Task CargarHistoricoAsync()
        {
            if (EstaCargandoHistorico) return;

            try
            {
                EstaCargandoHistorico = true;
                ProgresoCargarHistorico = 0;
                TextoProgresoHistorico = "Inicializando...";

                // Leer configuración para SOAP
                string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\ServicioReportesOracle");
                string configPath = Path.Combine(basePath, "config.json");

                if (!File.Exists(configPath))
                {
                    MainViewModel.Instance?.ShowNotification("❌ No se encontró config.json", "error");
                    return;
                }

                var config = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(configPath));
                string dominio = config.Dominio?.ToString();
                string urlAuth = config.UrlAutentificacion?.ToString();
                string urlWs = config.UrlWS?.ToString();

                if (string.IsNullOrEmpty(dominio) || string.IsNullOrEmpty(urlAuth) || string.IsNullOrEmpty(urlWs))
                {
                    MainViewModel.Instance?.ShowNotification("❌ Config SOAP incompleta", "error");
                    return;
                }

                // Leer filtros
                string filtersPath = Path.Combine(basePath, "filters.json");
                if (!File.Exists(filtersPath))
                {
                    MainViewModel.Instance?.ShowNotification("❌ No se encontró filters.json", "error");
                    return;
                }

                var filtros = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(filtersPath));
                var fMlogis = filtros?.FirstOrDefault(f => f.Entidad == "Mlogis");

                if (fMlogis == null)
                {
                    MainViewModel.Instance?.ShowNotification("❌ Filtro Mlogis no encontrado", "error");
                    return;
                }

                // Autenticar
                TextoProgresoHistorico = "Autenticando...";
                var soapClient = new SoapClientUI(dominio, urlAuth, urlWs);
                string token = await soapClient.LoginAsync();

                // Limpiar histórico existente
                if (File.Exists(_historicoMensualPath))
                {
                    try
                    {
                        File.Delete(_historicoMensualPath);
                        System.Diagnostics.Debug.WriteLine("🗑️ Histórico mensual eliminado para recarga limpia.");
                    }
                    catch (Exception ex)
                    {
                        MainViewModel.Instance?.ShowNotification($"⚠️ No se pudo limpiar histórico: {ex.Message}", "warning");
                    }
                }

                // Preparar histórico limpio
                var historicoMensual = new MlogisHistoricoMensual { Dias = new List<MetricaDiaria>() };

                // 30 días individuales (consulta día por día)
                int totalDias = 30;

                for (int dia = 0; dia < totalDias; dia++)
                {
                    DateTime fecha = DateTime.Today.AddDays(-(totalDias - dia));

                    if (fecha >= DateTime.Today) continue;

                    TextoProgresoHistorico = $"Cargando {fecha:dd/MM/yyyy}...";
                    ProgresoCargarHistorico = (int)((dia / (double)totalDias) * 100);

                    try
                    {
                        // Filtro para UN solo día
                        DateTime desde = fecha.Date;
                        DateTime hasta = fecha.Date.AddDays(1).AddSeconds(-1);

                        // Construir filtro temporal
                        string fStr = $"FECUPD>='{desde:dd/MM/yyyy HH:mm:ss}' AND FECUPD<='{hasta:dd/MM/yyyy HH:mm:ss}'";

                        // Agregar condiciones de estado
                        var jFMlogis = (JObject)fMlogis;
                        var condicionesArr = jFMlogis["Condiciones"] as JArray;

                        if (condicionesArr != null && condicionesArr.Count > 0)
                        {
                            var partes = new List<string>();
                            foreach (var cond in condicionesArr)
                            {
                                var partesCond = new List<string>();
                                string estadoLog = cond["EstadoLog"]?.ToString();
                                string status = cond["Status"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(estadoLog)) partesCond.Add($"ESTADOLOG='{estadoLog.Trim()}'");
                                if (!string.IsNullOrWhiteSpace(status)) partesCond.Add($"STATUS='{status.Trim()}'");
                                if (partesCond.Count > 0) partes.Add($"({string.Join(" AND ", partesCond)})");
                            }
                            if (partes.Count == 1)
                                fStr += $" AND {partes[0]}";
                            else if (partes.Count > 1)
                                fStr += $" AND ({string.Join(" OR ", partes)})";
                        }

                        // Llamada SOAP
                        string resultInner = await soapClient.ObtenerRegistrosGenericoAsync(token, "Mlogis", fStr);

                        // Parsear registros
                        int totalIds = 0;
                        if (!string.IsNullOrWhiteSpace(resultInner))
                        {
                            if (resultInner.Trim().StartsWith("["))
                            {
                                var list = JsonConvert.DeserializeObject<List<dynamic>>(resultInner);
                                totalIds = list?.Count ?? 0;
                            }
                            else
                            {
                                int pos = 0;
                                while ((pos = resultInner.IndexOf("<ID>", pos, StringComparison.OrdinalIgnoreCase)) != -1)
                                {
                                    totalIds++;
                                    pos += 4;
                                }
                            }
                        }

                        // Agregar métrica del día con cantidad REAL
                        historicoMensual.Dias.Add(new MetricaDiaria
                        {
                            Fecha = fecha.ToString("yyyy-MM-dd"),
                            TotalCorridas = 1,
                            TotalRegistrosPico = totalIds,
                            AlertasOracleEnviadas = 0
                        });

                        await Task.Delay(300);  // Throttle (300ms × 30 días = 9 segundos)
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error día {fecha:dd/MM}: {ex.Message}");
                    }
                }

                // Guardar
                TextoProgresoHistorico = "Guardando...";
                ProgresoCargarHistorico = 95;

                File.WriteAllText(_historicoMensualPath, JsonConvert.SerializeObject(historicoMensual, Formatting.Indented));

                ProgresoCargarHistorico = 100;
                TextoProgresoHistorico = "✅ Completado";

                MainViewModel.Instance?.ShowNotification($"✅ Cargados {historicoMensual.Dias.Count} días", "success");

                await Task.Delay(1000);
                await CargarAsync();
            }
            catch (Exception ex)
            {
                TextoProgresoHistorico = $"❌ Error: {ex.Message}";
                MainViewModel.Instance?.ShowNotification($"❌ {ex.Message}", "error");
            }
            finally
            {
                EstaCargandoHistorico = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _watcherHistorial?.Dispose(); } catch { }
            try { _watcherAlertas?.Dispose(); } catch { }
            try { _watcherMensual?.Dispose(); } catch { }
            try { _watcherSettings?.Dispose(); } catch { }
            try { _debounceTimer?.Dispose(); } catch { }
            _watcherHistorial = null;
            _watcherAlertas   = null;
            _watcherMensual   = null;
            _watcherSettings  = null;
            _debounceTimer    = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ══════════════════════════════════════════════════════════════════
    // SoapClientUI - Cliente SOAP interno para carga histórica
    // ══════════════════════════════════════════════════════════════════
    internal class SoapClientUI
    {
        private readonly System.Net.Http.HttpClient _http;
        private readonly string _dominio;
        private readonly string _urlAuth;
        private readonly string _urlWs;

        public SoapClientUI(string dominio, string urlAuth, string urlWs)
        {
            _dominio = dominio;
            _urlAuth = urlAuth;
            _urlWs = urlWs;
            _http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        public async Task<string> LoginAsync()
        {
            string envelope =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<soap:Body><LoginServiceWithPackDirect xmlns=\"DkMServer.Services\">" +
                "<packName>dkactas</packName>" +
                $"<domain>{_dominio}</domain>" +
                $"<userName>{_dominio}</userName>" +
                $"<userPwd>{_dominio}</userPwd>" +
                "</LoginServiceWithPackDirect></soap:Body></soap:Envelope>";

            var content = new System.Net.Http.StringContent(envelope, System.Text.Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", "\"DkMServer.Services/LoginServiceWithPackDirect\"");

            var response = await _http.PostAsync(_urlAuth, content);
            string body = await response.Content.ReadAsStringAsync();

            string resultInner = GetXmlVal(body, "LoginServiceWithPackDirectResult");
            string unescaped = UnescapeXml(resultInner);
            string token = GetXmlVal(unescaped, "UserToken");

            if (string.IsNullOrEmpty(token))
                throw new Exception("No se encontró UserToken");

            return token;
        }

        public async Task<string> ObtenerRegistrosGenericoAsync(string token, string entidad, string filtro)
        {
            string envelope =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<soap:Body><ObtenerRegistrosGenerico xmlns=\"http://tempuri.org/\">" +
                $"<token>{token}</token>" +
                $"<entidad>{entidad}</entidad>" +
                "<condicionesFiltro>" +
                $"<string>{EscapeXml(filtro)}</string>" +
                "</condicionesFiltro>" +
                "</ObtenerRegistrosGenerico></soap:Body></soap:Envelope>";

            var content = new System.Net.Http.StringContent(envelope, System.Text.Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", "\"http://tempuri.org/ObtenerRegistrosGenerico\"");

            var response = await _http.PostAsync(_urlWs, content);
            string body = await response.Content.ReadAsStringAsync();

            string resultInner = GetXmlVal(body, "ObtenerRegistrosGenericoResult");
            string unescaped = UnescapeXml(resultInner);

            string codigo = GetXmlVal(unescaped, "CodigoError");
            if (codigo != "0")
                throw new Exception($"Error SOAP {codigo}");

            return UnescapeXml(GetXmlVal(unescaped, "ResultXML"));
        }

        private string GetXmlVal(string xml, string tag)
        {
            if (string.IsNullOrEmpty(xml)) return "";
            string open = $"<{tag}>";
            int s = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (s < 0) return "";
            s += open.Length;
            int e = xml.IndexOf($"</{tag}>", s, StringComparison.OrdinalIgnoreCase);
            if (e < 0) return "";
            return xml.Substring(s, e - s).Trim();
        }

        private string UnescapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("&lt;", "<").Replace("&gt;", ">")
                    .Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&apos;", "'");
        }

        private string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
