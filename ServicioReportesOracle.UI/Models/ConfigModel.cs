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

    public class ConfigModel
    {
        public string Empresa { get; set; } = "";
        public string ConnectionString { get; set; }
        public string RutaExcel { get; set; }
        public string RutaSQL { get; set; }
        public string Remitente { get; set; }
        public string ServidorSMTP { get; set; }
        public int PuertoSMTP { get; set; }
        public string UsuarioSMTP { get; set; }
        public string ClaveSMTP { get; set; }
        public bool ModoDebug { get; set; }
        public string MensajeNuevosMovimientos { get; set; }
        public string MensajeMovimientosResueltos { get; set; }
        public string MensajeTodoOK { get; set; }
        public bool HabilitarMlogis { get; set; }
        public string Dominio { get; set; }
        public string UrlAutentificacion { get; set; }
        public string UrlWS { get; set; }
        public int FrecuenciaSoapMinutos { get; set; }
        public int DelayComparacionMinutos { get; set; }
        public DateTime? UltimaEjecucionSoap { get; set; }
        public DateTime? UltimaReconciliacion { get; set; }
        public int IntervaloReconciliacionHoras { get; set; } = 2;

        // Contraseña de acceso a la UI (almacenada encriptada con CryptoHelper)
        public string ClaveUI { get; set; }

        // Health Check del WebService SOAP
        public HealthCheckSoapModel HealthCheckSoap { get; set; } = new HealthCheckSoapModel();
    }
}
