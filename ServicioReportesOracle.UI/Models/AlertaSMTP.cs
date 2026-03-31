using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ServicioReportesOracle.UI.Models
{
    public class AlertaSMTP : INotifyPropertyChanged
    {
        public DateTime Timestamp { get; set; }
        public string Tipo { get; set; }
        public string IdReferencia { get; set; }
        public List<string> Destinatarios { get; set; }
        public string Asunto { get; set; }
        public string Detalle { get; set; }
        public string Origen { get; set; }

        public string TimestampFormateado => Timestamp.ToString("dd/MM HH:mm");

        public string TipoAmigable
        {
            get
            {
                var mapa = new Dictionary<string, string>
                {
                    ["oracle_caso_a"]        = "⚠️ Oracle - Caso A",
                    ["oracle_caso_b"]        = "🔴 Oracle - Caso B",
                    ["ws_caido"]             = "🔴 WebService Caído",
                    ["ws_recuperado"]        = "✅ WS Recuperado",
                    ["oracle_caido"]         = "🔌❌ Oracle Caído",
                    ["oracle_recuperado"]    = "🔌✅ Oracle Recuperado",
                    ["tarea_sql"]            = "📊 Tarea SQL",
                    ["pendientes_umbral"]    = "⏳ Pendientes Crítico",
                    ["pendientes_resuelto"]  = "✅ Pendientes Resuelto"
                };
                return mapa.ContainsKey(Tipo) ? mapa[Tipo] : Tipo;
            }
        }

        public string DestinatariosStr => Destinatarios != null
            ? string.Join(", ", Destinatarios)
            : "";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
