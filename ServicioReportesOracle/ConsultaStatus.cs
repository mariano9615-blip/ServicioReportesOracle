using System.Collections.Generic;

namespace ServicioOracleReportes
{
    public class ConsultaStatus
    {
        // ID → Metadata asociada (CTG, u otros campos futuros)
        public Dictionary<string, Dictionary<string, string>> ItemsEnError { get; set; }
            = new Dictionary<string, Dictionary<string, string>>();
    }
}
