using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ServicioOracleReportes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    public class MetricasViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly string _logsDir;
        private readonly string _historialPath;
        private readonly string _historialAyerPath;
        private readonly string _alertasPath;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private FileSystemWatcher _watcherHistorial;
        private FileSystemWatcher _watcherAlertas;
        private Timer _debounceTimer;
        private const int DebounceMs = 2000;
        private bool _disposed;

        private PointCollection _idsSparklinePoints = new PointCollection();
        private PointCollection _duracionSparklinePoints = new PointCollection();
        private string _corridasHoyVsAyer = "0 hoy / 0 ayer";
        private int _alertasHoy;
        private int _fullHoy;
        private int _deltaHoy;
        private double _fullPorcentaje;
        private double _deltaPorcentaje;
        private double _fullBarWidth;
        private double _deltaBarWidth;
        private string _idsSparklineLabel = "Sin datos";
        private string _duracionSparklineLabel = "Sin datos";

        public ICommand RefreshCommand { get; }

        public PointCollection IdsSparklinePoints
        {
            get => _idsSparklinePoints;
            set { _idsSparklinePoints = value; OnPropertyChanged(); }
        }

        public PointCollection DuracionSparklinePoints
        {
            get => _duracionSparklinePoints;
            set { _duracionSparklinePoints = value; OnPropertyChanged(); }
        }

        public string CorridasHoyVsAyer
        {
            get => _corridasHoyVsAyer;
            set { _corridasHoyVsAyer = value; OnPropertyChanged(); }
        }

        public int AlertasHoy
        {
            get => _alertasHoy;
            set { _alertasHoy = value; OnPropertyChanged(); }
        }

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

        public string IdsSparklineLabel
        {
            get => _idsSparklineLabel;
            set { _idsSparklineLabel = value; OnPropertyChanged(); }
        }

        public string DuracionSparklineLabel
        {
            get => _duracionSparklineLabel;
            set { _duracionSparklineLabel = value; OnPropertyChanged(); }
        }

        public MetricasViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _logsDir = Path.GetFullPath(Path.Combine(basePath, @"..\ServicioReportesOracle\Logs"));
            _historialPath = Path.Combine(_logsDir, "mlogis_historial.json");
            _historialAyerPath = Path.Combine(_logsDir, "mlogis_historial_ayer.json");
            _alertasPath = Path.Combine(_logsDir, "alertas_oracle_enviadas.json");

            RefreshCommand = new RelayCommand(_ => _ = CargarAsync());
            ConfigurarWatcher();
            _ = CargarAsync();
        }

        internal async Task CargarAsync()
        {
            if (!await _refreshLock.WaitAsync(0)) return;
            try
            {
                var hoy = await LeerHistorialAsync(_historialPath);
                var ayer = await LeerHistorialAsync(_historialAyerPath);
                var combinadas = hoy.Concat(ayer)
                                    .OrderBy(c => c.FechaEjecucion)
                                    .ToList();

                int skip = Math.Max(0, combinadas.Count - 20);
                var ultimas20 = combinadas.Skip(skip).ToList();
                var idsSerie = ultimas20.Select(c => (double)(c.Registros?.Count ?? 0)).ToList();
                var duracionSerie = ultimas20.Select(CalcularDuracionSegundos).ToList();

                int totalHoy = hoy.Count;
                int totalAyer = ayer.Count;
                int fullHoy = hoy.Count(c => (c.Tipo ?? "").IndexOf("FULL", StringComparison.OrdinalIgnoreCase) >= 0);
                int deltaHoy = hoy.Count - fullHoy;
                int totalTipoHoy = Math.Max(1, fullHoy + deltaHoy);
                double fullPct = (fullHoy * 100.0) / totalTipoHoy;
                double deltaPct = 100.0 - fullPct;

                int alertasHoy = await ContarAlertasHoyAsync();

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    IdsSparklinePoints = BuildSparklinePoints(idsSerie, 200, 60);
                    DuracionSparklinePoints = BuildSparklinePoints(duracionSerie, 200, 60);
                    CorridasHoyVsAyer = $"{totalHoy} hoy / {totalAyer} ayer";
                    FullHoy = fullHoy;
                    DeltaHoy = deltaHoy;
                    FullPorcentaje = Math.Round(fullPct, 1);
                    DeltaPorcentaje = Math.Round(deltaPct, 1);
                    FullBarWidth = Math.Round(260 * (fullPct / 100.0), 2);
                    DeltaBarWidth = Math.Round(260 * (deltaPct / 100.0), 2);
                    AlertasHoy = alertasHoy;

                    IdsSparklineLabel = idsSerie.Count == 0
                        ? "Sin corridas en la ventana"
                        : $"Min {idsSerie.Min():0} / Max {idsSerie.Max():0}";
                    DuracionSparklineLabel = duracionSerie.Count == 0
                        ? "Sin datos de duración"
                        : $"Min {duracionSerie.Min():0.0}s / Max {duracionSerie.Max():0.0}s";
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

                JArray arr = null;
                try { arr = JArray.Parse(json); } catch { }
                if (arr == null) return 0;

                int count = 0;
                foreach (var token in arr)
                {
                    if (DateTime.TryParse(token["timestamp"]?.ToString(), out DateTime ts) && ts.Date == DateTime.Today)
                        count++;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static double CalcularDuracionSegundos(MlogisCorrida corrida)
        {
            if (corrida?.Registros == null || corrida.Registros.Count == 0)
                return 0;

            // Estimación con timestamps de registros de la corrida (misma fuente de historial).
            var minTs = corrida.Registros.Min(r => r.PrimeraVezVisto);
            var maxTs = corrida.Registros.Max(r => r.UltimaVezVisto);
            var segundos = (maxTs - minTs).TotalSeconds;
            return segundos < 0 ? 0 : segundos;
        }

        private static PointCollection BuildSparklinePoints(IReadOnlyList<double> valores, double width, double height)
        {
            var pts = new PointCollection();
            if (valores == null || valores.Count == 0) return pts;

            double min = valores.Min();
            double max = valores.Max();
            double range = Math.Abs(max - min);
            double xStep = valores.Count > 1 ? width / (valores.Count - 1.0) : 0;

            for (int i = 0; i < valores.Count; i++)
            {
                double x = valores.Count == 1 ? width / 2.0 : i * xStep;
                double y = range < 0.0001
                    ? height / 2.0
                    : height - ((valores[i] - min) / range) * height;
                pts.Add(new Point(x, y));
            }

            return pts;
        }

        private void ConfigurarWatcher()
        {
            try { _watcherHistorial?.Dispose(); } catch { }
            try { _watcherAlertas?.Dispose(); } catch { }
            _debounceTimer?.Dispose();
            _watcherHistorial = null;
            _watcherAlertas = null;
            _debounceTimer = null;

            if (string.IsNullOrWhiteSpace(_logsDir) || !Directory.Exists(_logsDir))
                return;

            _debounceTimer = new Timer(_ =>
            {
                if (_disposed) return;
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => _ = CargarAsync()));
            }, null, Timeout.Infinite, Timeout.Infinite);

            _watcherHistorial = new FileSystemWatcher(_logsDir)
            {
                Filter = "mlogis_historial*.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcherHistorial.Changed += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
            _watcherHistorial.Created += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);

            _watcherAlertas = new FileSystemWatcher(_logsDir)
            {
                Filter = "alertas_oracle_enviadas.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcherAlertas.Changed += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
            _watcherAlertas.Created += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _watcherHistorial?.Dispose(); } catch { }
            try { _watcherAlertas?.Dispose(); } catch { }
            try { _debounceTimer?.Dispose(); } catch { }
            _watcherHistorial = null;
            _watcherAlertas = null;
            _debounceTimer = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
