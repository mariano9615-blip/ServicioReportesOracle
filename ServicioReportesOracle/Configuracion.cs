using System.Collections.Generic;

namespace ServicioOracleReportes
{
    public class Configuracion
    {
        public string ConnectionString { get; set; }
        public int DiaEjecucion { get; set; }
        public string HoraEjecucion { get; set; }
        public string RutaExcel { get; set; }
        public List<string> Destinatarios { get; set; }
        public string Remitente { get; set; }
        public string ServidorSMTP { get; set; }
        public int PuertoSMTP { get; set; }
        public string UsuarioSMTP { get; set; }
        public string ClaveSMTP { get; set; }
        public string AsuntoCorreo { get; set; }
        public string CuerpoCorreo { get; set; }
        public bool ModoDebug { get; set; }
        public bool EnviarCorreo { get; set; }
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

        public DateTime? UltimaEjecucionSoap { get; set; }
    }
}
