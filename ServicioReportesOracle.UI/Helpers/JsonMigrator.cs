using System;
using System.IO;

namespace ServicioReportesOracle.UI.Helpers
{
    public static class JsonMigrator
    {
        public static int MigrarJsonsASubcarpeta(string baseDir)
        {
            try
            {
                var logDir = Path.Combine(baseDir, "Logs");
                var jsonDir = Path.Combine(logDir, "json");

                if (!Directory.Exists(jsonDir))
                    Directory.CreateDirectory(jsonDir);

                var archivosAMigrar = new[]
                {
                    "mlogis_historial.json",
                    "mlogis_historial_ayer.json",
                    "comparaciones_pendientes.json",
                    "alertas_smtp_enviadas.json",
                    "alertas_oracle_enviadas.json",
                    "ids_history.json",
                    "status.json",
                    "ws_estado.json",
                    "oracle_circuit_state.json",
                    "pendientes_alerta_estado.json",
                    "alertas_leidas.json",
                    "ui_settings.json"
                };

                int migrados = 0;
                foreach (var archivo in archivosAMigrar)
                {
                    var pathViejo = Path.Combine(logDir, archivo);
                    var pathNuevo = Path.Combine(jsonDir, archivo);

                    if (File.Exists(pathViejo) && !File.Exists(pathNuevo))
                    {
                        File.Move(pathViejo, pathNuevo);
                        migrados++;
                    }
                }

                return migrados;
            }
            catch
            {
                return 0;
            }
        }
    }
}
