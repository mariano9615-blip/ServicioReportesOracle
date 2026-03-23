using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ServicioOracleReportes;

namespace TestSoap
{
    class Program
    {
        private static StringBuilder _log = new StringBuilder();

        static void Main(string[] args)
        {
            try
            {
                RunTest().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log("❌ ERROR CRÍTICO:");
                Log(ex.ToString());
            }
            finally
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "testsoap.txt"), _log.ToString());
                }
                catch { }
            }
            
            Console.WriteLine("\n[Fin del proceso. El resultado se guardó en testsoap.txt]");
            Console.WriteLine("Presione una tecla para salir...");
            Console.ReadKey();
        }

        static void Log(string msg)
        {
            Console.WriteLine(msg);
            _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
        }

        static async Task RunTest()
        {
            Log("========================================");
            Log("   PRUEBA DE AUTENTICACIÓN SOAP MLOGIS  ");
            Log("========================================");
            
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(basePath, "config.json");
            string filtersPath = Path.Combine(basePath, "filters.json");

            if (!File.Exists(configPath))
            {
                Log($"❌ Error: No se encontró {configPath} en la carpeta actual.");
                return;
            }

            Log("1. Cargando config.json...");
            Configuracion config;
            try
            {
                var json = File.ReadAllText(configPath);
                config = JsonConvert.DeserializeObject<Configuracion>(json);
            }
            catch (Exception ex)
            {
                Log("\n❌ ERROR DE FORMATO EN CONFIG.JSON:");
                Log(ex.Message);
                return;
            }
            
            Log($"   Dominio: {config.Dominio}");
            Log("\n2. Intentando Login...");

            var client = new SoapClient(config.Dominio, config.UrlAutentificacion, config.UrlWS);
            string token = await client.LoginAsync();
            Log("✅ LOGIN EXITOSO.");

            Log("\n3. Cargando filtros para consulta...");
            if (!File.Exists(filtersPath))
            {
                Log($"⚠️ Advertencia: No se encontró {filtersPath} en la carpeta. No se puede probar la consulta de IDs.");
                return;
            }

            try
            {
                var filtros = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(filtersPath));
                var fMlogis = filtros.FirstOrDefault(f => f.Entidad.ToString().Equals("Mlogis", StringComparison.OrdinalIgnoreCase));
                
                if (fMlogis == null)
                {
                    Log("⚠️ No se encontró una entidad 'Mlogis' en filters.json.");
                    Log("Entidades encontradas: " + string.Join(", ", filtros.Select(f => f.Entidad.ToString())));
                    return;
                }

                // Construcción automática de la consulta
                int overlay = 3;
                if (fMlogis.Overlay != null) int.TryParse(fMlogis.Overlay.ToString(), out overlay);

                // IMPORTANTE: El servidor SOAP espera formato DD/MM/YYYY
                string desde = DateTime.Today.AddDays(-overlay).ToString("dd/MM/yyyy");
                string hasta = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

                string fStr = $"FECHA >= '{desde}' AND FECHA <= '{hasta}'";

                if (fMlogis.EstadoLog != null) 
                    fStr += BuildFilterIn("ESTADOLOG", fMlogis.EstadoLog.ToString());
                
                if (fMlogis.Status != null) 
                    fStr += BuildFilterIn("STATUS", fMlogis.Status.ToString());

                Log($"   Consultando movimientos desde {desde} hasta {hasta} (Overlay: {overlay} días)...");
                Log($"   [DEBUG] Query Final: {fStr}");
                string resultXml = await client.ObtenerRegistrosGenericoAsync(token, "Mlogis", fStr);
                
                List<string> ids = new List<string>();
                int pos = 0;
                while ((pos = resultXml.IndexOf("<ID>", pos, StringComparison.OrdinalIgnoreCase)) != -1)
                {
                    int start = pos + 4;
                    int end = resultXml.IndexOf("</ID>", start, StringComparison.OrdinalIgnoreCase);
                    if (end != -1) ids.Add(resultXml.Substring(start, end - start));
                    pos = end != -1 ? end : start;
                }

                Log($"✅ CONSULTA EXITOSA. Se recuperaron {ids.Count} IDs.");
                
                if (ids.Count > 0)
                {
                    Log("\n--- MUESTRA DE IDs (Primeros 5) ---");
                    foreach (var id in ids.Take(5))
                    {
                        Log($"   - ID: {id}");
                    }
                    Log("-----------------------------------");
                }
                else
                {
                    Log("⚠️ No se encontraron movimientos en este rango de fecha para esta empresa.");
                }
            }
            catch (Exception ex)
            {
                Log("❌ Error durante la consulta: " + ex.Message);
            }
        }

        private static string BuildFilterIn(string column, string values)
        {
            if (string.IsNullOrWhiteSpace(values)) return "";
            var list = values.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(v => $"'{v.Trim()}'");
            return $" AND {column} IN ({string.Join(",", list)})";
        }
    }
}
