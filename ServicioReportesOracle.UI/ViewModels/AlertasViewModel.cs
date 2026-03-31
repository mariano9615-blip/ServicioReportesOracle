using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using ServicioReportesOracle.UI.Models;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class AlertasViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly string baseDir;
        private FileSystemWatcher _watcher;
        private System.Timers.Timer _debounceTimer;

        private ObservableCollection<AlertaSMTP> _alertas;
        public ObservableCollection<AlertaSMTP> Alertas
        {
            get => _alertas;
            set { _alertas = value; OnPropertyChanged(); }
        }

        private int _totalAlertas;
        public int TotalAlertas
        {
            get => _totalAlertas;
            set { _totalAlertas = value; OnPropertyChanged(); }
        }

        private int _alertasHoy;
        public int AlertasHoy
        {
            get => _alertasHoy;
            set { _alertasHoy = value; OnPropertyChanged(); }
        }

        public ICommand ActualizarCommand { get; }
        public ICommand ExportarCommand { get; }

        public AlertasViewModel()
        {
            baseDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\ServicioReportesOracle"));

            ActualizarCommand = new RelayCommand(_ => CargarAlertas());
            ExportarCommand = new RelayCommand(_ => ExportarExcel());

            CargarAlertas();
            IniciarWatcher();
        }

        private void CargarAlertas()
        {
            try
            {
                var path = Path.Combine(baseDir, "Logs", "json", "alertas_smtp_enviadas.json");
                if (!File.Exists(path))
                {
                    Alertas = new ObservableCollection<AlertaSMTP>();
                    TotalAlertas = 0;
                    AlertasHoy = 0;
                    return;
                }

                string json;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs))
                    json = reader.ReadToEnd();

                var container = JObject.Parse(json);
                var alertas = (container["alertas"] as JArray ?? new JArray())
                    .Select(a => new AlertaSMTP
                    {
                        Timestamp     = DateTime.Parse(a["timestamp"].ToString()),
                        Tipo          = a["tipo"]?.ToString() ?? "",
                        IdReferencia  = a["id_referencia"]?.ToString(),
                        Destinatarios = a["destinatarios"]?.ToObject<List<string>>() ?? new List<string>(),
                        Asunto        = a["asunto"]?.ToString() ?? "",
                        Detalle       = a["detalle"]?.ToString() ?? "",
                        Origen        = a["origen"]?.ToString() ?? ""
                    })
                    .OrderByDescending(a => a.Timestamp)
                    .ToList();

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Alertas = new ObservableCollection<AlertaSMTP>(alertas);
                    TotalAlertas = alertas.Count;
                    AlertasHoy = alertas.Count(a => a.Timestamp.Date == DateTime.Today);
                    
                    // Marcar como leídas después de asignar (v7.3.2 Fix)
                    MarcarComoLeidas();
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                    MainViewModel.Instance.ShowNotification($"Error al cargar alertas: {ex.Message}"));
            }
        }

        private void MarcarComoLeidas()
        {
            try
            {
                var alertasLeidasPath = Path.Combine(baseDir, "Logs", "json", "alertas_leidas.json");
                var alertasLeidas = new HashSet<string>();

                // 1. Cargar existentes
                if (File.Exists(alertasLeidasPath))
                {
                    try
                    {
                        var json = File.ReadAllText(alertasLeidasPath);
                        var arr = JArray.Parse(json);
                        foreach (var token in arr)
                        {
                            string id = token.ToString();
                            if (!string.IsNullOrEmpty(id))
                                alertasLeidas.Add(id);
                        }
                    }
                    catch { }
                }

                // 2. Purga automática (Ajuste v7.3.2) - Mantener solo 7 días
                var cutoffDate = DateTime.Today.AddDays(-7);
                alertasLeidas = alertasLeidas
                    .Where(id => {
                        var parts = id.Split('_');
                        if (parts.Length > 0 && DateTime.TryParse(parts[0], out var ts))
                        {
                            return ts.Date >= cutoffDate;
                        }
                        return false;
                    })
                    .ToHashSet();

                // 3. Marcar actuales de hoy como leídas
                int nuevasLeidas = 0;
                if (Alertas != null)
                {
                    foreach (var a in Alertas.Where(x => x.Timestamp.Date == DateTime.Today))
                    {
                        string id = $"{a.Timestamp:yyyy-MM-ddTHH:mm:ss}_{a.Tipo}";
                        if (alertasLeidas.Add(id))
                        {
                            nuevasLeidas++;
                        }
                    }
                }

                // 4. Guardar si hubo cambios o purga
                File.WriteAllText(alertasLeidasPath, JArray.FromObject(alertasLeidas).ToString(Newtonsoft.Json.Formatting.Indented));

                System.Diagnostics.Debug.WriteLine($"[AlertasViewModel] Marcadas {nuevasLeidas} alertas nuevas como leídas (Total persistido: {alertasLeidas.Count})");

                // 5. Notificar al MainViewModel (Ajuste v7.3.2)
                Application.Current?.Dispatcher.InvokeAsync(() => 
                    MainViewModel.Instance?.CargarAlertasSidebarAsync()
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlertasViewModel] Error en MarcarComoLeidas: {ex.Message}");
            }
        }

        private void IniciarWatcher()
        {
            var logDir = Path.Combine(baseDir, "Logs", "json");
            if (!Directory.Exists(logDir)) return;

            _watcher = new FileSystemWatcher(logDir)
            {
                Filter = "alertas_smtp_enviadas.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnArchivoChanged;
            _watcher.Created += OnArchivoChanged;

            _debounceTimer = new System.Timers.Timer(2000) { AutoReset = false };
            _debounceTimer.Elapsed += (s, e) =>
                Application.Current?.Dispatcher.InvokeAsync(() => CargarAlertas());
        }

        private void OnArchivoChanged(object sender, FileSystemEventArgs e)
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
        }

        private void ExportarExcel()
        {
            if (Alertas == null || Alertas.Count == 0)
            {
                MainViewModel.Instance.ShowNotification("No hay alertas para exportar.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV Files|*.csv",
                FileName = $"Alertas_SMTP_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                using (var writer = new StreamWriter(dialog.FileName, false, Encoding.UTF8))
                {
                    // Header
                    writer.WriteLine("Fecha,Tipo,ID Referencia,Destinatarios,Asunto,Detalle,Origen");
                    
                    // Data
                    foreach (var a in Alertas)
                    {
                        writer.WriteLine($"\"{a.TimestampFormateado}\",\"{a.TipoAmigable}\",\"{a.IdReferencia ?? ""}\",\"{a.DestinatariosStr}\",\"{a.Asunto}\",\"{a.Detalle}\",\"{a.Origen}\"");
                    }
                }

                MainViewModel.Instance.ShowNotification($"Exportado: {Path.GetFileName(dialog.FileName)}");
            }
            catch (Exception ex)
            {
                MainViewModel.Instance.ShowNotification($"Error al exportar: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _debounceTimer?.Dispose();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
