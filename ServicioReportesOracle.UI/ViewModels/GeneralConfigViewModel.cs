using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.IO;
using ServicioReportesOracle.UI.Models;
using ServicioReportesOracle.UI.Services;
using ServicioOracleReportes;
using System.Windows;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class GeneralConfigViewModel : INotifyPropertyChanged
    {
        private ConfigModel _config;
        private readonly ConfigService _service;
        private string _soapStatus;
        private string _smtpPassword;

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

        public ICommand SaveCommand { get; }
        public ICommand TestSoapCommand { get; }

        public GeneralConfigViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(basePath, "..\\ServicioReportesOracle\\config.json");
            string consultasPath = Path.Combine(basePath, "..\\ServicioReportesOracle\\Consultas.json");

            _service = new ConfigService(configPath, consultasPath);
            Config = _service.LoadConfig();

            // Desencriptar ClaveSMTP para mostrarla en el PasswordBox
            SmtpPassword = CryptoHelper.Decrypt(Config.ClaveSMTP);

            SaveCommand = new RelayCommand(_ => {
                // Encriptar la contraseña antes de guardar al JSON
                Config.ClaveSMTP = CryptoHelper.Encrypt(SmtpPassword);
                _service.SaveConfig(Config);
                // Restaurar en memoria el valor plano (por si se guarda de nuevo sin recargar)
                Config.ClaveSMTP = SmtpPassword;
                MainViewModel.Instance.ShowNotification("Configuración guardada correctamente.");
            });

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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
