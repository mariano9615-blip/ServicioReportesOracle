using System.Collections.Generic;
using Newtonsoft.Json;

namespace ServicioReportesOracle.UI.Models
{
    public class ConfigModel
    {
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
    }
}
