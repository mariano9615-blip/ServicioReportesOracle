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
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class AlertasViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly string _alertasEnviadasPath;
        private readonly string _alertasLeidasPath;
        private FileSystemWatcher _watcher;
        private Timer _debounceTimer;
        private const int DebounceMs = 2000;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public ObservableCollection<AlertaItem> Alertas { get; } = new ObservableCollection<AlertaItem>();

        private bool _hasAlertas;
        public bool HasAlertas { get => _hasAlertas; set { _hasAlertas = value; OnPropertyChanged(); } }

        public ICommand RefreshCommand { get; }
        public ICommand MarkAsReadCommand { get; }

        public AlertasViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string logsDir = Path.GetFullPath(Path.Combine(basePath, @"..\ServicioReportesOracle\Logs"));
            string uiSettingsDir = Path.GetFullPath(Path.Combine(basePath, @"..\ServicioReportesOracle"));

            _alertasEnviadasPath = Path.Combine(logsDir, "alertas_oracle_enviadas.json");
            _alertasLeidasPath = Path.Combine(uiSettingsDir, "alertas_leidas.json");

            RefreshCommand = new RelayCommand(_ => _ = CargarAsync());
            MarkAsReadCommand = new RelayCommand(alertaObj => MarkAsRead(alertaObj as AlertaItem));

            ConfigurarWatcher();
            _ = CargarAsync();
            
            // Marcar todas como leídas al entrar a la vista
            MarcarTodasComoLeidas();
        }

        private async Task CargarAsync()
        {
            if (!await _refreshLock.WaitAsync(0)) return;
            try
            {
                await Task.Run(() => CargarAlertas());
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(HasAlertas));
                });
            }
            catch { /* absorber */ }
            finally
            {
                _refreshLock.Release();
            }
        }

        private void CargarAlertas()
        {
            try
            {
                var hoy = DateTime.Today;
                var alertasHoy = new List<AlertaItem>();

                if (File.Exists(_alertasEnviadasPath))
                {
                    string json = LeerArchivoSeguro(_alertasEnviadasPath);
                    var arr = JArray.Parse(json);

                    foreach (var token in arr)
                    {
                        string timestampStr = token["timestamp"]?.ToString();
                        if (DateTime.TryParse(timestampStr, out var dt) && dt.Date == hoy)
                        {
                            string id = token["id"]?.ToString() ?? "-";
                            string tipoCaso = token["tipo_caso"]?.ToString() ?? "-";
                            string timestamp = dt.ToString("HH:mm");
                            string nrocomprobante = token["nrocomprobante"]?.ToString() ?? "";

                            var item = new AlertaItem
                            {
                                Id = id,
                                TipoCaso = tipoCaso,
                                Timestamp = timestamp,
                                Nrocomprobante = nrocomprobante,
                                FechaCompleta = dt
                            };

                            alertasHoy.Add(item);
                        }
                    }
                }

                // Ordenar de más reciente a más antigua
                var alertasOrdenadas = alertasHoy.OrderByDescending(a => a.FechaCompleta).ToList();

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Alertas.Clear();
                    foreach (var alerta in alertasOrdenadas)
                    {
                        Alertas.Add(alerta);
                    }
                    HasAlertas = Alertas.Count > 0;
                });
            }
            catch
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Alertas.Clear();
                    HasAlertas = false;
                });
            }
        }

        private void MarcarTodasComoLeidas()
        {
            try
            {
                var hoy = DateTime.Today.ToString("yyyy-MM-dd");
                var idsActuales = new HashSet<string>();

                // Leer alertas leídas existentes
                var leidasExistentes = new List<JObject>();
                if (File.Exists(_alertasLeidasPath))
                {
                    string json = LeerArchivoSeguro(_alertasLeidasPath);
                    var arr = JArray.Parse(json);
                    foreach (var token in arr)
                    {
                        string fecha = token["fecha"]?.ToString();
                        if (fecha == hoy)
                        {
                            leidasExistentes.Add((JObject)token);
                            idsActuales.Add(token["id"]?.ToString() ?? "");
                        }
                    }
                }

                // Agregar alertas del día actual que no estén en leídas
                bool cambios = false;
                if (File.Exists(_alertasEnviadasPath))
                {
                    string json = LeerArchivoSeguro(_alertasEnviadasPath);
                    var arr = JArray.Parse(json);
                    foreach (var token in arr)
                    {
                        string timestampStr = token["timestamp"]?.ToString();
                        if (DateTime.TryParse(timestampStr, out var dt) && dt.Date == DateTime.Today)
                        {
                            string id = token["id"]?.ToString() ?? "";
                            if (!idsActuales.Contains(id) && !string.IsNullOrEmpty(id))
                            {
                                leidasExistentes.Add(JObject.FromObject(new { id, fecha = hoy }));
                                idsActuales.Add(id);
                                cambios = true;
                            }
                        }
                    }
                }

                // Limpiar entradas de días anteriores y guardar
                if (cambios || leidasExistentes.Count > 0)
                {
                    var leidasLimpiadas = leidasExistentes
                        .Where(obj =>
                        {
                            string f = obj["fecha"]?.ToString();
                            return f == hoy;
                        })
                        .ToList();

                    var arr = new JArray(leidasLimpiadas);
                    GuardarArchivoSeguro(_alertasLeidasPath, arr.ToString());
                }
            }
            catch { /* absorber */ }
        }

        private void MarkAsRead(AlertaItem alerta)
        {
            if (alerta == null) return;
            MarcarTodasComoLeidas(); // Por ahora, marcamos todas como leídas de una vez
        }

        private string LeerArchivoSeguro(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
                return reader.ReadToEnd();
        }

        private void GuardarArchivoSeguro(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(fs))
                writer.Write(content);
        }

        private void ConfigurarWatcher()
        {
            try { _watcher?.Dispose(); } catch { }
            _debounceTimer?.Dispose();
            _watcher = null;
            _debounceTimer = null;

            string logsDir = Path.GetDirectoryName(_alertasEnviadasPath);
            if (string.IsNullOrEmpty(logsDir) || !Directory.Exists(logsDir)) return;

            _debounceTimer = new Timer(_ =>
            {
                if (_disposed) return;
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => _ = CargarAsync()));
            }, null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(logsDir)
            {
                Filter = "alertas_oracle_enviadas.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Changed += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
            _watcher.Created += (s, e) => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _watcher?.Dispose(); } catch { }
            try { _debounceTimer?.Dispose(); } catch { }
            _watcher = null;
            _debounceTimer = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AlertaItem
    {
        public string Id { get; set; }
        public string TipoCaso { get; set; }
        public string Timestamp { get; set; }
        public string Nrocomprobante { get; set; }
        public DateTime FechaCompleta { get; set; }

        public string Icono
        {
            get
            {
                return TipoCaso switch
                {
                    "B" => "🔴",
                    "A" => "🟡",
                    _ => "⚫"  // Anulados u otros
                };
            }
        }

        public string TipoTexto
        {
            get
            {
                return TipoCaso switch
                {
                    "B" => "Caso B",
                    "A" => "Caso A",
                    _ => "Anulado"
                };
            }
        }
    }
}
