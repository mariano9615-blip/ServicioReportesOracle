using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ServicioOracleReportes;

namespace TestSoap
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                RunTest().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n❌ ERROR CRÍTICO:");
                Console.WriteLine(ex.ToString());
            }
            Console.WriteLine("\nPresione una tecla para salir...");
            Console.ReadKey();
        }

        static async Task RunTest()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("   PRUEBA DE AUTENTICACIÓN SOAP MLOGIS  ");
            Console.WriteLine("========================================");
            
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(basePath, "config.json");
            string filtersPath = Path.Combine(basePath, "filters.json");

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Error: No se encontró {configPath} en la carpeta actual.");
                return;
            }

            var json = File.ReadAllText(configPath);
            var config = JsonConvert.DeserializeObject<Configuracion>(json);
            
            Console.WriteLine($"[Config] Dominio: {config.Dominio}");
            Console.WriteLine($"[Config] Auth URL: {config.UrlAutentificacion}");
            Console.WriteLine($"[Config] WS URL: {config.UrlWS}");
            Console.WriteLine("\nIntentando Login...");

            var client = new SoapClient(config.Dominio, config.UrlAutentificacion, config.UrlWS);
            string token = await client.LoginAsync();
            Console.WriteLine("✅ LOGIN EXITOSO. Token recibido.");

            if (File.Exists(filtersPath))
            {
                var filtros = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(filtersPath));
                var fMlogis = filtros.FirstOrDefault(f => f.Entidad == "Mlogis");
                
                if (fMlogis != null)
                {
                    string fStr = fMlogis.Filtro.ToString();
                    string hoy = DateTime.Now.ToString("yyyy-MM-dd");
                    fStr = fStr.Replace("{FECHA_DESDE}", hoy).Replace("{FECHA_HASTA}", hoy);

                    Console.WriteLine($"\nConsultando Azure para el día de HOY ({hoy})...");
                    string resultXml = await client.ObtenerRegistrosGenericoAsync(token, "Mlogis", fStr);
                    
                    int count = 0;
                    int s = 0;
                    while ((s = resultXml.IndexOf("<ID>", s, StringComparison.OrdinalIgnoreCase)) != -1)
                    {
                        count++;
                        s += 4;
                    }

                    Console.WriteLine($"✅ CONSULTA EXITOSA. Se encontraron {count} IDs hoy.");
                    if (count > 0) Console.WriteLine("El servicio SOAP está configurado y respondiendo correctamente.");
                }
            }
            else
            {
                Console.WriteLine("\n⚠️ Archivo filters.json no encontrado. Saltando prueba de consulta.");
            }
        }
    }
}
