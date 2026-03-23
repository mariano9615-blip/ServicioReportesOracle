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
                Log($"Error: No se encontró {configPath} en la carpeta actual.");
                return;
            }

            Log("Cargando config.json...");
            Configuracion config;
            try
            {
                var json = File.ReadAllText(configPath);
                config = JsonConvert.DeserializeObject<Configuracion>(json);
            }
            catch (Exception ex)
            {
                Log("\n❌ ERROR DE FORMATO EN CONFIG.JSON:");
                Log("Asegúrate de que no falten comas al final de cada línea (excepto la última).");
                Log("Detalle: " + ex.Message);
                return;
            }
            
            Log($"[Config] Dominio: {config.Dominio}");
            Log($"[Config] Auth URL: {config.UrlAutentificacion}");
            Log($"[Config] WS URL: {config.UrlWS}");
            Log("\nIntentando Login...");

            var client = new SoapClient(config.Dominio, config.UrlAutentificacion, config.UrlWS);
            string token = await client.LoginAsync();
            Log("✅ LOGIN EXITOSO. Token recibido.");

            if (File.Exists(filtersPath))
            {
                var filtros = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(filtersPath));
                var fMlogis = filtros.FirstOrDefault(f => f.Entidad == "Mlogis");
                
                if (fMlogis != null)
                {
                    string fStr = fMlogis.Filtro.ToString();
                    string hoy = DateTime.Now.ToString("yyyy-MM-dd");
                    fStr = fStr.Replace("{FECHA_DESDE}", hoy).Replace("{FECHA_HASTA}", hoy);

                    Log($"\nConsultando Azure para el día de HOY ({hoy})...");
                    string resultXml = await client.ObtenerRegistrosGenericoAsync(token, "Mlogis", fStr);
                    
                    int count = 0;
                    int s = 0;
                    while ((s = resultXml.IndexOf("<ID>", s, StringComparison.OrdinalIgnoreCase)) != -1)
                    {
                        count++;
                        s += 4;
                    }

                    Log($"✅ CONSULTA EXITOSA. Se encontraron {count} IDs hoy.");
                }
            }
        }
    }
}
