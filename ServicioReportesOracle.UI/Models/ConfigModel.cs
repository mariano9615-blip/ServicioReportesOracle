using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ServicioReportesOracle.UI.Models
{
    public class HealthCheckSoapModel : INotifyPropertyChanged
    {
        private List<string> _destinatarios = new List<string> { "mdemichelis@bit.com.ar" };
        private string _asuntoCaido = "⚠️ [{Empresa}] WebService SOAP no disponible — {Fecha}";
        private string _cuerpoCaido = "El WebService SOAP no está respondiendo.\n\nURL: {UrlWS}\nDetectado: {Timestamp}\nTimeout: 60 segundos\n\nLas corridas SOAP quedan suspendidas hasta que el servicio se recupere.\nEl monitoreo Oracle y el sistema de mails existente continúan funcionando normalmente.";
        private string _asuntoRecuperado = "✅ [{Empresa}] WebService SOAP recuperado — {Fecha}";
        private string _cuerpoRecuperado = "El WebService SOAP volvió a estar disponible.\n\nURL: {UrlWS}\nCaído desde: {UltimaVezCaido}\nRecuperado: {Timestamp}\n\nLas corridas SOAP se reanudan normalmente.";

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Destinatarios
        {
            get => _destinatarios;
            set { _destinatarios = value; OnPropertyChanged(); }
        }

        public string AsuntoCaido
        {
            get => _asuntoCaido;
            set { _asuntoCaido = value; OnPropertyChanged(); }
        }

        public string CuerpoCaido
        {
            get => _cuerpoCaido;
            set { _cuerpoCaido = value; OnPropertyChanged(); }
        }

        public string AsuntoRecuperado
        {
            get => _asuntoRecuperado;
            set { _asuntoRecuperado = value; OnPropertyChanged(); }
        }

        public string CuerpoRecuperado
        {
            get => _cuerpoRecuperado;
            set { _cuerpoRecuperado = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ConfigModel : INotifyPropertyChanged
    {
        private string _empresa = "";
        private string _connectionString;
        private string _rutaExcel;
        private string _rutaSQL;
        private string _remitente;
        private string _servidorSMTP;
        private int _puertoSMTP;
        private string _usuarioSMTP;
        private string _claveSMTP;
        private bool _modoDebug;
        private string _mensajeNuevosMovimientos;
        private string _mensajeMovimientosResueltos;
        private string _mensajeTodoOK;
        private bool _habilitarMlogis;
        private string _dominio;
        private string _urlAutentificacion;
        private string _urlWS;
        private int _frecuenciaSoapMinutos;
        private int _delayComparacionMinutos;
        private DateTime? _ultimaEjecucionSoap;
        private DateTime? _ultimaReconciliacion;
        private int _intervaloReconciliacionHoras = 2;
        private string _claveUI;
        private HealthCheckSoapModel _healthCheckSoap = new HealthCheckSoapModel();

        public string Empresa
        {
            get => _empresa;
            set { _empresa = value; OnPropertyChanged(); }
        }

        public string ConnectionString
        {
            get => _connectionString;
            set { _connectionString = value; OnPropertyChanged(); }
        }

        public string RutaExcel
        {
            get => _rutaExcel;
            set { _rutaExcel = value; OnPropertyChanged(); }
        }

        public string RutaSQL
        {
            get => _rutaSQL;
            set { _rutaSQL = value; OnPropertyChanged(); }
        }

        public string Remitente
        {
            get => _remitente;
            set { _remitente = value; OnPropertyChanged(); }
        }

        public string ServidorSMTP
        {
            get => _servidorSMTP;
            set { _servidorSMTP = value; OnPropertyChanged(); }
        }

        public int PuertoSMTP
        {
            get => _puertoSMTP;
            set { _puertoSMTP = value; OnPropertyChanged(); }
        }

        public string UsuarioSMTP
        {
            get => _usuarioSMTP;
            set { _usuarioSMTP = value; OnPropertyChanged(); }
        }

        public string ClaveSMTP
        {
            get => _claveSMTP;
            set { _claveSMTP = value; OnPropertyChanged(); }
        }

        public bool ModoDebug
        {
            get => _modoDebug;
            set { _modoDebug = value; OnPropertyChanged(); }
        }

        public string MensajeNuevosMovimientos
        {
            get => _mensajeNuevosMovimientos;
            set { _mensajeNuevosMovimientos = value; OnPropertyChanged(); }
        }

        public string MensajeMovimientosResueltos
        {
            get => _mensajeMovimientosResueltos;
            set { _mensajeMovimientosResueltos = value; OnPropertyChanged(); }
        }

        public string MensajeTodoOK
        {
            get => _mensajeTodoOK;
            set { _mensajeTodoOK = value; OnPropertyChanged(); }
        }

        public bool HabilitarMlogis
        {
            get => _habilitarMlogis;
            set { _habilitarMlogis = value; OnPropertyChanged(); }
        }

        public string Dominio
        {
            get => _dominio;
            set { _dominio = value; OnPropertyChanged(); }
        }

        public string UrlAutentificacion
        {
            get => _urlAutentificacion;
            set { _urlAutentificacion = value; OnPropertyChanged(); }
        }

        public string UrlWS
        {
            get => _urlWS;
            set { _urlWS = value; OnPropertyChanged(); }
        }

        public int FrecuenciaSoapMinutos
        {
            get => _frecuenciaSoapMinutos;
            set { _frecuenciaSoapMinutos = value; OnPropertyChanged(); }
        }

        public int DelayComparacionMinutos
        {
            get => _delayComparacionMinutos;
            set { _delayComparacionMinutos = value; OnPropertyChanged(); }
        }

        public DateTime? UltimaEjecucionSoap
        {
            get => _ultimaEjecucionSoap;
            set { _ultimaEjecucionSoap = value; OnPropertyChanged(); }
        }

        public DateTime? UltimaReconciliacion
        {
            get => _ultimaReconciliacion;
            set { _ultimaReconciliacion = value; OnPropertyChanged(); }
        }

        public int IntervaloReconciliacionHoras
        {
            get => _intervaloReconciliacionHoras;
            set { _intervaloReconciliacionHoras = value; OnPropertyChanged(); }
        }

        // Contraseña de acceso a la UI (almacenada encriptada con CryptoHelper)
        public string ClaveUI
        {
            get => _claveUI;
            set { _claveUI = value; OnPropertyChanged(); }
        }

        // Health Check del WebService SOAP
        public HealthCheckSoapModel HealthCheckSoap
        {
            get => _healthCheckSoap;
            set { _healthCheckSoap = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
