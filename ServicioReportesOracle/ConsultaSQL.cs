using System;
using System.Collections.Generic;

namespace ServicioOracleReportes
{
    public class MailConfig
    {
        public string AsuntoConError { get; set; }
        public string AsuntoSinError { get; set; }
        public string CuerpoConError { get; set; }
        public string CuerpoSinError { get; set; }
    }

    // NUEVO → Define qué columnas se muestran en el correo
    public class CamposCorreoConfig
    {
        public List<string> Errores { get; set; } = new List<string>();
        public List<string> Resueltos { get; set; } = new List<string>();
    }

    public class ConsultaSQL
    {
        public string Nombre { get; set; }
        public string Archivo { get; set; }
        public int FrecuenciaMinutos { get; set; }
        public List<string> Destinatarios { get; set; }
        public bool EnviarCorreo { get; set; }

        // TRACK inteligente
        public bool Track { get; set; }
        public string CampoTrack { get; set; }

        // Exclusión de columnas en Excel
        public List<string> ExcluirCampos { get; set; }

        // Configuración del mail por consulta
        public MailConfig Mail { get; set; }

        // NUEVO → Config desde JSON para definir qué columnas imprimir
        public CamposCorreoConfig CamposCorreo { get; set; }

        // NUEVO → Contienen los valores reales por fila (para imprimir en correo)
        public List<Dictionary<string, string>> DetallesErrores { get; set; }
        public List<Dictionary<string, string>> DetallesResueltos { get; set; }

        // Control interno
        public DateTime? UltimaEjecucion { get; set; }
    }
}
