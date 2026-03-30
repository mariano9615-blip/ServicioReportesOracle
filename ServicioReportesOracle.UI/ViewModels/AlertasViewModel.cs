using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ClosedXML.Excel;
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
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                    MainViewModel.Instance.ShowNotification($"Error al cargar alertas: {ex.Message}"));
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
                Filter = "Excel Files|*.xlsx",
                FileName = $"Alertas_SMTP_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Alertas");

                    ws.Cell(1, 1).Value = "Fecha";
                    ws.Cell(1, 2).Value = "Tipo";
                    ws.Cell(1, 3).Value = "ID Referencia";
                    ws.Cell(1, 4).Value = "Destinatarios";
                    ws.Cell(1, 5).Value = "Asunto";
                    ws.Cell(1, 6).Value = "Detalle";
                    ws.Cell(1, 7).Value = "Origen";

                    var headerRange = ws.Range("A1:G1");
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F46E5");
                    headerRange.Style.Font.FontColor = XLColor.White;
                    headerRange.Style.Font.Bold = true;

                    int row = 2;
                    foreach (var a in Alertas)
                    {
                        ws.Cell(row, 1).Value = a.TimestampFormateado;
                        ws.Cell(row, 2).Value = a.TipoAmigable;
                        ws.Cell(row, 3).Value = a.IdReferencia ?? "";
                        ws.Cell(row, 4).Value = a.DestinatariosStr;
                        ws.Cell(row, 5).Value = a.Asunto;
                        ws.Cell(row, 6).Value = a.Detalle;
                        ws.Cell(row, 7).Value = a.Origen;
                        row++;
                    }

                    ws.Columns().AdjustToContents();
                    wb.SaveAs(dialog.FileName);
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
