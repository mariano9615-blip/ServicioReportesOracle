using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ServicioReportesOracle.UI.Models;
using ServicioReportesOracle.UI.Services;
using ServicioReportesOracle.UI;
using ServicioOracleReportes;
using Oracle.ManagedDataAccess.Client;
using System.Windows;
using System.Windows.Threading;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class GeneralConfigViewModel : INotifyPropertyChanged
    {
        private ConfigModel _config;
        private readonly ConfigService _service;
        private readonly string _configPath;
        private string _soapStatus;
        private string _oracleStatus;
        private string _smtpPassword;
        private bool _smtpEncryptionIsEncrypted;
        private string _smtpEncryptionLabel;
        private bool _pendingBackupFail;
        private bool _isBusy;
        private bool _smtpPasswordVisible;
        private string _smtpPasswordPlainText;

        // WS Estado
        private string _wsStatusText = "Sin datos aún";
        private bool _wsStatusEsOk = true;
        private string _wsUltimaVezCaido = "—";
        private string _wsUltimaVezRecuperado = "—";

        public ConfigModel Config
        {
            get => _config;
            set
            {
                _config = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UltimaEjecucionSoapDisplay));
                OnPropertyChanged(nameof(UltimaReconciliacionDisplay));
            }
        }

        public string SoapStatus
        {
            get => _soapStatus;
            set { _soapStatus = value; OnPropertyChanged(); }
        }

        public string OracleStatus
        {
            get => _oracleStatus;
            set { _oracleStatus = value; OnPropertyChanged(); }
        }

        public string SmtpPassword
        {
            get => _smtpPassword;
            set { _smtpPassword = value; OnPropertyChanged(); }
        }

        public bool SmtpEncryptionIsEncrypted
        {
            get => _smtpEncryptionIsEncrypted;
            set { _smtpEncryptionIsEncrypted = value; OnPropertyChanged(); }
        }

        public string SmtpEncryptionLabel
        {
            get => _smtpEncryptionLabel;
            set { _smtpEncryptionLabel = value; OnPropertyChanged(); }
        }

        public bool SmtpPasswordVisible
        {
            get => _smtpPasswordVisible;
            set { _smtpPasswordVisible = value; OnPropertyChanged(); }
        }

        public string SmtpPasswordPlainText
        {
            get => _smtpPasswordPlainText;
            set { _smtpPasswordPlainText = value; OnPropertyChanged(); }
        }

        public bool PendingBackupFail
        {
            get => _pendingBackupFail;
            set { _pendingBackupFail = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
        }

        public bool IsNotBusy => !_isBusy;

        public string UltimaEjecucionSoapDisplay =>
            Config?.UltimaEjecucionSoap.HasValue == true
                ? Config.UltimaEjecucionSoap.Value.ToString("dd/MM/yyyy HH:mm:ss")
                : "Sin datos aún";

        public string UltimaReconciliacionDisplay =>
            Config?.UltimaReconciliacion.HasValue == true
                ? Config.UltimaReconciliacion.Value.ToString("dd/MM/yyyy HH:mm:ss")
                : "Sin datos aún";

        // WS Estado properties
        public string WsStatusText
        {
            get => _wsStatusText;
            set { _wsStatusText = value; OnPropertyChanged(); }
        }

        public bool WsStatusEsOk
        {
            get => _wsStatusEsOk;
            set { _wsStatusEsOk = value; OnPropertyChanged(); }
        }

        public string WsUltimaVezCaido
        {
            get => _wsUltimaVezCaido;
            set { _wsUltimaVezCaido = value; OnPropertyChanged(); }
        }

        public string WsUltimaVezRecuperado
        {
            get => _wsUltimaVezRecuperado;
            set { _wsUltimaVezRecuperado = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand ConfirmSaveCommand { get; }
        public ICommand CancelSaveCommand { get; }
        public ICommand TestSoapCommand { get; }
        public ICommand TestOracleCommand { get; }
        public ICommand RefreshWsStateCommand { get; }
        public ICommand SaveHealthCheckCommand { get; }
        public ICommand ShowSmtpPasswordCommand { get; }
        public ICommand BrowseExcelFolderCommand { get; }
        public ICommand BrowseSqlFolderCommand { get; }

        public GeneralConfigViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _configPath = Path.GetFullPath(Path.Combine(basePath, "..\\ServicioReportesOracle\\config.json"));
            string consultasPath = Path.Combine(basePath, "..\\ServicioReportesOracle\\Consultas.json");

            _service = new ConfigService(_configPath, consultasPath);

            // Inicializar Config con valores vacíos para que los bindings no fallen mientras carga
            Config = new ConfigModel();

            SaveCommand = new RelayCommand(_ => ExecuteSave(), _ => IsNotBusy);
            ConfirmSaveCommand = new RelayCommand(_ => ExecuteSave(skipBackup: true));
            CancelSaveCommand = new RelayCommand(_ => PendingBackupFail = false);

            TestSoapCommand = new RelayCommand(async _ => {
                SoapStatus = "Probando conexión...";
                try {
                    var client = new SoapClient(Config.Dominio, Config.UrlAutentificacion, Config.UrlWS);
                    string token = await client.LoginAsync();
                    SoapStatus = "✅ ¡Conexión Exitosa!";
                    MainViewModel.Instance.ShowNotification("Prueba SOAP exitosa.");
                } catch (Exception ex) {
                    SoapStatus = "❌ Error: " + ex.Message;
                    MainViewModel.Instance.ShowNotification("Error en prueba SOAP.", "Error");
                }
            }, _ => IsNotBusy);

            TestOracleCommand = new RelayCommand(async _ => {
                OracleStatus = "Probando conexión...";
                try {
                    await Task.Run(() => {
                        using (var conn = new OracleConnection(Config.ConnectionString))
                        {
                            conn.Open();
                            conn.Close();
                        }
                    });
                    OracleStatus = "✅ ¡Conexión Exitosa!";
                    MainViewModel.Instance.ShowNotification("Conexión Oracle exitosa.");
                } catch (Exception ex) {
                    OracleStatus = "❌ Error: " + ex.Message;
                    MainViewModel.Instance.ShowNotification("Error en conexión Oracle.", "Error");
                }
            }, _ => IsNotBusy);

            RefreshWsStateCommand = new RelayCommand(_ => LoadWsEstado());
            SaveHealthCheckCommand = new RelayCommand(_ => ExecuteSaveHealthCheck());
            ShowSmtpPasswordCommand = new RelayCommand(_ => ExecuteShowSmtpPassword());

            BrowseExcelFolderCommand = new RelayCommand(_ => BrowseFolder(path => Config.RutaExcel = path));
            BrowseSqlFolderCommand   = new RelayCommand(_ => BrowseFolder(path => Config.RutaSQL = path));

            _ = LoadConfigAsync();
        }

        private void BrowseFolder(Action<string> onSelected)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Seleccionar carpeta",
                ShowNewFolderButton = true
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                onSelected(dialog.SelectedPath);
        }

        private async Task LoadConfigAsync()
        {
            IsBusy = true;
            try
            {
                bool configMissing = !File.Exists(_configPath);

                ConfigModel cfg = await Task.Run(() => _service.LoadConfig());

                List<string> migrados = new List<string>();
                if (!configMissing && File.Exists(_configPath))
                {
                    migrados = await Task.Run(() => MigrarConfigSiFaltan(_configPath));
                    if (migrados.Count > 0)
                        cfg = await Task.Run(() => _service.LoadConfig());
                }

                string smtpPlain = CryptoHelper.Decrypt(cfg.ClaveSMTP);
                bool isEncrypted = cfg.ClaveSMTP?.StartsWith("ENC:") == true;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Config = cfg;
                    SmtpPassword = smtpPlain;
                    UpdateEncryptionStatus(isEncrypted);
                    CommandManager.InvalidateRequerySuggested();

                    if (configMissing)
                    {
                        Application.Current.Dispatcher.BeginInvoke(
                            new Action(() => MainViewModel.Instance?.ShowNotification(
                                "config.json no encontrado. Configurá los datos y guardá para crearlo.", "Error")),
                            DispatcherPriority.ApplicationIdle);
                    }
                    else if (migrados.Count > 0)
                    {
                        string lista = string.Join(", ", migrados);
                        Application.Current.Dispatcher.BeginInvoke(
                            new Action(() => MainViewModel.Instance?.ShowNotification(
                                $"Config.json actualizado: se agregaron nuevos atributos: {lista}")),
                            DispatcherPriority.ApplicationIdle);
                    }
                });

                LoadWsEstado();
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    MainViewModel.Instance?.ShowNotification("Error cargando configuración: " + ex.Message, "Error"));
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void LoadWsEstado()
        {
            try
            {
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string wsPath = Path.GetFullPath(Path.Combine(basePath, "..\\ServicioReportesOracle\\Logs\\json\\ws_estado.json"));

                if (!File.Exists(wsPath))
                {
                    WsStatusText = "Sin datos aún";
                    WsStatusEsOk = true;
                    WsUltimaVezCaido = "—";
                    WsUltimaVezRecuperado = "—";
                    return;
                }

                var jobj = JObject.Parse(File.ReadAllText(wsPath));
                string estado = jobj["ultimo_estado"]?.ToString() ?? "ok";
                string detalleError = jobj["detalle_error"]?.ToString();
                bool esOk = string.Equals(estado, "ok", StringComparison.OrdinalIgnoreCase);
                bool esAuthError = string.Equals(estado, "auth_error", StringComparison.OrdinalIgnoreCase);

                string caido = jobj["ultima_vez_caido"]?.ToString();
                string recuperado = jobj["ultima_vez_recuperado"]?.ToString();

                WsStatusEsOk = esOk;
                if (esOk)
                    WsStatusText = "✅ WebService operativo";
                else if (esAuthError)
                    WsStatusText = string.IsNullOrEmpty(detalleError)
                        ? "⚠️ Error de autenticación SOAP"
                        : $"⚠️ Error de autenticación: {detalleError}";
                else
                    WsStatusText = "⚠️ WebService no disponible";
                WsUltimaVezCaido = ParseFechaWs(caido);
                WsUltimaVezRecuperado = ParseFechaWs(recuperado);
            }
            catch
            {
                WsStatusText = "Sin datos aún";
                WsStatusEsOk = true;
                WsUltimaVezCaido = "—";
                WsUltimaVezRecuperado = "—";
            }
        }

        private string ParseFechaWs(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "—";
            if (DateTime.TryParse(raw, out var dt))
                return dt.ToString("dd/MM/yyyy HH:mm:ss");
            return raw;
        }

        private void ExecuteSave(bool skipBackup = false)
        {
            if (!skipBackup)
            {
                try { _service.BackupConfig(); }
                catch
                {
                    PendingBackupFail = true;
                    return;
                }
            }

            PendingBackupFail = false;
            Config.ClaveSMTP = CryptoHelper.Encrypt(SmtpPassword);
            _service.SaveConfig(Config);
            Config.ClaveSMTP = SmtpPassword;
            UpdateEncryptionStatus(true);
            MainViewModel.Instance.ShowNotification("Configuración guardada. El servicio recargará automáticamente sin reiniciarse.");
        }

        private void ExecuteSaveHealthCheck()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    MainViewModel.Instance.ShowNotification("config.json no encontrado. Guardá la configuración primero.", "Error");
                    return;
                }

                var jobj = JObject.Parse(File.ReadAllText(_configPath));
                jobj["HealthCheckSoap"] = JObject.FromObject(Config.HealthCheckSoap);
                File.WriteAllText(_configPath, jobj.ToString(Formatting.Indented));
                MainViewModel.Instance.ShowNotification("Alertas Health Check guardadas.");
            }
            catch (Exception ex)
            {
                MainViewModel.Instance.ShowNotification("Error guardando Health Check: " + ex.Message, "Error");
            }
        }

        private static readonly string[] _clavesObsoletas = { "DiaEjecucion", "HoraEjecucion", "Destinatarios", "AsuntoCorreo", "CuerpoCorreo", "EnviarCorreo" };

        private List<string> MigrarConfigSiFaltan(string configPath)
        {
            var agregados = new List<string>();
            try
            {
                var defaults = JObject.FromObject(new ConfigModel());
                var actual = JObject.Parse(File.ReadAllText(configPath));

                foreach (var prop in defaults.Properties())
                {
                    if (!actual.ContainsKey(prop.Name))
                    {
                        actual[prop.Name] = prop.Value;
                        agregados.Add(prop.Name);
                    }
                }

                bool huboCambios = agregados.Count > 0;
                foreach (var clave in _clavesObsoletas)
                {
                    if (actual.ContainsKey(clave)) { actual.Remove(clave); huboCambios = true; }
                }

                if (huboCambios)
                    File.WriteAllText(configPath, actual.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch { /* no interrumpir carga de UI */ }
            return agregados;
        }

        private void UpdateEncryptionStatus(bool isEncrypted)
        {
            SmtpEncryptionIsEncrypted = isEncrypted;
            SmtpEncryptionLabel = isEncrypted ? "Encriptada ✓" : "Sin encriptar ⚠";
        }

        private void ExecuteShowSmtpPassword()
        {
            // Ocultar si ya está visible (toggle)
            if (SmtpPasswordVisible)
            {
                SmtpPasswordVisible = false;
                SmtpPasswordPlainText = null;
                return;
            }

            var login = new LoginWindow();
            bool? result = login.ShowDialog();
            if (result == true && login.IsMasterLogin)
            {
                SmtpPasswordPlainText = SmtpPassword;
                SmtpPasswordVisible = true;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
