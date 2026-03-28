using System;
using System.Collections.Generic;

namespace ServicioOracleReportes
{
    public class CircuitBreakerAlertaConfig
    {
        public List<string> Destinatarios { get; set; } = new List<string>();
        public string AsuntoCaido { get; set; } = "⚠️ [{Empresa}] Oracle no disponible — {Fecha}";
        public string CuerpoCaido { get; set; } =
            "El Circuit Breaker de Oracle se abrió por fallos consecutivos de conexión.\n\n" +
            "Empresa: {Empresa}\n" +
            "Detectado: {Timestamp}\n" +
            "Fallos consecutivos: {FallosConsecutivos}\n\n" +
            "Las corridas que requieren Oracle se omitirán hasta que se recupere la conexión.";
        public string AsuntoRecuperado { get; set; } = "✅ [{Empresa}] Oracle recuperado — {Fecha}";
        public string CuerpoRecuperado { get; set; } =
            "La conexión Oracle se recuperó y el Circuit Breaker volvió a CLOSED.\n\n" +
            "Empresa: {Empresa}\n" +
            "Recuperado: {Timestamp}\n" +
            "Fallos consecutivos previos: {FallosConsecutivos}\n\n" +
            "Las corridas Oracle se reanudan normalmente.";
    }

    public class HealthCheckSoapConfig
    {
        public List<string> Destinatarios { get; set; } = new List<string> { "mdemichelis@bit.com.ar" };
        public string AsuntoCaido { get; set; } = "⚠️ [{Empresa}] WebService SOAP no disponible — {Fecha}";
        public string CuerpoCaido { get; set; } = "El WebService SOAP no está respondiendo.\n\nEmpresa: {Empresa}\nServicio: {EndpointHost}\nDetectado: {Timestamp}\nDetalle: {DetalleError}\n\nLas corridas SOAP quedan suspendidas hasta que el servicio se recupere.\nEl monitoreo Oracle y el sistema de mails existente continúan funcionando normalmente.";
        public string AsuntoRecuperado { get; set; } = "✅ [{Empresa}] WebService SOAP recuperado — {Fecha}";
        public string CuerpoRecuperado { get; set; } = "El WebService SOAP volvió a estar disponible.\n\nEmpresa: {Empresa}\nServicio: {EndpointHost}\nCaído desde: {UltimaVezCaido}\nRecuperado: {Timestamp}\n\nLas corridas SOAP se reanudan normalmente.";
    }

    public class Configuracion
    {
        public string Empresa { get; set; } = "";
        public string ConnectionString { get; set; }
        public string RutaExcel { get; set; }
        public string Remitente { get; set; }
        public string ServidorSMTP { get; set; }
        public int PuertoSMTP { get; set; }
        public string UsuarioSMTP { get; set; }
        public string ClaveSMTP { get; set; }
        public bool ModoDebug { get; set; }
        public string RutaSQL { get; set; }
        public string MensajeNuevosMovimientos { get; set; }
        public string MensajeMovimientosResueltos { get; set; }
        public string MensajeTodoOK { get; set; }

        // SOAP Integration
        public string Dominio { get; set; }
        public string UrlAutentificacion { get; set; }
        public string UrlWS { get; set; }
        public int FrecuenciaSoapMinutos { get; set; }
        public int DelayComparacionMinutos { get; set; }
        public bool HabilitarMlogis { get; set; } = true;

        public DateTime? UltimaEjecucionSoap { get; set; }
        public DateTime? UltimaReconciliacion { get; set; }
        public int IntervaloReconciliacionHoras { get; set; } = 2;

        // Contraseña de acceso a la UI (almacenada encriptada con CryptoHelper)
        public string ClaveUI { get; set; }

        // Health Check del WebService SOAP
        public HealthCheckSoapConfig HealthCheckSoap { get; set; } = new HealthCheckSoapConfig();

        // Circuit Breaker Oracle
        public int CircuitBreakerUmbral { get; set; } = 3;
        public int CircuitBreakerTimeoutMinutos { get; set; } = 15;
        public CircuitBreakerAlertaConfig CircuitBreakerAlerta { get; set; } = new CircuitBreakerAlertaConfig();
    }
}
