using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ServicioReportesOracle.UI.Models;
using ServicioReportesOracle.UI.Services;
using ServicioOracleReportes;
using System.Windows;
using System.Windows.Threading;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class GeneralConfigViewModel : INotifyPropertyChanged
    {
        private ConfigModel _config;
        private readonly ConfigService _service;
        private string _soapStatus;
        private string _smtpPassword;
        private bool _smtpEncryptionIsEncrypted;
        private string _smtpEncryptionLabel;
        private bool _pendingBackupFail;

        public ConfigModel Config
        {
            get => _config;
            set { _config = value; OnPropertyChanged(); }
        }

        public string SoapStatus
        {
            get => _soapStatus;
            set { _soapStatus = value; OnPropertyChanged(); }
        }

        // Contraseña en texto plano para el PasswordBox (no va al JSON directamente)
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

        public bool PendingBackupFail
        {
            get => _pendingBackupFail;
            set { _pendingBackupFail = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand ConfirmSaveCommand { get; }
        public ICommand CancelSaveCommand { get; }
        public ICommand TestSoapCommand { get; }

        public GeneralConfigViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(basePath, "..\\ServicioReportesOracle\\config.json");
            string consultasPath = Path.Combine(basePath, "..\\ServicioReportesOracle\\Consultas.json");

            _service = new ConfigService(configPath, consultasPath);

            bool configMissing = !File.Exists(configPath);
            Config = _service.LoadConfig();

            // Si config.json no existía, mostrar Toast una vez que la ventana esté visible
            if (configMissing)
            {
                Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => MainViewModel.Instance?.ShowNotification(
                        "config.json no encontrado. Configurá los datos y guardá para crearlo.", "Error")),
                    DispatcherPriority.ApplicationIdle);
            }

            // Auto-migración de atributos faltantes en config.json
            if (!configMissing && File.Exists(configPath))
            {
                var migrados = MigrarConfigSiFaltan(configPath);
                if (migrados.Count > 0)
                {
                    Config = _service.LoadConfig();
                    string lista = string.Join(", ", migrados);
                    Application.Current.Dispatcher.BeginInvoke(
                        new Action(() => MainViewModel.Instance?.ShowNotification(
                            $"Config.json actualizado: se agregaron nuevos atributos: {lista}")),
                        DispatcherPriority.ApplicationIdle);
                }
            }

            // Inicializar indicador de encriptación basado en el valor del JSON
            UpdateEncryptionStatus(Config.ClaveSMTP?.StartsWith("ENC:") == true);

            // Desencriptar ClaveSMTP para mostrarla en el PasswordBox
            SmtpPassword = CryptoHelper.Decrypt(Config.ClaveSMTP);

            SaveCommand = new RelayCommand(_ => ExecuteSave());
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
            });
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
            // Restaurar en memoria el valor plano (por si se guarda de nuevo sin recargar)
            Config.ClaveSMTP = SmtpPassword;
            UpdateEncryptionStatus(true);
            MainViewModel.Instance.ShowNotification("Configuración guardada. El servicio recargará automáticamente sin reiniciarse.");
        }

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

                if (agregados.Count > 0)
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
