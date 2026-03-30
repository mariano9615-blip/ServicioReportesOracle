using ClosedXML.Excel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.ServiceProcess;
using System.Timers;
using System.Threading.Tasks;

namespace ServicioOracleReportes
{
    public class ServicioOracleReportes : ServiceBase
    {
        private Timer timer;
        private Configuracion configuracion;
        private List<ConsultaSQL> consultas;
        private static readonly object lockObj        = new object();
        private static readonly object _wsEstadoLock  = new object();
        private bool enEjecucion = false;
        private DateTime ultimoWriteTimeConsultas = DateTime.MinValue;
        private string _rutaLogs;

        // Hot-reload
        private FileSystemWatcher _fileWatcher;
        private System.Threading.Timer _debounceConfig;
        private System.Threading.Timer _debounceConsultas;
        private string _configPath;
        private string _consultasPath;
        private OracleCircuitBreaker _oracleCircuitBreaker;


        protected override void OnStart(string[] args)
        {
            try
            {
                EscribirLog("Iniciando servicio...");

                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string configPath = Path.Combine(basePath, "config.json");
                string consultasPath = Path.Combine(basePath, "consultas.json");

                if (!File.Exists(configPath))
                    throw new FileNotFoundException("No se encontró config.json");
                if (!File.Exists(consultasPath))
                    throw new FileNotFoundException("No se encontró consultas.json");

                configuracion = JsonConvert.DeserializeObject<Configuracion>(File.ReadAllText(configPath));

                // Auto-migración: atributos faltantes en config.json
                MigrarConfigSiFaltan(configPath);

                // Auto-migración: encriptar ClaveSMTP si aún está en texto plano
                if (!string.IsNullOrEmpty(configuracion.ClaveSMTP) && !CryptoHelper.IsEncrypted(configuracion.ClaveSMTP))
                {
                    EscribirLog("🔐 ClaveSMTP en texto plano detectada, encriptando...");
                    configuracion.ClaveSMTP = CryptoHelper.Encrypt(configuracion.ClaveSMTP);
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(configuracion, Formatting.Indented));
                    EscribirLog("✅ ClaveSMTP encriptada y guardada en config.json.");
                }
                // Desencriptar en memoria para uso interno (SmtpClient siempre recibe texto plano)
                configuracion.ClaveSMTP = CryptoHelper.Decrypt(configuracion.ClaveSMTP);

                consultas = JsonConvert.DeserializeObject<List<ConsultaSQL>>(File.ReadAllText(consultasPath));
                ultimoWriteTimeConsultas = File.GetLastWriteTime(consultasPath);

                _configPath = configPath;
                _consultasPath = consultasPath;

                _rutaLogs = Path.Combine(basePath, "Logs");
                Directory.CreateDirectory(_rutaLogs);
                MigrarArchivosOperativos();
                _oracleCircuitBreaker = new OracleCircuitBreaker(
                    Path.Combine(_rutaLogs, "oracle_circuit_state.json"),
                    configuracion.CircuitBreakerUmbral,
                    configuracion.CircuitBreakerTimeoutMinutos,
                    EscribirLog);

                IniciarFileWatcher(basePath);

                timer = new Timer(60000); // Cada 60 segundos
                timer.Elapsed += TimerElapsed;
                timer.Start();

                EscribirLog("Servicio iniciado correctamente.");
            }
            catch (Exception ex)
            {
                EscribirLog("Error al iniciar el servicio: " + ex.Message);
            }
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                lock (lockObj)
                {
                    if (enEjecucion) return;
                    enEjecucion = true;

                    string basePath = AppDomain.CurrentDomain.BaseDirectory;
                    string consultasPath = Path.Combine(basePath, "consultas.json");

                    // Verificamos si el archivo fue modificado
                    DateTime writeTimeActual = File.GetLastWriteTime(consultasPath);

                    if (writeTimeActual != ultimoWriteTimeConsultas)
                    {
                        EscribirLog("🔄 Se detectó un cambio en consultas.json. Recargando...");
                        consultas = JsonConvert.DeserializeObject<List<ConsultaSQL>>(File.ReadAllText(consultasPath));
                        ultimoWriteTimeConsultas = writeTimeActual;
                        EscribirLog("✔️ consultas.json recargado correctamente.");
                    }
                }

                // ================================
                //   EJECUTA CONSULTAS (INCLUYE COMPARACIÓN)
                // ================================
                EjecutarConsultasSegunFrecuencia();

                // ================================
                //   INVOCACIÓN SOAP (BACKGROUND)
                // ================================
                if (configuracion.HabilitarMlogis && configuracion.FrecuenciaSoapMinutos > 0)
                {
                    bool debeEjecutarSoap = !configuracion.UltimaEjecucionSoap.HasValue ||
                        (DateTime.Now - configuracion.UltimaEjecucionSoap.Value).TotalMinutes >= configuracion.FrecuenciaSoapMinutos;
                    if (debeEjecutarSoap)
                        Task.Run(() => InvocacionSoapMlogis());
                }

                // ================================
                //   LIMPIEZA AUTOMÁTICA
                // ================================
                EjecutarLimpiezaAutomatica();
            }
            catch (Exception ex)
            {
                EscribirLog("Error en TimerElapsed: " + ex.Message);
            }
            finally
            {
                lock (lockObj)
                {
                    enEjecucion = false;
                }
            }
        }

        private async Task InvocacionSoapMlogis()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                EscribirLog("🌐 Iniciando Invocación SOAP Mlogis...");

                // Health check: verificar que el WS esté disponible antes de continuar
                bool wsOk = await VerificarWebService();
                if (!wsOk) return;

                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string filtersPath = Path.Combine(basePath, "filters.json");
                if (!File.Exists(filtersPath)) return;

                // ── Determinar tipo de corrida: full (cada 1h) o delta ────────
                int intervaloHoras = configuracion.IntervaloReconciliacionHoras > 0
                    ? configuracion.IntervaloReconciliacionHoras
                    : 2;
                bool esFull = !configuracion.UltimaReconciliacion.HasValue ||
                              (DateTime.Now - configuracion.UltimaReconciliacion.Value).TotalHours >= intervaloHoras;
                string tipo = esFull ? "full" : "delta";

                DateTime desde = esFull
                    ? DateTime.Today                                      // full: desde 00:00 del día
                    : (configuracion.UltimaEjecucionSoap ?? DateTime.Today); // delta: desde última ejecución
                DateTime hasta = DateTime.Now;

                EscribirLog($"🔄 Corrida {tipo.ToUpper()} | Desde: {desde:dd/MM/yyyy HH:mm:ss} | Hasta: {hasta:dd/MM/yyyy HH:mm:ss}");

                var soapClient = new SoapClient(configuracion.Dominio, configuracion.UrlAutentificacion, configuracion.UrlWS);
                string token = await soapClient.LoginAsync();
                EscribirLog("🔐 Autenticación exitosa en Mlogis SOAP.");

                var filtros = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(filtersPath));
                var fMlogis = filtros?.FirstOrDefault(f => f.Entidad == "Mlogis");
                if (fMlogis == null)
                {
                    EscribirLog("⚠️ No se encontró filtro para entidad 'Mlogis' en filters.json.");
                    return;
                }

                // ── Cambio 1: Filtro por FECUPD en lugar de FECHA ─────────────
                string fStr = $"FECUPD>='{desde:dd/MM/yyyy HH:mm:ss}' AND FECUPD<='{hasta:dd/MM/yyyy HH:mm:ss}'";

                // Soportar nuevo formato Condiciones (OR entre pares AND) y formato viejo (compatibilidad)
                var jFMlogis       = (Newtonsoft.Json.Linq.JObject)fMlogis;
                var condicionesArr = jFMlogis["Condiciones"] as Newtonsoft.Json.Linq.JArray;

                if (condicionesArr != null && condicionesArr.Count > 0)
                {
                    var partes = new List<string>();
                    foreach (var cond in condicionesArr)
                    {
                        var partesCond = new List<string>();
                        string estadoLog = cond["EstadoLog"]?.ToString();
                        string status    = cond["Status"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(estadoLog)) partesCond.Add($"ESTADOLOG='{estadoLog.Trim()}'");
                        if (!string.IsNullOrWhiteSpace(status))    partesCond.Add($"STATUS='{status.Trim()}'");
                        if (partesCond.Count > 0) partes.Add($"({string.Join(" AND ", partesCond)})");
                    }
                    if (partes.Count == 1)
                        fStr += $" AND {partes[0]}";
                    else if (partes.Count > 1)
                        fStr += $" AND ({string.Join(" OR ", partes)})";
                }
                else
                {
                    // Formato viejo: EstadoLog y Status como strings separados por coma
                    if (fMlogis.EstadoLog != null) fStr += BuildFilterIn("ESTADOLOG", fMlogis.EstadoLog.ToString());
                    if (fMlogis.Status    != null) fStr += BuildFilterIn("STATUS",    fMlogis.Status.ToString());
                }

                EscribirLog($"🔍 Filtro MLOGIS: {fStr}");

                string resultInner = await soapClient.ObtenerRegistrosGenericoAsync(token, "Mlogis", fStr);

                // ── Parsear registros MLOGIS (ID + NROCOMPROBANTE) ────────────
                var mlogisRecords = new List<(string Id, string NroComprobante, string FecUpd)>();

                if (!string.IsNullOrWhiteSpace(resultInner))
                {
                    if (resultInner.Trim().StartsWith("["))
                    {
                        try
                        {
                            var list = JsonConvert.DeserializeObject<List<dynamic>>(resultInner);
                            foreach (var item in list)
                            {
                                string id     = item.ID?.ToString()           ?? item.Id?.ToString()           ?? "";
                                string nro    = item.NROCOMPROBANTE?.ToString() ?? item.NroComprobante?.ToString() ?? "";
                                string fecupd = item.FECUPD?.ToString()        ?? item.FecUpd?.ToString()        ?? "";
                                if (!string.IsNullOrEmpty(id)) mlogisRecords.Add((id, nro, fecupd));
                            }
                        }
                        catch (Exception ex) { EscribirLog("Error parseando JSON de Mlogis: " + ex.Message); }
                    }
                    else
                    {
                        // Fallback XML: extraer ID y FECUPD
                        int posId = 0;
                        while ((posId = resultInner.IndexOf("<ID>", posId, StringComparison.OrdinalIgnoreCase)) != -1)
                        {
                            int start = posId + 4;
                            int end = resultInner.IndexOf("</ID>", start, StringComparison.OrdinalIgnoreCase);
                            if (end == -1) break;
                            string id = resultInner.Substring(start, end - start).Trim();

                            // Intentar extraer FECUPD cercano al tag ID
                            string fecupd = "";
                            int fecStart = resultInner.IndexOf("<FECUPD>", end, StringComparison.OrdinalIgnoreCase);
                            int fecEnd   = fecStart >= 0 ? resultInner.IndexOf("</FECUPD>", fecStart + 8, StringComparison.OrdinalIgnoreCase) : -1;
                            if (fecStart >= 0 && fecEnd >= 0)
                                fecupd = resultInner.Substring(fecStart + 8, fecEnd - fecStart - 8).Trim();

                            if (!string.IsNullOrEmpty(id)) mlogisRecords.Add((id, "", fecupd));
                            posId = end;
                        }
                    }
                }

                EscribirLog($"✅ SOAP MLOGIS: {mlogisRecords.Count} movimientos recuperados.");

                // ── Cambio 1: Segunda llamada a MLOGISLEGAL para obtener CTG ──
                var ctgPorMlogisId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (mlogisRecords.Count > 0)
                {
                    var mlogisIds = mlogisRecords.Select(r => r.Id).Distinct().ToList();
                    string idList     = string.Join("','", mlogisIds);
                    string filtroLegal = $"MLOGISID IN ('{idList}') AND TIPO='1'";

                    EscribirLog($"🔍 Consultando MLOGISLEGAL para {mlogisIds.Count} IDs...");

                    try
                    {
                        string legalResult = await soapClient.ObtenerRegistrosGenericoAsync(token, "MlogisLegal", filtroLegal);

                        if (!string.IsNullOrWhiteSpace(legalResult))
                        {
                            if (legalResult.Trim().StartsWith("["))
                            {
                                var legalList = JsonConvert.DeserializeObject<List<dynamic>>(legalResult);
                                foreach (var item in legalList)
                                {
                                    string mlogisId = item.MLOGISID?.ToString() ?? item.MlogisId?.ToString() ?? "";
                                    string valor    = item.VALOR?.ToString()    ?? item.Valor?.ToString()    ?? "";
                                    if (!string.IsNullOrEmpty(mlogisId))
                                        ctgPorMlogisId[mlogisId] = valor;
                                }
                            }
                            else
                            {
                                // Fallback XML para MlogisLegal
                                int pos = 0;
                                while ((pos = legalResult.IndexOf("<MLOGISID>", pos, StringComparison.OrdinalIgnoreCase)) != -1)
                                {
                                    int s = pos + "<MLOGISID>".Length;
                                    int e = legalResult.IndexOf("</MLOGISID>", s, StringComparison.OrdinalIgnoreCase);
                                    if (e == -1) break;
                                    string mlogisId = legalResult.Substring(s, e - s).Trim();

                                    int vs = legalResult.IndexOf("<VALOR>", e, StringComparison.OrdinalIgnoreCase);
                                    int ve = vs >= 0 ? legalResult.IndexOf("</VALOR>", vs + 7, StringComparison.OrdinalIgnoreCase) : -1;
                                    string valor = (vs >= 0 && ve >= 0)
                                        ? legalResult.Substring(vs + 7, ve - vs - 7).Trim()
                                        : "";

                                    if (!string.IsNullOrEmpty(mlogisId))
                                        ctgPorMlogisId[mlogisId] = valor;
                                    pos = e;
                                }
                            }
                        }
                    }
                    catch (Exception exLegal)
                    {
                        EscribirLog("⚠️ Error consultando MLOGISLEGAL: " + exLegal.Message);
                    }

                    EscribirLog($"✅ SOAP MLOGISLEGAL: {ctgPorMlogisId.Count} CTGs recuperados.");
                }

                // ── Cambio 3: Cargar/crear historial estructurado ─────────────
                string historialPath = Path.Combine(_rutaLogs, "mlogis_historial.json");
                MlogisHistorial historial;

                if (File.Exists(historialPath))
                {
                    try { historial = JsonConvert.DeserializeObject<MlogisHistorial>(File.ReadAllText(historialPath)) ?? new MlogisHistorial(); }
                    catch { historial = new MlogisHistorial(); }
                }
                else
                {
                    historial = new MlogisHistorial();
                }

                // Para corrida FULL: construir lookup plano del estado más reciente de cada ID
                // desde TODO el historial antes de limpiar — permite comparación inteligente con fecupd.
                var estadoPrevio = new Dictionary<string, MlogisRegistro>(StringComparer.OrdinalIgnoreCase);
                if (esFull)
                {
                    foreach (var corrida in historial.Corridas)
                        foreach (var reg in corrida.Registros)
                            estadoPrevio[reg.Id] = reg; // la última corrida vista "gana"
                    historial.Corridas.Clear();
                }

                DateTime fechaEjecucion = hasta;
                var nuevaCorrida = new MlogisCorrida
                {
                    FechaEjecucion = fechaEjecucion,
                    Tipo           = tipo,
                    Registros      = new List<MlogisRegistro>()
                };

                // ── Detectar cambios comparando con estado previo ──
                var alertas                 = new List<(MlogisRegistro Registro, MlogisCambio Cambio)>();
                var registrosParaPendientes = new List<MlogisRegistro>();
                int cntNuevos = 0, cntActualizados = 0, cntSinCambios = 0;

                foreach (var (mlogisId, nroComprobante, fecUpd) in mlogisRecords)
                {
                    ctgPorMlogisId.TryGetValue(mlogisId, out string ctg);
                    ctg = ctg ?? "";

                    // Buscar estado previo del ID
                    MlogisRegistro previo = null;
                    if (esFull)
                        estadoPrevio.TryGetValue(mlogisId, out previo);
                    else
                        for (int i = historial.Corridas.Count - 1; i >= 0 && previo == null; i--)
                            previo = historial.Corridas[i].Registros.Find(r => r.Id == mlogisId);

                    var registro = new MlogisRegistro
                    {
                        Id              = mlogisId,
                        NroComprobante  = nroComprobante,
                        Ctg             = ctg,
                        FecUpd          = fecUpd,
                        PrimeraVezVisto = previo?.PrimeraVezVisto ?? fechaEjecucion,
                        UltimaVezVisto  = fechaEjecucion,
                        CambiosDetectados = previo?.CambiosDetectados != null
                            ? new List<MlogisCambio>(previo.CambiosDetectados)
                            : new List<MlogisCambio>()
                    };

                    bool irAPendientes = true;

                    if (previo == null)
                    {
                        // ID nunca visto → nuevo
                        cntNuevos++;
                    }
                    else if (esFull
                             && DateTime.TryParse(fecUpd, out DateTime fecUpdDt)
                             && fecUpdDt <= previo.PrimeraVezVisto)
                    {
                        // fecupd <= primera_vez_visto → el registro no cambió desde que lo vimos
                        cntSinCambios++;
                        irAPendientes = false;
                    }
                    else
                    {
                        // Comparar datos (FULL con fecupd > pvz, o cualquier DELTA)
                        bool datoCambiado = false;

                        // Auditar CTG
                        if (!string.Equals(ctg, previo.Ctg, StringComparison.Ordinal)
                            && !(string.IsNullOrEmpty(ctg) && string.IsNullOrEmpty(previo.Ctg)))
                        {
                            var cambio = new MlogisCambio
                            {
                                Campo         = "ctg",
                                ValorAnterior = previo.Ctg,
                                ValorNuevo    = ctg,
                                Detectado     = fechaEjecucion
                            };
                            registro.CambiosDetectados.Add(cambio);
                            alertas.Add((registro, cambio));
                            datoCambiado = true;
                        }

                        // Auditar NROCOMPROBANTE
                        if (!string.Equals(nroComprobante, previo.NroComprobante, StringComparison.Ordinal)
                            && !(string.IsNullOrEmpty(nroComprobante) && string.IsNullOrEmpty(previo.NroComprobante)))
                        {
                            var cambio = new MlogisCambio
                            {
                                Campo         = "nrocomprobante",
                                ValorAnterior = previo.NroComprobante,
                                ValorNuevo    = nroComprobante,
                                Detectado     = fechaEjecucion
                            };
                            registro.CambiosDetectados.Add(cambio);
                            alertas.Add((registro, cambio));
                            datoCambiado = true;
                        }

                        if (datoCambiado)
                            cntActualizados++;
                        else
                        {
                            cntSinCambios++;
                            if (esFull) irAPendientes = false; // FULL sin cambio real → no contaminar pendientes
                        }
                    }

                    if (irAPendientes)
                        registrosParaPendientes.Add(registro);

                    nuevaCorrida.Registros.Add(registro);
                }

                nuevaCorrida.DuracionSegundos = sw.Elapsed.TotalSeconds;
                historial.Corridas.Add(nuevaCorrida);

                // Rotación diaria: hoy → mlogis_historial.json | ayer → mlogis_historial_ayer.json | anteriores → descartadas
                var corridasHoy  = historial.Corridas.Where(c => c.FechaEjecucion.Date == DateTime.Today).ToList();
                var corridasAyer = historial.Corridas.Where(c => c.FechaEjecucion.Date == DateTime.Today.AddDays(-1)).ToList();
                int descartadas  = historial.Corridas.Count - corridasHoy.Count - corridasAyer.Count;

                historial.Corridas = corridasHoy;
                File.WriteAllText(historialPath, JsonConvert.SerializeObject(historial, Formatting.Indented));

                if (corridasAyer.Count > 0)
                {
                    string historialAyerPath = Path.Combine(_rutaLogs, "mlogis_historial_ayer.json");
                    var historialAyer = new MlogisHistorial { Corridas = corridasAyer };
                    File.WriteAllText(historialAyerPath, JsonConvert.SerializeObject(historialAyer, Formatting.Indented));
                }

                EscribirLog($"🗑️ [Historial] Rotación diaria: {corridasHoy.Count} corridas hoy, {corridasAyer.Count} de ayer preservadas, {descartadas} descartadas.");

                EscribirLog($"✅ Corrida {tipo.ToUpper()} guardada en mlogis_historial.json. " +
                            $"Registros: {nuevaCorrida.Registros.Count}. Cambios detectados: {alertas.Count}.");

                // ── Mantener ids_history.json para compatibilidad con EjecutarComparacionMlogis ──
                var idsActualesSoap = new HashSet<string>(mlogisRecords.Select(r => r.Id));
                string historyPath  = Path.Combine(_rutaLogs, "ids_history.json");
                var historia = File.Exists(historyPath)
                    ? JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(File.ReadAllText(historyPath))
                    : new Dictionary<string, DateTime>();

                foreach (var id in idsActualesSoap)
                    if (!historia.ContainsKey(id)) historia[id] = fechaEjecucion;

                var keysToRemove = historia.Where(kv => (DateTime.Now - kv.Value).TotalDays > 5).Select(kv => kv.Key).ToList();
                foreach (var k in keysToRemove) historia.Remove(k);
                File.WriteAllText(historyPath, JsonConvert.SerializeObject(historia, Formatting.Indented));

                // ── Cambio 5: Enviar alertas por mail ─────────────────────────
                string soapConfigPath = Path.Combine(basePath, "consultas_soap.json");
                foreach (var (reg, cambio) in alertas)
                    EnviarAlertaCambioSoap(reg, cambio, tipo, fechaEjecucion, soapConfigPath);

                // ── Buffer de comparaciones pendientes ────────────────────────
                string pendientesPath = Path.Combine(_rutaLogs, "comparaciones_pendientes.json");
                var regsParaPendientes = esFull ? registrosParaPendientes : nuevaCorrida.Registros;
                ActualizarComparacionesPendientes(pendientesPath, regsParaPendientes, tipo, esFull, fechaEjecucion);

                // ── Comparación contra Oracle (query_oracle de consultas_soap.json) ──
                int cntAnulados = CompararConOracle(pendientesPath, soapConfigPath, tipo, fechaEjecucion, historialPath);

                // ── Persistir timestamps en config.json ───────────────────────
                configuracion.UltimaEjecucionSoap = fechaEjecucion;
                if (esFull) configuracion.UltimaReconciliacion = fechaEjecucion;

                try
                {
                    var jObj = JObject.Parse(File.ReadAllText(_configPath));
                    jObj["UltimaEjecucionSoap"] = configuracion.UltimaEjecucionSoap;
                    if (esFull) jObj["UltimaReconciliacion"] = configuracion.UltimaReconciliacion;
                    File.WriteAllText(_configPath, jObj.ToString(Formatting.Indented));
                }
                catch (Exception exSave) { EscribirLog("⚠️ No se pudo persistir timestamps: " + exSave.Message); }

                EscribirLog($"Run {tipo.ToUpper()}: {mlogisRecords.Count} IDs | " +
                            $"N:{cntNuevos} U:{cntActualizados} A:{cntAnulados} S:{cntSinCambios} | " +
                            $"{sw.Elapsed.TotalSeconds:F1}s");
            }
            catch (Exception ex)
            {
                EscribirLog("❌ Error en InvocacionSoapMlogis: " + ex.Message);
            }
        }

        // ── Cambio 5: Alertas de cambios SOAP ────────────────────────────────
        private void EnviarAlertaCambioSoap(
            MlogisRegistro registro,
            MlogisCambio   cambio,
            string         tipo,
            DateTime       fechaEjecucion,
            string         soapConfigPath)
        {
            try
            {
                string asunto = $"⚠️ Cambio detectado en Mlogis — {cambio.Campo.ToUpper()} — ID {registro.Id}";
                var destinatarios = new List<string>();
                string cuerpoTemplate = null;

                if (File.Exists(soapConfigPath))
                {
                    try
                    {
                        var jSoap   = JObject.Parse(File.ReadAllText(soapConfigPath));
                        var jAlerta = jSoap["alertas_cambios"];
                        if (jAlerta != null)
                        {
                            var dest = jAlerta["destinatarios"]?.ToObject<List<string>>();
                            if (dest != null) destinatarios = dest;
                            string asuntoOverride = jAlerta["asunto"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(asuntoOverride)) asunto = asuntoOverride;
                            cuerpoTemplate = jAlerta["cuerpo_template"]?.ToString();
                        }
                    }
                    catch { /* usar defaults */ }
                }

                if (destinatarios.Count == 0)
                {
                    EscribirLog($"⚠️ Sin destinatarios en consultas_soap.json — alerta omitida (ID={registro.Id}, campo={cambio.Campo}).");
                    return;
                }

                string cuerpo;
                if (!string.IsNullOrWhiteSpace(cuerpoTemplate))
                {
                    cuerpo = cuerpoTemplate
                        .Replace("{ID}",            registro.Id)
                        .Replace("{Campo}",          cambio.Campo)
                        .Replace("{ValorAnterior}",  cambio.ValorAnterior)
                        .Replace("{ValorNuevo}",     cambio.ValorNuevo)
                        .Replace("{Timestamp}",      fechaEjecucion.ToString("dd/MM/yyyy HH:mm:ss"))
                        .Replace("{Tipo}",           tipo);
                }
                else
                {
                    cuerpo = $"Se detectó un cambio en el movimiento MLOGIS.\n\n" +
                             $"ID: {registro.Id}\n" +
                             $"Campo modificado: {cambio.Campo}\n" +
                             $"Valor anterior: {cambio.ValorAnterior}\n" +
                             $"Valor nuevo: {cambio.ValorNuevo}\n" +
                             $"Detectado: {fechaEjecucion:dd/MM/yyyy HH:mm:ss}\n" +
                             $"Tipo de corrida: {tipo}";
                }

                asunto = AgregarPrefixEmpresa(asunto
                    .Replace("{ID}",    registro.Id)
                    .Replace("{Campo}", cambio.Campo));

                using (var cliente = new SmtpClient(configuracion.ServidorSMTP, configuracion.PuertoSMTP))
                {
                    cliente.Credentials = new NetworkCredential(configuracion.UsuarioSMTP, configuracion.ClaveSMTP);
                    cliente.EnableSsl   = true;

                    using (var mensaje = new MailMessage())
                    {
                        mensaje.From        = new MailAddress(configuracion.Remitente);
                        mensaje.Subject     = asunto;
                        mensaje.Body        = cuerpo;
                        mensaje.IsBodyHtml  = false;
                        foreach (var d in destinatarios) mensaje.To.Add(d);
                        cliente.Send(mensaje);
                    }
                }

                EscribirLog($"📧 Alerta SOAP enviada: ID={registro.Id}, campo={cambio.Campo}, " +
                            $"'{cambio.ValorAnterior}' → '{cambio.ValorNuevo}'");
            }
            catch (Exception ex)
            {
                EscribirLog($"❌ Error enviando alerta SOAP (ID={registro.Id}): " + ex.Message);
            }
        }
        // ── Comparación Oracle vs buffer de comparaciones pendientes ─────────
        private int CompararConOracle(
            string pendientesPath,
            string soapConfigPath,
            string tipo,
            DateTime fechaEjecucion,
            string historialPath)
        {
            try
            {
                if (!File.Exists(soapConfigPath)) return 0;

                string queryTemplate;
                try
                {
                    var jSoap = JObject.Parse(File.ReadAllText(soapConfigPath));
                    queryTemplate = jSoap["alertas_cambios"]?["query_oracle"]?.ToString();
                }
                catch (Exception ex)
                {
                    EscribirLog("⚠️ Error leyendo query_oracle de consultas_soap.json: " + ex.Message);
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(queryTemplate))
                {
                    EscribirLog("⚠️ query_oracle no configurado en consultas_soap.json. Comparación Oracle omitida.");
                    return 0;
                }

                // Cargar buffer de comparaciones pendientes
                JArray pendientesArr;
                try
                {
                    pendientesArr = File.Exists(pendientesPath)
                        ? JObject.Parse(File.ReadAllText(pendientesPath))["pendientes"] as JArray ?? new JArray()
                        : new JArray();
                }
                catch { pendientesArr = new JArray(); }

                if (pendientesArr.Count == 0)
                {
                    EscribirLog("ℹ️ [Oracle] Sin IDs en buffer de comparaciones pendientes.");
                    return 0;
                }

                // Filtrar por DelayComparacionMinutos
                int delay = configuracion.DelayComparacionMinutos > 0 ? configuracion.DelayComparacionMinutos : 0;
                var listos      = new List<JObject>();
                var postergados = new List<JObject>();

                foreach (JObject entry in pendientesArr)
                {
                    string idStr = entry["id"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(idStr)) continue;

                    DateTime primeraVez;
                    if (!DateTime.TryParse(entry["primera_vez_visto"]?.ToString(), out primeraVez))
                        primeraVez = DateTime.MinValue;

                    double minutosPasados = (DateTime.Now - primeraVez).TotalMinutes;
                    if (minutosPasados >= delay)
                    {
                        listos.Add(entry);
                    }
                    else
                    {
                        postergados.Add(entry);
                    }
                }

                if (listos.Count == 0)
                {
                    EscribirLog("⏳ [Oracle] Sin IDs que cumplan el delay. Comparación postergada.");
                    return 0;
                }

                EscribirLog($"🔍 Comparando {listos.Count} IDs contra Oracle (delay OK, {postergados.Count} postergados)...");

                // Lookup id → nrocomprobante desde los pendientes listos
                var mlogisNro = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in listos)
                {
                    string id  = entry["id"]?.ToString() ?? "";
                    string nro = entry["nrocomprobante"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(id)) mlogisNro[id] = nro;
                }

                var ids = mlogisNro.Keys.ToList();
                var alertasOracle = new List<AlertaOracleItem>();

                // Todos los IDs que pasaron el delay se remueven del buffer (con o sin diferencia)
                var idsARemover = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

                // Helper para lectura case-insensitive de columnas Oracle
                Func<DataRow, string, string> getCol = (row, colName) =>
                {
                    foreach (DataColumn dc in row.Table.Columns)
                        if (string.Equals(dc.ColumnName, colName, StringComparison.OrdinalIgnoreCase))
                            return row[dc]?.ToString() ?? "";
                    return "";
                };

                // v6.6.1 — Recolectar todos los registros Oracle para fuzzy-match de anulados
                var registrosOracle = new List<(string Id, string Nro)>();

                const int chunkSize = 999;
                int totalChunks = (ids.Count + chunkSize - 1) / chunkSize;

                using (var conexion = AbrirConexionOracleConCircuitBreaker("CompararConOracle"))
                {
                    if (conexion == null)
                    {
                        EscribirLog("⚠️ [Oracle] Comparación omitida por Circuit Breaker OPEN/HALF-OPEN.");
                        return 0;
                    }

                    for (int chunk = 0; chunk < totalChunks; chunk++)
                    {
                        var idsChunk = ids.Skip(chunk * chunkSize).Take(chunkSize).ToList();
                        string idsList = string.Join(", ", idsChunk.Select(id => $"'{id}'"));
                        string idsListTruncados = string.Join(", ", idsChunk.Select(id => $"'{id.Substring(0, Math.Min(15, id.Length))}'"));
                        string sql = queryTemplate.Replace("{IDS}", idsList).Replace("{IDS_TRUNCADOS}", idsListTruncados);

                        EscribirLog($"🔍 [Oracle Query] {sql}");

                        using (var cmd = new OracleCommand(sql, conexion))
                        using (var da  = new OracleDataAdapter(cmd))
                        {
                            var dt = new DataTable();
                            da.Fill(dt);

                            foreach (DataRow row in dt.Rows)
                            {
                                string idOracle  = getCol(row, "id");
                                string nroOracle = getCol(row, "nrocomprobante");
                                if (!string.IsNullOrEmpty(idOracle))
                                    registrosOracle.Add((idOracle, nroOracle));
                            }
                        }
                    }
                }

                EscribirLog($"🔍 Oracle devolvió {registrosOracle.Count} filas (directas + anuladas AN%).");

                // Caso A: ID exacto en Oracle pero nrocomprobante difiere (excluye anulados)
                foreach (var (idOracle, nroOracle) in registrosOracle)
                {
                    if (idOracle.StartsWith("AN", StringComparison.OrdinalIgnoreCase))
                        continue; // Anulados no generan Caso A

                    if (mlogisNro.TryGetValue(idOracle, out string nroMlogis)
                        && !string.Equals(nroOracle, nroMlogis, StringComparison.Ordinal)
                        && !(string.IsNullOrEmpty(nroOracle) && string.IsNullOrEmpty(nroMlogis)))
                    {
                        var reg = new MlogisRegistro { Id = idOracle, NroComprobante = nroMlogis };
                        alertasOracle.Add(new AlertaOracleItem
                        {
                            TipoCaso    = "A",
                            Registro    = reg,
                            Campo       = "nrocomprobante",
                            ValorOracle = nroOracle,
                            ValorMlogis = nroMlogis
                        });
                    }
                }

                // Caso B: ID Mlogis no encontrado ni directo ni como anulado (fuzzy-match AN%)
                var idsEncontradosEnOracle  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var anuladosDetectados      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int cntAnulados             = 0;

                foreach (var id in ids)
                {
                    // El trigger Oracle forma el ID anulado como: 'AN' + SUBSTR(idOriginal, 1, 15) + sufijo numérico
                    // Para el match correcto comparamos SUBSTR(idOracle, 2, 15) == idMlogis truncado a 15
                    string idPrefix = id.Substring(0, Math.Min(15, id.Length));

                    bool existeOriginalOAnulado = registrosOracle.Any(ora =>
                        string.Equals(ora.Id, id, StringComparison.OrdinalIgnoreCase) ||
                        (ora.Id.StartsWith("AN", StringComparison.OrdinalIgnoreCase) &&
                         ora.Id.Length > 2 &&
                         string.Equals(ora.Id.Substring(2, Math.Min(15, ora.Id.Length - 2)), idPrefix, StringComparison.OrdinalIgnoreCase)));

                    if (existeOriginalOAnulado)
                    {
                        idsEncontradosEnOracle.Add(id);
                        var anulado = registrosOracle.FirstOrDefault(ora =>
                            ora.Id.StartsWith("AN", StringComparison.OrdinalIgnoreCase) &&
                            ora.Id.Length > 2 &&
                            string.Equals(ora.Id.Substring(2, Math.Min(15, ora.Id.Length - 2)), idPrefix, StringComparison.OrdinalIgnoreCase));
                        if (anulado.Id != null)
                        {
                            cntAnulados++;
                            anuladosDetectados[id] = anulado.Id;
                        }
                    }
                    else
                    {
                        var reg = new MlogisRegistro { Id = id, NroComprobante = mlogisNro[id] };
                        alertasOracle.Add(new AlertaOracleItem
                        {
                            TipoCaso    = "B",
                            Registro    = reg,
                            Campo       = "presencia_oracle",
                            ValorOracle = "No encontrado en ADMIS",
                            ValorMlogis = ""
                        });
                    }
                }

                EscribirLog($"✅ Comparación Oracle completada. " +
                            $"Oracle: {idsEncontradosEnOracle.Count}/{ids.Count} IDs encontrados.");

                // Remover del buffer los IDs que pasaron el delay y guardar
                var pendientesRestantes = new JArray();
                foreach (JObject entry in pendientesArr)
                {
                    string id = entry["id"]?.ToString() ?? "";
                    if (!idsARemover.Contains(id))
                        pendientesRestantes.Add(entry);
                }
                try
                {
                    File.WriteAllText(pendientesPath,
                        new JObject { ["pendientes"] = pendientesRestantes }.ToString(Formatting.Indented));
                    EscribirLog($"📋 [Pendientes] {pendientesRestantes.Count} IDs restantes en buffer tras comparación.");
                }
                catch (Exception ex) { EscribirLog("⚠️ Error guardando comparaciones_pendientes.json: " + ex.Message); }

                // Marcar anulados en historial (batch, una sola escritura)
                if (anuladosDetectados.Count > 0)
                {
                    try
                    {
                        var hist = File.Exists(historialPath)
                            ? JsonConvert.DeserializeObject<MlogisHistorial>(File.ReadAllText(historialPath)) ?? new MlogisHistorial()
                            : new MlogisHistorial();
                        var ultimaCorrida = hist.Corridas.LastOrDefault();
                        if (ultimaCorrida != null)
                        {
                            foreach (var reg in ultimaCorrida.Registros)
                                if (anuladosDetectados.TryGetValue(reg.Id, out string idAN))
                                { reg.Anulado = true; reg.IdAnuladoOracle = idAN; }
                            File.WriteAllText(historialPath, JsonConvert.SerializeObject(hist, Formatting.Indented));
                        }
                    }
                    catch (Exception ex) { EscribirLog("⚠️ Error marcando anulados en historial: " + ex.Message); }
                }

                // Enviar alertas consolidadas con deduplicación
                if (alertasOracle.Count > 0)
                {
                    string dedupPath = Path.Combine(_rutaLogs, "alertas_oracle_enviadas.json");
                    EnviarAlertaOracleConsolidada(alertasOracle, soapConfigPath, tipo, fechaEjecucion, dedupPath);
                }

                return cntAnulados;
            }
            catch (Exception ex)
            {
                EscribirLog("❌ Error en CompararConOracle: " + ex.Message);
                return 0;
            }
        }

        // ── Actualizar buffer de comparaciones pendientes ─────────────────────
        private void ActualizarComparacionesPendientes(
            string pendientesPath,
            List<MlogisRegistro> registros,
            string tipo,
            bool esFull,
            DateTime fechaEjecucion)
        {
            try
            {
                JArray pendientesArr;

                if (esFull)
                {
                    // Corrida FULL: limpiar buffer antes de repoblar con los IDs actuales
                    pendientesArr = new JArray();
                    EscribirLog("🔄 [Pendientes] Corrida FULL: buffer de comparaciones pendientes limpiado.");
                }
                else
                {
                    try
                    {
                        pendientesArr = File.Exists(pendientesPath)
                            ? JObject.Parse(File.ReadAllText(pendientesPath))["pendientes"] as JArray ?? new JArray()
                            : new JArray();
                    }
                    catch { pendientesArr = new JArray(); }
                }

                // Construir lookup de pendientes existentes: id → JObject
                var pendientesDict = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                foreach (JObject entry in pendientesArr)
                {
                    string id = entry["id"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(id)) pendientesDict[id] = entry;
                }

                foreach (var reg in registros)
                {
                    if (string.IsNullOrEmpty(reg.Id)) continue;

                    if (pendientesDict.TryGetValue(reg.Id, out JObject existing))
                    {
                        // Ya existe: actualizar nrocomprobante si cambió, mantener primera_vez_visto
                        string prevNro = existing["nrocomprobante"]?.ToString() ?? "";
                        if (!string.Equals(prevNro, reg.NroComprobante ?? "", StringComparison.Ordinal))
                            existing["nrocomprobante"] = reg.NroComprobante ?? "";
                    }
                    else
                    {
                        // Nuevo: agregar con primera_vez_visto = fechaEjecucion de la corrida
                        var entry = new JObject
                        {
                            ["id"]                = reg.Id,
                            ["nrocomprobante"]    = reg.NroComprobante ?? "",
                            ["primera_vez_visto"] = fechaEjecucion.ToString("yyyy-MM-ddTHH:mm:ss"),
                            ["corrida_origen"]    = tipo
                        };
                        pendientesArr.Add(entry);
                        pendientesDict[reg.Id] = entry;
                    }
                }

                File.WriteAllText(pendientesPath,
                    new JObject { ["pendientes"] = pendientesArr }.ToString(Formatting.Indented));
                EscribirLog($"📋 [Pendientes] {pendientesArr.Count} IDs en buffer. Corrida: {tipo.ToUpper()}.");

                VerificarUmbralPendientes(pendientesArr);
            }
            catch (Exception ex)
            {
                EscribirLog("⚠️ Error actualizando comparaciones_pendientes.json: " + ex.Message);
            }
        }

        private void VerificarUmbralPendientes(JArray pendientesArr)
        {
            try
            {
                var cfg = configuracion.AlertaPendientes ?? new AlertaPendientesConfig();
                int umbral = cfg.UmbralCantidad > 0 ? cfg.UmbralCantidad : 50;
                int cooldownHoras = cfg.CooldownHoras > 0 ? cfg.CooldownHoras : 4;
                int cantidadActual = pendientesArr?.Count ?? 0;
                string estadoPath = Path.Combine(_rutaLogs, "pendientes_alerta_estado.json");

                PendientesAlertaEstado estado;
                try
                {
                    estado = File.Exists(estadoPath)
                        ? JsonConvert.DeserializeObject<PendientesAlertaEstado>(File.ReadAllText(estadoPath)) ?? new PendientesAlertaEstado()
                        : new PendientesAlertaEstado();
                }
                catch { estado = new PendientesAlertaEstado(); }

                DateTime ahora = DateTime.Now;
                var registros = new List<(string Id, DateTime PrimeraVez)>();

                if (pendientesArr != null)
                {
                    foreach (JObject item in pendientesArr)
                    {
                        string id = item["id"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(id)) continue;

                        DateTime primeraVez;
                        if (!DateTime.TryParse(item["primera_vez_visto"]?.ToString(), out primeraVez))
                            primeraVez = ahora;

                        registros.Add((id, primeraVez));
                    }
                }

                string idMasAntiguo = "N/A";
                double horasEnBuffer = 0;
                if (registros.Count > 0)
                {
                    var masAntiguo = registros.OrderBy(r => r.PrimeraVez).First();
                    idMasAntiguo = masAntiguo.Id;
                    horasEnBuffer = Math.Max(0, (ahora - masAntiguo.PrimeraVez).TotalHours);
                }

                bool arribaUmbral = cantidadActual >= umbral;
                bool habiaAlertaActiva = estado.UltimoEnvio.HasValue;

                if (arribaUmbral)
                {
                    bool dentroCooldown = estado.UltimoEnvio.HasValue &&
                                          (ahora - estado.UltimoEnvio.Value).TotalHours < cooldownHoras;

                    if (dentroCooldown)
                    {
                        EscribirLog($"ℹ️ [AlertaPendientes] Umbral alcanzado ({cantidadActual}/{umbral}) pero en cooldown ({cooldownHoras}h).");
                        return;
                    }

                    if (EnviarMailAlertaPendientes(
                        esResolucion: false,
                        cantidadActual: cantidadActual,
                        idMasAntiguo: idMasAntiguo,
                        horasEnBuffer: horasEnBuffer))
                    {
                        estado.UltimoEnvio = ahora;
                        estado.CantidadAlEnviar = cantidadActual;
                        GuardarEstadoAlertaPendientes(estadoPath, estado);
                    }

                    return;
                }

                if (habiaAlertaActiva)
                {
                    if (EnviarMailAlertaPendientes(
                        esResolucion: true,
                        cantidadActual: cantidadActual,
                        idMasAntiguo: idMasAntiguo,
                        horasEnBuffer: horasEnBuffer))
                    {
                        estado.UltimoEnvio = null;
                        estado.CantidadAlEnviar = 0;
                        GuardarEstadoAlertaPendientes(estadoPath, estado);
                    }
                }
            }
            catch (Exception ex)
            {
                EscribirLog("⚠️ [AlertaPendientes] Error verificando umbral de pendientes: " + ex.Message);
            }
        }

        private bool EnviarMailAlertaPendientes(bool esResolucion, int cantidadActual, string idMasAntiguo, double horasEnBuffer)
        {
            try
            {
                var cfg = configuracion.AlertaPendientes ?? new AlertaPendientesConfig();
                var destinatarios = cfg.Destinatarios ?? new List<string>();
                if (destinatarios.Count == 0)
                {
                    EscribirLog("⚠️ [AlertaPendientes] Destinatarios vacíos. Mail omitido.");
                    return false;
                }

                DateTime ahora = DateTime.Now;
                string empresa = configuracion.Empresa ?? "";
                string timestamp = ahora.ToString("dd/MM/yyyy HH:mm:ss");
                string horasTexto = horasEnBuffer.ToString("F2");

                string asuntoTemplate = esResolucion ? cfg.AsuntoResolucion : cfg.AsuntoAlerta;
                string cuerpoTemplate = esResolucion ? cfg.CuerpoResolucion : cfg.CuerpoAlerta;

                string asunto = (asuntoTemplate ?? "")
                    .Replace("{Empresa}", empresa)
                    .Replace("{CantidadActual}", cantidadActual.ToString())
                    .Replace("{IdMasAntiguo}", idMasAntiguo)
                    .Replace("{HorasEnBuffer}", horasTexto)
                    .Replace("{Timestamp}", timestamp);

                string cuerpo = (cuerpoTemplate ?? "")
                    .Replace("{Empresa}", empresa)
                    .Replace("{CantidadActual}", cantidadActual.ToString())
                    .Replace("{IdMasAntiguo}", idMasAntiguo)
                    .Replace("{HorasEnBuffer}", horasTexto)
                    .Replace("{Timestamp}", timestamp);

                string claveSMTP = CryptoHelper.IsEncrypted(configuracion.ClaveSMTP)
                    ? CryptoHelper.Decrypt(configuracion.ClaveSMTP)
                    : configuracion.ClaveSMTP;

                var destsUnicos = new HashSet<string>(
                    destinatarios.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                if (destsUnicos.Count == 0)
                {
                    EscribirLog("⚠️ [AlertaPendientes] Sin destinatarios válidos. Mail omitido.");
                    return false;
                }

                using (var cliente = new SmtpClient(configuracion.ServidorSMTP, configuracion.PuertoSMTP))
                {
                    cliente.Credentials = new NetworkCredential(configuracion.UsuarioSMTP, claveSMTP);
                    cliente.EnableSsl = true;

                    using (var mensaje = new MailMessage())
                    {
                        mensaje.From = new MailAddress(configuracion.Remitente);
                        mensaje.Subject = asunto;
                        mensaje.Body = cuerpo;
                        mensaje.IsBodyHtml = false;
                        foreach (var d in destsUnicos) mensaje.To.Add(d);
                        cliente.Send(mensaje);
                    }
                }

                EscribirLog($"📧 [AlertaPendientes] Mail de {(esResolucion ? "resolución" : "alerta")} enviado. Pendientes={cantidadActual}, ID más antiguo={idMasAntiguo}, Horas={horasTexto}.");
                return true;
            }
            catch (Exception ex)
            {
                EscribirLog("❌ [AlertaPendientes] Error enviando mail: " + ex.Message);
                return false;
            }
        }

        private void GuardarEstadoAlertaPendientes(string path, PendientesAlertaEstado estado)
        {
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(estado, Formatting.Indented));
            }
            catch (Exception ex)
            {
                EscribirLog("⚠️ [AlertaPendientes] Error guardando pendientes_alerta_estado.json: " + ex.Message);
            }
        }

        // ── Fix 2+3: Alerta Oracle consolidada con deduplicación ─────────────
        private void EnviarAlertaOracleConsolidada(
            List<AlertaOracleItem> alertas,
            string soapConfigPath,
            string tipo,
            DateTime fechaEjecucion,
            string dedupPath)
        {
            try
            {
                // Cargar historial de deduplicación (array acumulativo diario)
                JArray dedupArray = LeerDedupCompatible(dedupPath);

                // Construir set de claves ya enviadas HOY: "id|tipo_caso"
                var dedupHoy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in dedupArray)
                {
                    if (DateTime.TryParse(entry["timestamp"]?.ToString(), out DateTime tsEntry)
                        && tsEntry.Date == DateTime.Today)
                    {
                        string key = $"{entry["id"]}|{entry["tipo_caso"]}";
                        dedupHoy.Add(key);
                    }
                }

                // Filtrar solo las alertas no enviadas hoy
                var alertasNuevas = new List<AlertaOracleItem>();
                foreach (var alerta in alertas)
                {
                    string key = $"{alerta.Registro.Id}|{alerta.TipoCaso}";
                    if (dedupHoy.Contains(key))
                    {
                        EscribirLog($"🔕 [Oracle] Alerta ya enviada hoy — ID={alerta.Registro.Id}, tipo={alerta.TipoCaso}");
                        continue;
                    }
                    alertasNuevas.Add(alerta);
                }

                if (alertasNuevas.Count == 0)
                {
                    EscribirLog("ℹ️ [Oracle] Todas las diferencias ya fueron alertadas anteriormente. Mail consolidado omitido.");
                    return;
                }

                // Leer destinatarios de consultas_soap.json
                var destinatarios = new List<string>();
                if (File.Exists(soapConfigPath))
                {
                    try
                    {
                        var jSoap = JObject.Parse(File.ReadAllText(soapConfigPath));
                        var dest  = jSoap["alertas_cambios"]?["destinatarios"]?.ToObject<List<string>>();
                        if (dest != null) destinatarios = dest;
                    }
                    catch { }
                }

                // Construir cuerpo consolidado
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Se detectaron diferencias entre Logística (Mlogis) y ADMIS (Oracle).");
                sb.AppendLine();
                foreach (var item in alertasNuevas)
                {
                    if (item.TipoCaso == "B")
                    {
                        sb.AppendLine("[Caso B - Ausente en Oracle]");
                        sb.AppendLine($"ID: {item.Registro.Id}");
                        sb.AppendLine("Motivo: Existe en Logística pero no fue encontrado en ADMIS");
                    }
                    else
                    {
                        sb.AppendLine("[Caso A - Diferencia en nrocomprobante]");
                        sb.AppendLine($"ID: {item.Registro.Id}");
                        sb.AppendLine($"Valor en Oracle (ADMIS): {item.ValorOracle}");
                        sb.AppendLine($"Valor en Logística (Mlogis): {item.ValorMlogis}");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine($"Detectado: {fechaEjecucion:dd/MM/yyyy HH:mm:ss}");
                sb.AppendLine($"Tipo de corrida: {tipo}");

                string asunto = AgregarPrefixEmpresa(
                    $"⚠️ Diferencias detectadas en Mlogis vs Oracle — {alertasNuevas.Count} registros — {fechaEjecucion:dd/MM/yyyy}");

                if (destinatarios.Count == 0)
                {
                    EscribirLog($"⚠️ Sin destinatarios en consultas_soap.json — alerta Oracle consolidada omitida ({alertasNuevas.Count} casos).");
                }
                else
                {
                    using (var cliente = new SmtpClient(configuracion.ServidorSMTP, configuracion.PuertoSMTP))
                    {
                        cliente.Credentials = new NetworkCredential(configuracion.UsuarioSMTP, configuracion.ClaveSMTP);
                        cliente.EnableSsl   = true;

                        using (var mensaje = new MailMessage())
                        {
                            mensaje.From       = new MailAddress(configuracion.Remitente);
                            mensaje.Subject    = asunto;
                            mensaje.Body       = sb.ToString();
                            mensaje.IsBodyHtml = false;
                            foreach (var d in destinatarios) mensaje.To.Add(d);
                            cliente.Send(mensaje);
                        }
                    }

                    int casoA = alertasNuevas.Count(a => a.TipoCaso == "A");
                    int casoB = alertasNuevas.Count(a => a.TipoCaso == "B");
                    EscribirLog($"📧 Alerta Oracle consolidada enviada: {alertasNuevas.Count} casos ({casoA} Caso A, {casoB} Caso B).");
                }

                // Actualizar archivo de deduplicación después de enviar (o si sin destinatarios)
                // Estructura acumulativa: array plano con purga diaria al inicio del día siguiente
                try
                {
                    // Re-leer del disco para no sobrepisar entradas escritas por otra corrida paralela
                    JArray arrayActual = LeerDedupCompatible(dedupPath);

                    // Purgar entradas de días anteriores (mantener solo las de hoy)
                    var arrayPurgado = new JArray();
                    foreach (var entry in arrayActual)
                    {
                        if (DateTime.TryParse(entry["timestamp"]?.ToString(), out DateTime tsEntry)
                            && tsEntry.Date >= DateTime.Today)
                            arrayPurgado.Add(entry);
                    }

                    // Agregar las nuevas alertas enviadas en esta corrida
                    foreach (var item in alertasNuevas)
                    {
                        arrayPurgado.Add(new JObject
                        {
                            ["id"]             = item.Registro.Id,
                            ["tipo_caso"]      = item.TipoCaso,
                            ["timestamp"]      = fechaEjecucion.ToString("yyyy-MM-ddTHH:mm:ss"),
                            ["nrocomprobante"] = item.Registro.NroComprobante ?? ""
                        });
                    }

                    File.WriteAllText(dedupPath, arrayPurgado.ToString(Formatting.Indented));
                }
                catch (Exception ex)
                {
                    EscribirLog("⚠️ Error escribiendo alertas_oracle_enviadas.json: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                EscribirLog("❌ Error en EnviarAlertaOracleConsolidada: " + ex.Message);
            }
        }

        // Lee alertas_oracle_enviadas.json soportando el formato viejo {"alertas":[...]} y el nuevo array plano.
        // Si encuentra el formato viejo, migra los campos: ultima_vez_alertado → timestamp, campo → tipo_caso.
        private JArray LeerDedupCompatible(string path)
        {
            if (!File.Exists(path)) return new JArray();
            string content;
            try { content = File.ReadAllText(path); } catch { return new JArray(); }
            if (string.IsNullOrWhiteSpace(content)) return new JArray();

            // Formato nuevo: array plano
            try
            {
                var arr = JArray.Parse(content);
                if (arr != null) return arr;
            }
            catch { }

            // Formato viejo: {"alertas": [...]}
            try
            {
                var obj = JObject.Parse(content);
                var viejas = obj["alertas"] as JArray;
                if (viejas == null) return new JArray();
                var migrado = new JArray();
                foreach (var v in viejas)
                {
                    string ts  = v["ultima_vez_alertado"]?.ToString() ?? DateTime.Today.ToString("o");
                    string campo = v["campo"]?.ToString() ?? "";
                    // "nrocomprobante" corresponde a Caso A; cualquier otro campo también default A
                    string tipoCaso = "A";
                    migrado.Add(new JObject
                    {
                        ["id"]             = v["id"]?.ToString() ?? "",
                        ["tipo_caso"]      = tipoCaso,
                        ["timestamp"]      = ts,
                        ["nrocomprobante"] = ""
                    });
                }
                return migrado;
            }
            catch { return new JArray(); }
        }

        private class AlertaOracleItem
        {
            public string TipoCaso    { get; set; }  // "A" = diferencia nrocomprobante, "B" = ausente en Oracle
            public MlogisRegistro Registro { get; set; }
            public string Campo       { get; set; }
            public string ValorOracle { get; set; }
            public string ValorMlogis { get; set; }
        }

        private void EjecutarLimpiezaAutomatica()
        {
            try
            {
                int diasRetencion = 7;

                string carpetaReportes = configuracion.RutaExcel;
                string carpetaEnviados = Path.Combine(carpetaReportes, "..", "Enviados");
                carpetaEnviados = Path.GetFullPath(carpetaEnviados);

                // Reportes
                if (Directory.Exists(carpetaReportes))
                {
                    foreach (var file in Directory.GetFiles(carpetaReportes, "*.xlsx"))
                    {
                        if (File.GetCreationTime(file) < DateTime.Now.AddDays(-diasRetencion))
                        {
                            File.Delete(file);
                            EscribirLog($"🧹 Limpiado Reporte viejo: {file}");
                        }
                    }
                }

                // Enviados
                if (Directory.Exists(carpetaEnviados))
                {
                    foreach (var file in Directory.GetFiles(carpetaEnviados, "*.xlsx"))
                    {
                        if (File.GetCreationTime(file) < DateTime.Now.AddDays(-diasRetencion))
                        {
                            File.Delete(file);
                            EscribirLog($"🧹 Limpiado Enviado viejo: {file}");
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                EscribirLog("⚠️ Error en EjecutarLimpiezaAutomatica(): " + ex.Message);
            }
        }



        private void EjecutarConsultasSegunFrecuencia()
        {
            EscribirLog("Verificando consultas por frecuencia...");

            using (OracleConnection conexion = AbrirConexionOracleConCircuitBreaker("EjecutarConsultasSegunFrecuencia"))
            {
                if (conexion == null)
                {
                    EscribirLog("⚠️ [CircuitBreaker] Corrida Oracle omitida por circuito abierto.");
                    return;
                }

                foreach (var consulta in consultas)
                {
                    try
                    {
                        if (consulta.FrecuenciaMinutos <= 0)
                            continue;

                        bool debeEjecutar = consulta.UltimaEjecucion == null ||
                                            (DateTime.Now - consulta.UltimaEjecucion.Value).TotalMinutes >= consulta.FrecuenciaMinutos;

                        if (!debeEjecutar)
                            continue;

                        if (consulta.Nombre == "ComparacionMlogisOracle")
                        {
                            if (configuracion.HabilitarMlogis)
                                EjecutarComparacionMlogis(conexion, consulta);
                            else
                                EscribirLog("⚠️ Comparación Mlogis omitida (Módulo deshabilitado en config.json).");
                        }
                        else
                        {
                            EjecutarConsultaIndividual(conexion, consulta);
                        }
                        
                        consulta.UltimaEjecucion = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        EscribirLog($"Error ejecutando consulta {consulta.Nombre}: {ex.Message}");
                    }
                }
            }
        }

        private void EjecutarComparacionMlogis(OracleConnection conexion, ConsultaSQL consulta)
        {
            try
            {
                EscribirLog("🔍 Ejecutando Comparación Mlogis vs Oracle (con Delay)...");

                string sqlPath = Path.Combine(configuracion.RutaSQL, consulta.Archivo);
                if (!File.Exists(sqlPath)) throw new FileNotFoundException("No se encontró " + sqlPath);
                
                string sql = File.ReadAllText(sqlPath);
                DataTable tablaOracle = EjecutarConsulta(conexion, sql);
                var idsOracle = tablaOracle.Rows.Cast<DataRow>()
                    .Select(r => r[consulta.CampoTrack]?.ToString())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToHashSet();

                string basePath = AppDomain.CurrentDomain.BaseDirectory;

                // Guardar IDs de Oracle encontrados actualmente (para visibilidad)
                string oracleIdsPath = Path.Combine(basePath, "mlogis_oracle_ids.json");
                File.WriteAllText(oracleIdsPath, JsonConvert.SerializeObject(idsOracle.ToList(), Formatting.Indented));
                string historyPath = Path.Combine(_rutaLogs, "ids_history.json");
                if (!File.Exists(historyPath)) return;

                var historia = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(File.ReadAllText(historyPath));

                // Identificar IDs faltantes FUERA del delay
                int delayMin = configuracion.DelayComparacionMinutos;
                var idsFaltantesEnOracle = historia
                    .Where(kv => !idsOracle.Contains(kv.Key)) 
                    .Where(kv => (DateTime.Now - kv.Value).TotalMinutes >= delayMin) 
                    .Select(kv => kv.Key)
                    .ToList();

                string statusPath = Path.Combine(_rutaLogs, "status.json");
                var statusGlobal = File.Exists(statusPath)
                    ? JsonConvert.DeserializeObject<Dictionary<string, ConsultaStatus>>(File.ReadAllText(statusPath)) ?? new Dictionary<string, ConsultaStatus>()
                    : new Dictionary<string, ConsultaStatus>();

                if (!statusGlobal.ContainsKey(consulta.Nombre)) statusGlobal[consulta.Nombre] = new ConsultaStatus();
                var itemsPrevios = statusGlobal[consulta.Nombre].ItemsEnError;

                var nuevosErrores = idsFaltantesEnOracle.Where(id => !itemsPrevios.ContainsKey(id)).ToList();
                var resueltos = itemsPrevios.Keys.Where(id => idsOracle.Contains(id)).ToList();

                if (!nuevosErrores.Any() && !resueltos.Any())
                {
                    EscribirLog("✔️ Comparación Mlogis: Sin novedades.");
                    return;
                }

                foreach (var id in nuevosErrores) itemsPrevios[id] = new Dictionary<string, string> { { "ID", id } };
                foreach (var id in resueltos) itemsPrevios.Remove(id);
                File.WriteAllText(statusPath, JsonConvert.SerializeObject(statusGlobal, Formatting.Indented));

                consulta.DetallesErrores = idsFaltantesEnOracle.Select(id => new Dictionary<string, string> { { "ID", id } }).ToList();
                consulta.DetallesResueltos = resueltos.Select(id => new Dictionary<string, string> { { "ID", id } }).ToList();

                string rutaExcelAdjunto = null;
                if (idsFaltantesEnOracle.Count > 0)
                {
                    DataTable dtDiferencias = new DataTable("Diferencias");
                    dtDiferencias.Columns.Add("ID_Mlogis_Azure", typeof(string));
                    dtDiferencias.Columns.Add("Fecha_Deteccion", typeof(string));

                    foreach (var id in idsFaltantesEnOracle)
                        dtDiferencias.Rows.Add(id, DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));

                    string nombreArchivo = $"Mlogis_Diferencias_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                    rutaExcelAdjunto = Path.Combine(configuracion.RutaExcel, nombreArchivo);
                    
                    GuardarExcel(dtDiferencias, rutaExcelAdjunto, "Mlogis_Diferencias");
                    EscribirLog($"📦 Reporte de diferencias generado: {nombreArchivo}");
                }

                if (consulta.EnviarCorreo)
                    EnviarCorreoTracking(consulta, rutaExcelAdjunto, idsFaltantesEnOracle.Count > 0, idsFaltantesEnOracle, resueltos);
            }
            catch (Exception ex)
            {
                EscribirLog("❌ Error en EjecutarComparacionMlogis: " + ex.Message);
            }
        }

        private void EjecutarConsultaIndividual(OracleConnection conexion, ConsultaSQL consulta)
        {
            string sqlPath = Path.Combine(configuracion.RutaSQL, consulta.Archivo);

            if (!File.Exists(sqlPath))
            {
                EscribirLog($"Archivo SQL no encontrado: {sqlPath}");
                return;
            }

            EscribirLog($"Ejecutando SQL: {consulta.Nombre} ({consulta.Archivo})");

            string sql = File.ReadAllText(sqlPath);
            DataTable tabla = EjecutarConsulta(conexion, sql);

            // =====================================================
            //  MAPA ID → METADATA (CTG u otros)
            // =====================================================
            var mapaIdMetadata = new Dictionary<string, Dictionary<string, string>>();

            foreach (DataRow r in tabla.Rows)
            {
                string id = r[consulta.CampoTrack]?.ToString();
                if (string.IsNullOrWhiteSpace(id)) continue;

                if (!mapaIdMetadata.ContainsKey(id))
                    mapaIdMetadata[id] = new Dictionary<string, string>();

                if (tabla.Columns.Contains("CTG"))
                    mapaIdMetadata[id]["CTG"] = r["CTG"]?.ToString() ?? "";
            }

            // ============================
            //   TRACK = FALSE
            // ============================
            if (!consulta.Track)
            {
                if (tabla.Rows.Count == 0)
                {
                    EscribirLog($"Consulta {consulta.Nombre} → 0 filas. No genera Excel.");
                    return;
                }

                string nombreArchivo = $"Reporte_{consulta.Nombre}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                string rutaArchivo = Path.Combine(configuracion.RutaExcel, nombreArchivo);

                DataTable excelData = tabla.Copy();
                if (consulta.ExcluirCampos != null)
                {
                    foreach (string col in consulta.ExcluirCampos)
                        if (excelData.Columns.Contains(col))
                            excelData.Columns.Remove(col);
                }

                GuardarExcel(excelData, rutaArchivo, consulta.Nombre);

                if (consulta.EnviarCorreo)
                    EnviarCorreoIndividual(rutaArchivo, consulta);

                return;
            }

            // ============================
            //   TRACK = TRUE
            // ============================
            if (string.IsNullOrWhiteSpace(consulta.CampoTrack))
            {
                EscribirLog($"ERROR: {consulta.Nombre} tiene Track=true sin CampoTrack");
                return;
            }

            var idsActuales = tabla.Rows
                .Cast<DataRow>()
                .Select(r => r[consulta.CampoTrack]?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            // ============================
            //   STATUS.JSON
            // ============================
            string statusPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "status.json");

            var statusGlobal = File.Exists(statusPath)
                ? JsonConvert.DeserializeObject<Dictionary<string, ConsultaStatus>>(File.ReadAllText(statusPath))
                    ?? new Dictionary<string, ConsultaStatus>()
                : new Dictionary<string, ConsultaStatus>();

            if (!statusGlobal.ContainsKey(consulta.Nombre))
                statusGlobal[consulta.Nombre] = new ConsultaStatus();

            var itemsPrevios = statusGlobal[consulta.Nombre].ItemsEnError;
            var idsPrevios = itemsPrevios.Keys.ToList();

            var nuevos = idsActuales.Except(idsPrevios).ToList();
            var resueltos = idsPrevios.Except(idsActuales).ToList();

            if (!nuevos.Any() && !resueltos.Any())
            {
                EscribirLog($"[{consulta.Nombre}] Sin cambios → no se envía correo.");
                return;
            }

            // ============================
            //   PERSISTIR NUEVOS ERRORES
            // ============================
            foreach (var id in nuevos)
            {
                if (!itemsPrevios.ContainsKey(id))
                {
                    itemsPrevios[id] = new Dictionary<string, string>();

                    if (mapaIdMetadata.ContainsKey(id))
                    {
                        foreach (var kv in mapaIdMetadata[id])
                            itemsPrevios[id][kv.Key] = kv.Value;
                    }
                }
            }

            // ============================
            //   DETALLES ERRORES
            // ============================
            var detallesErrores = new List<Dictionary<string, string>>();
            var camposErrores = consulta.CamposCorreo?.Errores ?? new List<string>();

            foreach (DataRow r in tabla.Rows)
            {
                string id = r[consulta.CampoTrack]?.ToString();
                if (!idsActuales.Contains(id)) continue;

                var detalle = new Dictionary<string, string>();
                foreach (var col in camposErrores)
                {
                    if (tabla.Columns.Contains(col))
                        detalle[col] = r[col]?.ToString() ?? "";
                    else if (itemsPrevios.ContainsKey(id) && itemsPrevios[id].ContainsKey(col))
                        detalle[col] = itemsPrevios[id][col];
                    else
                        detalle[col] = "";
                }

                detallesErrores.Add(detalle);
            }

            // ============================
            //   DETALLES RESUELTOS
            // ============================
            var detallesResueltos = new List<Dictionary<string, string>>();
            var camposResueltos = consulta.CamposCorreo?.Resueltos ?? new List<string>();

            foreach (var id in resueltos)
            {
                var detalle = new Dictionary<string, string>();

                if (itemsPrevios.ContainsKey(id))
                {
                    foreach (var col in camposResueltos)
                    {
                        if (itemsPrevios[id].ContainsKey(col))
                            detalle[col] = itemsPrevios[id][col];
                        else if (col.Equals(consulta.CampoTrack, StringComparison.OrdinalIgnoreCase))
                            detalle[col] = id;
                        else
                            detalle[col] = "";
                    }
                }

                detallesResueltos.Add(detalle);
            }

            consulta.DetallesErrores = detallesErrores;
            consulta.DetallesResueltos = detallesResueltos;

            // ============================
            //   ENVÍO ÚNICO DE MAIL
            // ============================
            if (consulta.EnviarCorreo)
            {
                bool hayErrores = idsActuales.Any();
                string rutaArchivo = null;

                if (hayErrores)
                {
                    string archivo = $"Reporte_{consulta.Nombre}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                    rutaArchivo = Path.Combine(configuracion.RutaExcel, archivo);

                    DataTable excelData = tabla.Copy();
                    if (consulta.ExcluirCampos != null)
                    {
                        foreach (string col in consulta.ExcluirCampos)
                            if (excelData.Columns.Contains(col))
                                excelData.Columns.Remove(col);
                    }

                    GuardarExcel(excelData, rutaArchivo, consulta.Nombre);
                }

                EnviarCorreoTracking(
                    consulta,
                    rutaArchivo,
                    hayErrores,
                    idsActuales,
                    resueltos
                );
            }

            // ============================
            //   LIMPIAR RESUELTOS
            // ============================
            foreach (var id in resueltos)
                itemsPrevios.Remove(id);

            File.WriteAllText(statusPath, JsonConvert.SerializeObject(statusGlobal, Formatting.Indented));

            EscribirLog($"[{consulta.Nombre}] status.json actualizado.");
        }



        private DataTable EjecutarConsulta(OracleConnection conexion, string sql)
        {
            using (var cmd = new OracleCommand(sql, conexion))
            using (var da = new OracleDataAdapter(cmd))
            {
                DataTable dt = new DataTable();
                da.Fill(dt);
                return dt;
            }
        }

        private OracleConnection AbrirConexionOracleConCircuitBreaker(string origen)
        {
            if (_oracleCircuitBreaker == null)
            {
                var directa = new OracleConnection(configuracion.ConnectionString);
                directa.Open();
                return directa;
            }

            var decision = _oracleCircuitBreaker.EvaluarAcceso(DateTime.Now);
            if (!decision.Permitir)
            {
                EscribirLog($"⚠️ [CircuitBreaker] {origen}: ejecución omitida. {decision.Motivo}");
                IntentarEnviarAlertaCaidaCircuitBreaker(decision.Estado?.FallosConsecutivos ?? 0, origen);
                return null;
            }

            if (decision.RequierePrueba && !EjecutarPruebaHalfOpen(origen))
            {
                EscribirLog($"⚠️ [CircuitBreaker] {origen}: prueba HALF-OPEN falló. Operación Oracle omitida.");
                return null;
            }

            OracleConnection conexion = null;
            try
            {
                conexion = new OracleConnection(configuracion.ConnectionString);
                conexion.Open();

                var transition = _oracleCircuitBreaker.RegistrarConexionExitosa(DateTime.Now, esPruebaHalfOpen: false);
                ProcesarTransicionCircuitBreaker(transition, origen);
                return conexion;
            }
            catch (Exception ex)
            {
                conexion?.Dispose();
                var transition = _oracleCircuitBreaker.RegistrarFalloConexion(DateTime.Now, esPruebaHalfOpen: false);
                EscribirLog($"❌ [CircuitBreaker] {origen}: fallo de conexión Oracle: {ex.Message}");
                ProcesarTransicionCircuitBreaker(transition, origen);
                return null;
            }
        }

        private bool EjecutarPruebaHalfOpen(string origen)
        {
            EscribirLog($"🧪 [CircuitBreaker] {origen}: ejecutando prueba HALF-OPEN (SELECT 1 FROM DUAL).");
            try
            {
                using (var conexion = new OracleConnection(configuracion.ConnectionString))
                {
                    conexion.Open();
                    using (var cmd = new OracleCommand("SELECT 1 FROM DUAL", conexion))
                    {
                        cmd.ExecuteScalar();
                    }
                }

                var transition = _oracleCircuitBreaker.RegistrarConexionExitosa(DateTime.Now, esPruebaHalfOpen: true);
                ProcesarTransicionCircuitBreaker(transition, origen);
                return true;
            }
            catch (Exception ex)
            {
                var transition = _oracleCircuitBreaker.RegistrarFalloConexion(DateTime.Now, esPruebaHalfOpen: true);
                EscribirLog($"❌ [CircuitBreaker] {origen}: prueba HALF-OPEN fallida: {ex.Message}");
                ProcesarTransicionCircuitBreaker(transition, origen);
                return false;
            }
        }

        private void ProcesarTransicionCircuitBreaker(OracleCircuitTransition transition, string origen)
        {
            if (transition == null) return;

            if (transition.DebeAlertarCaido)
                IntentarEnviarAlertaCaidaCircuitBreaker(transition.FallosConsecutivos, origen);

            if (transition.DebeAlertarRecuperado)
                EnviarMailCircuitBreaker(esRecuperacion: true, transition.FallosConsecutivos);
        }

        private void IntentarEnviarAlertaCaidaCircuitBreaker(int fallosConsecutivos, string origen)
        {
            if (_oracleCircuitBreaker == null) return;
            if (!_oracleCircuitBreaker.IntentarMarcarAlertaCaidaEnviada()) return;

            EscribirLog($"📣 [CircuitBreaker] Alerta de circuito OPEN habilitada para envío (origen={origen}, fallos={fallosConsecutivos}).");
            EnviarMailCircuitBreaker(esRecuperacion: false, fallosConsecutivos: fallosConsecutivos);
        }

        private void EnviarMailCircuitBreaker(bool esRecuperacion, int fallosConsecutivos)
        {
            try
            {
                var cb = configuracion.CircuitBreakerAlerta ?? new CircuitBreakerAlertaConfig();
                var destinatarios = cb.Destinatarios;
                if (destinatarios == null || destinatarios.Count == 0)
                {
                    EscribirLog("⚠️ [CircuitBreaker] CircuitBreakerAlerta.Destinatarios está vacío. Mail omitido.");
                    return;
                }

                DateTime ahora = DateTime.Now;
                string empresa = configuracion.Empresa ?? "";
                string fecha = ahora.ToString("dd/MM/yyyy HH:mm");
                string timestamp = ahora.ToString("dd/MM/yyyy HH:mm:ss");
                string fallos = fallosConsecutivos.ToString();

                string asuntoTemplate = esRecuperacion ? cb.AsuntoRecuperado : cb.AsuntoCaido;
                string cuerpoTemplate = esRecuperacion ? cb.CuerpoRecuperado : cb.CuerpoCaido;

                string asunto = (asuntoTemplate ?? "")
                    .Replace("{Empresa}", empresa)
                    .Replace("{Fecha}", fecha)
                    .Replace("{Timestamp}", timestamp)
                    .Replace("{FallosConsecutivos}", fallos);

                string cuerpo = (cuerpoTemplate ?? "")
                    .Replace("{Empresa}", empresa)
                    .Replace("{Fecha}", fecha)
                    .Replace("{Timestamp}", timestamp)
                    .Replace("{FallosConsecutivos}", fallos);

                string claveSMTP = CryptoHelper.IsEncrypted(configuracion.ClaveSMTP)
                    ? CryptoHelper.Decrypt(configuracion.ClaveSMTP)
                    : configuracion.ClaveSMTP;

                var destsUnicos = new HashSet<string>(
                    destinatarios.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                using (var cliente = new SmtpClient(configuracion.ServidorSMTP, configuracion.PuertoSMTP))
                {
                    cliente.Credentials = new NetworkCredential(configuracion.UsuarioSMTP, claveSMTP);
                    cliente.EnableSsl = true;

                    using (var mensaje = new MailMessage())
                    {
                        mensaje.From = new MailAddress(configuracion.Remitente);
                        mensaje.Subject = asunto;
                        mensaje.Body = cuerpo;
                        mensaje.IsBodyHtml = false;
                        foreach (var dest in destsUnicos)
                            mensaje.To.Add(dest);
                        if (mensaje.To.Count == 0)
                        {
                            EscribirLog("⚠️ [CircuitBreaker] Sin destinatarios válidos. Mail omitido.");
                            return;
                        }
                        cliente.Send(mensaje);
                    }
                }

                EscribirLog($"📧 [CircuitBreaker] Mail de {(esRecuperacion ? "recuperación" : "caída")} enviado a {string.Join(", ", destsUnicos)}");
            }
            catch (Exception ex)
            {
                EscribirLog($"❌ [CircuitBreaker] Error enviando mail: {ex.Message}");
            }
        }

        private void GuardarExcel(DataTable tabla, string ruta, string hoja)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(tabla, hoja);
                ws.Columns().AdjustToContents();
                wb.SaveAs(ruta);
            }
        }

        private void EnviarCorreoIndividual(string archivo, ConsultaSQL consulta)
        {
            try
            {
                using (var cliente = new SmtpClient(configuracion.ServidorSMTP, configuracion.PuertoSMTP))
                {
                    cliente.Credentials = new NetworkCredential(configuracion.UsuarioSMTP, configuracion.ClaveSMTP);
                    cliente.EnableSsl = true;

                    using (var mensaje = new MailMessage())
                    {
                        mensaje.From = new MailAddress(configuracion.Remitente);
                        mensaje.Subject = AgregarPrefixEmpresa($"Reporte automático: {consulta.Nombre}");
                        mensaje.Body = $"Se adjunta reporte correspondiente a {consulta.Nombre}.";
                        mensaje.IsBodyHtml = false;

                        foreach (var dest in consulta.Destinatarios)
                            mensaje.To.Add(dest);

                        mensaje.Attachments.Add(new Attachment(archivo));
                        cliente.Send(mensaje);

                        // 🔥 LOG COMPLETO POR CONSULTA
                        EscribirLog($"[ENVÍO INDIVIDUAL] Consulta '{consulta.Nombre}' enviada OK. Dest: {string.Join(", ", consulta.Destinatarios)} | Archivo: {archivo}");
                    }
                }
            }
            catch (Exception ex)
            {
                EscribirLog($"[ENVÍO INDIVIDUAL] ERROR al enviar consulta {consulta.Nombre}: {ex.Message}");
            }
        }
        private string AgregarPrefixEmpresa(string asunto)
        {
            if (string.IsNullOrWhiteSpace(configuracion?.Empresa)) return asunto;
            return $"[{configuracion.Empresa}] {asunto}";
        }

        private string NormalizarSaltos(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            return s.Replace("\r\n", "\n")
                    .Replace("\r", "\n");
        }


        private void EnviarCorreoTracking(
    ConsultaSQL consulta,
    string rutaArchivo,
    bool hayErrores,
    List<string> ids,
    List<string> resueltos)
        {
            try
            {
                using (var cliente = new SmtpClient(configuracion.ServidorSMTP, configuracion.PuertoSMTP))
                {
                    cliente.Credentials = new NetworkCredential(configuracion.UsuarioSMTP, configuracion.ClaveSMTP);
                    cliente.EnableSsl = true;

                    using (var mensaje = new MailMessage())
                    {
                        mensaje.From = new MailAddress(configuracion.Remitente);

                        // ======================
                        //  ASUNTO DINÁMICO
                        // ======================
                        string asunto = hayErrores
                            ? consulta.Mail?.AsuntoConError
                            : consulta.Mail?.AsuntoSinError;

                        if (string.IsNullOrWhiteSpace(asunto))
                        {
                            asunto = hayErrores
                                ? $"⚠️ Observaciones detectadas – {consulta.Nombre} – {DateTime.Now:dd/MM/yyyy HH:mm}"
                                : $"✔️ Sin novedades – {consulta.Nombre} – {DateTime.Now:dd/MM/yyyy HH:mm}";
                        }

                        mensaje.Subject = AgregarPrefixEmpresa(asunto
                            .Replace("{Nombre}", consulta.Nombre)
                            .Replace("{Fecha}", DateTime.Now.ToString("dd/MM/yyyy HH:mm")));

                        // ======================
                        //  DETALLES (JSON)
                        // ======================
                        var camposErr = consulta.CamposCorreo?.Errores ?? new List<string>();
                        var camposRes = consulta.CamposCorreo?.Resueltos ?? new List<string>();

                        // Errores
                        string detalleErrores = "";
                        if (consulta.DetallesErrores != null && consulta.DetallesErrores.Any())
                        {
                            foreach (var fila in consulta.DetallesErrores)
                            {
                                foreach (var col in camposErr)
                                {
                                    fila.TryGetValue(col, out var val);
                                    detalleErrores += $"{col}: {val}   ";
                                }
                                detalleErrores += "\n";
                            }
                        }

                        // Resueltos
                        string detalleResueltos = "";
                        if (consulta.DetallesResueltos != null && consulta.DetallesResueltos.Any())
                        {
                            foreach (var fila in consulta.DetallesResueltos)
                            {
                                foreach (var col in camposRes)
                                {
                                    fila.TryGetValue(col, out var val);
                                    detalleResueltos += $"{col}: {val}   ";
                                }
                                detalleResueltos += "\n";
                            }
                        }
                        // ======================
                        //  FALLBACK ERRORES
                        // ======================
                        if (hayErrores && string.IsNullOrWhiteSpace(detalleErrores))
                        {
                            // Mostrar al menos el ID (CampoTrack)
                            detalleErrores = string.Join("\n",
                                ids.Select(id => $"- {consulta.CampoTrack}: {id}")
                            );
                        }
                        // ======================
                        //  FALLBACK RESUELTOS
                        // ======================
                        if (resueltos.Any() && string.IsNullOrWhiteSpace(detalleResueltos))
                        {
                            detalleResueltos = string.Join("\n",
                                resueltos.Select(id => $"- {consulta.CampoTrack}: {id}")
                            );
                        }
                        


                        // ======================
                        //   ARMADO DEL CUERPO
                        // ======================
                        string cuerpo = hayErrores
                            ? consulta.Mail?.CuerpoConError
                            : consulta.Mail?.CuerpoSinError;

                        if (!string.IsNullOrWhiteSpace(cuerpo))
                        {
                            cuerpo = cuerpo
                                .Replace("{Nombre}", consulta.Nombre)
                                .Replace("{Fecha}", DateTime.Now.ToString("dd/MM/yyyy HH:mm"))
                                .Replace("{Cantidad}", ids.Count.ToString())
                                .Replace("{Lista}", detalleErrores)
                                .Replace("{Resueltos}", detalleResueltos);
                        }
                        else
                        {
                            cuerpo = hayErrores
                                ? $"Se detectaron errores en {consulta.Nombre}:\n\n{detalleErrores}"
                                : $"No quedan observaciones en {consulta.Nombre}.\n\n{detalleResueltos}";
                        }

                        // ======================
                        //  LIMPIEZA ROBUSTA DE SECCIÓN RESUELTOS
                        // ======================
                        cuerpo = NormalizarSaltos(cuerpo);

                        if (string.IsNullOrWhiteSpace(detalleResueltos))
                        {
                            // Quitar placeholder
                            cuerpo = cuerpo.Replace("{Resueltos}", "");

                            // Eliminar línea completa que contenga "resueltos"
                            var lineas = cuerpo.Split('\n').ToList();

                            lineas.RemoveAll(l =>
                                l.Trim().IndexOf("resueltos", StringComparison.OrdinalIgnoreCase) >= 0
                            );

                            cuerpo = string.Join("\n", lineas);

                            // Compactar blancos
                            while (cuerpo.Contains("\n\n\n"))
                                cuerpo = cuerpo.Replace("\n\n\n", "\n\n");

                            cuerpo = cuerpo.Trim();
                        }

                        EscribirLog($"[MAIL DEBUG] {consulta.Nombre}\n{cuerpo}");

                        mensaje.Body = cuerpo;
                        mensaje.IsBodyHtml = false;

                        // Destinatarios
                        if (consulta.Destinatarios != null)
                        {
                            foreach (var d in consulta.Destinatarios)
                                mensaje.To.Add(d);
                        }

                        // Adjuntar archivo si corresponde
                        if (!string.IsNullOrWhiteSpace(rutaArchivo) && File.Exists(rutaArchivo))
                            mensaje.Attachments.Add(new Attachment(rutaArchivo));

                        // Enviar mail
                        cliente.Send(mensaje);

                        EscribirLog($"[TRACKING] Correo enviado OK → {consulta.Nombre}. Estado: {(hayErrores ? "Con errores" : "Sin errores")}");
                    }
                }
            }
            catch (Exception ex)
            {
                EscribirLog($"[TRACKING] ERROR al enviar correo para '{consulta.Nombre}': {ex.Message}");
            }
        }



        private static readonly string[] _diasSemana = { "Domingo", "Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado" };

        private void EscribirLog(string mensaje)
        {
            try
            {
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string logsFolder = Path.Combine(basePath, "Logs");
                Directory.CreateDirectory(logsFolder);

                string diaNombre = _diasSemana[(int)DateTime.Today.DayOfWeek];
                string rutaLog = Path.Combine(logsFolder, $"Log_{diaNombre}.txt");

                // Rotación semanal: si el archivo existe pero NO es de hoy, eliminarlo
                if (File.Exists(rutaLog) && File.GetLastWriteTime(rutaLog).Date != DateTime.Today)
                    File.Delete(rutaLog);

                File.AppendAllText(rutaLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {mensaje}{Environment.NewLine}");
            }
            catch { }
        }

        private static readonly string[] _clavesObsoletas = { "DiaEjecucion", "HoraEjecucion", "Destinatarios", "AsuntoCorreo", "CuerpoCorreo", "EnviarCorreo" };

        private void MigrarConfigSiFaltan(string configPath)
        {
            try
            {
                var defaults = JObject.FromObject(new Configuracion());
                var actual = JObject.Parse(File.ReadAllText(configPath));
                var agregados = new List<string>();
                var eliminados = new List<string>();

                // Agregar atributos faltantes (incluye objetos anidados)
                AgregarPropiedadesFaltantesRecursivo(actual, defaults, agregados, prefijo: "");

                // Eliminar claves obsoletas
                foreach (var clave in _clavesObsoletas)
                {
                    if (actual.ContainsKey(clave))
                    {
                        actual.Remove(clave);
                        eliminados.Add(clave);
                    }
                }

                if (agregados.Count > 0 || eliminados.Count > 0)
                {
                    File.WriteAllText(configPath, actual.ToString(Formatting.Indented));
                    if (agregados.Count > 0)
                        EscribirLog($"🔧 Config.json migrado: atributos agregados: {string.Join(", ", agregados)}");
                    if (eliminados.Count > 0)
                        EscribirLog($"🧹 Config.json saneado: claves obsoletas eliminadas: {string.Join(", ", eliminados)}");
                    // Recargar con los nuevos valores
                    configuracion = JsonConvert.DeserializeObject<Configuracion>(File.ReadAllText(configPath));
                }
            }
            catch (Exception ex)
            {
                EscribirLog("⚠️ Error en migración de Config.json: " + ex.Message);
            }
        }

        private void AgregarPropiedadesFaltantesRecursivo(JObject actual, JObject defaults, List<string> agregados, string prefijo)
        {
            foreach (var prop in defaults.Properties())
            {
                string ruta = string.IsNullOrWhiteSpace(prefijo) ? prop.Name : $"{prefijo}.{prop.Name}";
                JToken existente;
                bool existe = actual.TryGetValue(prop.Name, out existente);

                if (!existe || existente == null || existente.Type == JTokenType.Null)
                {
                    actual[prop.Name] = prop.Value.DeepClone();
                    agregados.Add(ruta);
                    continue;
                }

                if (prop.Value is JObject defObj && existente is JObject actObj)
                {
                    AgregarPropiedadesFaltantesRecursivo(actObj, defObj, agregados, ruta);
                }
            }
        }

        private void IniciarFileWatcher(string carpeta)
        {
            try
            {
                _fileWatcher = new FileSystemWatcher(carpeta)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _fileWatcher.Changed += OnArchivoModificado;
            }
            catch (Exception ex)
            {
                EscribirLog("⚠️ No se pudo iniciar FileSystemWatcher: " + ex.Message);
            }
        }

        private void OnArchivoModificado(object sender, FileSystemEventArgs e)
        {
            string nombre = Path.GetFileName(e.Name);

            if (string.Equals(nombre, "config.json", StringComparison.OrdinalIgnoreCase))
            {
                _debounceConfig?.Dispose();
                _debounceConfig = new System.Threading.Timer(_ => RecargarConfig(), null, 500, System.Threading.Timeout.Infinite);
            }
            else if (string.Equals(nombre, "consultas.json", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(nombre, "Consultas.json", StringComparison.OrdinalIgnoreCase))
            {
                _debounceConsultas?.Dispose();
                _debounceConsultas = new System.Threading.Timer(_ => RecargarConsultas(), null, 500, System.Threading.Timeout.Infinite);
            }
        }

        private void RecargarConfig()
        {
            try
            {
                var nueva = JsonConvert.DeserializeObject<Configuracion>(File.ReadAllText(_configPath));
                if (nueva == null) return;
                nueva.ClaveSMTP = CryptoHelper.Decrypt(nueva.ClaveSMTP);
                lock (lockObj)
                {
                    configuracion = nueva;
                    _oracleCircuitBreaker?.ActualizarPolitica(
                        configuracion.CircuitBreakerUmbral,
                        configuracion.CircuitBreakerTimeoutMinutos);
                }
                EscribirLog("🔄 Config.json recargado en caliente.");
            }
            catch (Exception ex)
            {
                EscribirLog("⚠️ Error al recargar Config.json: " + ex.Message);
            }
        }

        private void RecargarConsultas()
        {
            try
            {
                var nuevas = JsonConvert.DeserializeObject<List<ConsultaSQL>>(File.ReadAllText(_consultasPath));
                if (nuevas == null) return;
                lock (lockObj)
                {
                    consultas = nuevas;
                    ultimoWriteTimeConsultas = File.GetLastWriteTime(_consultasPath);
                }
                EscribirLog("🔄 Consultas.json recargado en caliente.");
            }
            catch (Exception ex)
            {
                EscribirLog("⚠️ Error al recargar Consultas.json: " + ex.Message);
            }
        }

        protected override void OnStop()
        {
            timer?.Stop();
            timer?.Dispose();
            _fileWatcher?.Dispose();
            _debounceConfig?.Dispose();
            _debounceConsultas?.Dispose();
            EscribirLog("Servicio detenido.");
        }
        private string BuildFilterIn(string column, string values)
        {
            if (string.IsNullOrWhiteSpace(values)) return "";
            var list = values.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(v => $"'{v.Trim()}'");
            return $" AND {column} IN ({string.Join(",", list)})";
        }

        // ── Migración de archivos operativos de raíz a Logs\ ─────────────────
        private void MigrarArchivosOperativos()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string[] archivosOperativos =
            {
                "mlogis_historial.json",
                "comparaciones_pendientes.json",
                "alertas_oracle_enviadas.json",
                "ids_history.json",
                "status.json",
                "pendientes_alerta_estado.json"
            };

            foreach (var archivo in archivosOperativos)
            {
                string origen  = Path.Combine(basePath, archivo);
                string destino = Path.Combine(_rutaLogs, archivo);

                if (File.Exists(origen) && !File.Exists(destino))
                {
                    try
                    {
                        File.Move(origen, destino);
                        EscribirLog($"📁 [Migración] {archivo} movido a Logs\\");
                    }
                    catch (Exception ex)
                    {
                        EscribirLog($"⚠️ [Migración] Error al mover {archivo}: {ex.Message}");
                    }
                }
            }
        }

        // ── Health check del WebService SOAP ─────────────────────────────────
        private async Task<bool> VerificarWebService()
        {
            string wsEstadoPath = Path.Combine(_rutaLogs, "ws_estado.json");

            WsEstado estado;
            try
            {
                estado = File.Exists(wsEstadoPath)
                    ? JsonConvert.DeserializeObject<WsEstado>(File.ReadAllText(wsEstadoPath)) ?? new WsEstado()
                    : new WsEstado();
            }
            catch { estado = new WsEstado(); }

            bool wsDisponible = await Task.Run(() =>
            {
                // Paso 1: verificar UrlAutentificacion (si está configurada)
                if (!string.IsNullOrWhiteSpace(configuracion.UrlAutentificacion))
                {
                    try
                    {
                        var reqAuth = (HttpWebRequest)WebRequest.Create(configuracion.UrlAutentificacion);
                        reqAuth.Method  = "HEAD";
                        reqAuth.Timeout = 60000;
                        using (reqAuth.GetResponse()) { /* responde → continuar */ }
                    }
                    catch (WebException ex)
                    {
                        // Sin respuesta HTTP → servidor no disponible
                        if (!(ex.Response is HttpWebResponse))
                        {
                            EscribirLog($"⚠️ [WS] UrlAutentificacion no responde: {configuracion.UrlAutentificacion}");
                            return false;
                        }
                        // Con respuesta HTTP (4xx/5xx) → el servidor responde, seguir al paso 2
                    }
                    catch
                    {
                        EscribirLog($"⚠️ [WS] Error al verificar UrlAutentificacion: {configuracion.UrlAutentificacion}");
                        return false;
                    }
                }

                // Paso 2: verificar UrlWS
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(configuracion.UrlWS);
                    req.Method  = "HEAD";
                    req.Timeout = 60000;
                    using (var resp = req.GetResponse()) { return true; }
                }
                catch (WebException ex)
                {
                    // Una respuesta HTTP (aunque sea error 4xx/5xx) significa que el servidor responde
                    return ex.Response is HttpWebResponse;
                }
                catch { return false; }
            });

            DateTime ahora = DateTime.Now;

            // v6.4 — Reset contadores diarios si cambió el día
            if (!estado.FechaContadores.HasValue || estado.FechaContadores.Value.Date < DateTime.Today)
            {
                estado.CaidasHoy          = 0;
                estado.RecuperacionesHoy  = 0;
                estado.FechaContadores    = DateTime.Today;
                estado.AlertaCaidaEnviada = false; // v7.1.1 — resetear flag al cambiar de día
            }

            if (!wsDisponible)
            {
                // Si el tipo de error cambió, resetear flag para enviar nuevo mail
                if (!string.Equals(estado.UltimoEstado, "caido", StringComparison.OrdinalIgnoreCase))
                    estado.AlertaCaidaEnviada = false;

                estado.UltimoEstado = "caido";
                estado.DetalleError = "Sin respuesta HTTP (Timeout o servidor no disponible)";
                estado.UltimaVezCaido = ahora; // v7.1.1 — actualizar siempre, no solo en la primera caída

                estado.CaidasHoy++;
                AgregarEventoHistorial(estado, "caida", estado.DetalleError, ahora);

                if (!estado.AlertaCaidaEnviada)
                {
                    bool mailEnviado = EnviarMailWS(esRecuperacion: false, estado: estado);
                    if (mailEnviado)
                    {
                        estado.AlertaCaidaEnviada = true;
                        EscribirLog($"⚠️ [WS] WebService SOAP no disponible. Alerta enviada. Caídas hoy: {estado.CaidasHoy}");
                    }
                    else
                    {
                        EscribirLog($"⚠️ [WS] WebService SOAP no disponible. FALLO al enviar alerta SMTP. Se reintentará en la próxima corrida. Caídas hoy: {estado.CaidasHoy}");
                    }
                }
                else
                {
                    EscribirLog($"⚠️ [WS] WebService SOAP no disponible (alerta ya enviada). Corrida SOAP omitida. Caídas hoy: {estado.CaidasHoy}");
                }

                GuardarWsEstado(wsEstadoPath, estado);
                return false;
            }

            // Paso 3: verificar autenticación SOAP (Error de Negocio)
            var (authOk, authDetalle, authXml) = await VerificarAutenticacionSoapAsync();
            if (!authOk)
            {
                // Si el tipo de error cambió, resetear flag para enviar nuevo mail
                if (!string.Equals(estado.UltimoEstado, "auth_error", StringComparison.OrdinalIgnoreCase))
                    estado.AlertaCaidaEnviada = false;

                estado.UltimoEstado   = "auth_error";
                estado.DetalleError   = authDetalle;
                estado.UltimoErrorXml = authXml;
                estado.UltimaVezCaido = ahora; // v7.1.1 — actualizar siempre, no solo en la primera caída

                estado.CaidasHoy++;
                AgregarEventoHistorial(estado, "caida", authDetalle, ahora);

                if (!estado.AlertaCaidaEnviada)
                {
                    bool mailEnviado = EnviarMailWS(esRecuperacion: false, estado: estado);
                    if (mailEnviado)
                    {
                        estado.AlertaCaidaEnviada = true;
                        EscribirLog($"⚠️ [WS] Error de autenticación SOAP: {authDetalle}. Alerta enviada. Caídas hoy: {estado.CaidasHoy}");
                    }
                    else
                    {
                        EscribirLog($"⚠️ [WS] Error de autenticación SOAP: {authDetalle}. FALLO al enviar alerta SMTP. Se reintentará en la próxima corrida.");
                    }
                }
                else
                {
                    EscribirLog($"⚠️ [WS] Error de autenticación SOAP (alerta ya enviada): {authDetalle}. Corrida omitida.");
                }

                GuardarWsEstado(wsEstadoPath, estado);
                return false;
            }

            // WS disponible y autenticación OK
            bool estadoPrevioEraFalla = string.Equals(estado.UltimoEstado, "caido", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(estado.UltimoEstado, "auth_error", StringComparison.OrdinalIgnoreCase);
            if (estadoPrevioEraFalla)
            {
                estado.RecuperacionesHoy++;
                AgregarEventoHistorial(estado, "recuperacion", "WS disponible y autenticación OK", ahora);

                // Anti-spam: solo enviar mail de recuperación si se había avisado de la caída
                if (estado.AlertaCaidaEnviada)
                {
                    EnviarMailWS(esRecuperacion: true, estado: estado);
                    EscribirLog($"✅ [WS] WebService SOAP recuperado. Mail enviado. Recuperaciones hoy: {estado.RecuperacionesHoy}");
                }
                else
                {
                    EscribirLog($"✅ [WS] WebService SOAP recuperado (sin alerta de caída previa, mail omitido). Recuperaciones hoy: {estado.RecuperacionesHoy}");
                }
            }

            estado.UltimoEstado        = "ok";
            estado.UltimaVezRecuperado = ahora;
            estado.AlertaCaidaEnviada  = false;
            estado.DetalleError        = null;

            GuardarWsEstado(wsEstadoPath, estado);
            return true;
        }

        private static void AgregarEventoHistorial(WsEstado estado, string evento, string detalle, DateTime ts)
        {
            if (estado.HistorialEventos == null)
                estado.HistorialEventos = new List<WsEvento>();
            estado.HistorialEventos.Add(new WsEvento { Timestamp = ts, Evento = evento, Detalle = detalle });
            // Mantener solo los últimos 100 eventos
            while (estado.HistorialEventos.Count > 100)
                estado.HistorialEventos.RemoveAt(0);
        }

        private async Task<(bool ok, string detalle, string rawXml)> VerificarAutenticacionSoapAsync()
        {
            if (string.IsNullOrWhiteSpace(configuracion.UrlAutentificacion) ||
                string.IsNullOrWhiteSpace(configuracion.Dominio))
                return (true, null, null); // Sin configuración: no bloquear

            try
            {
                string envelope =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                    "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                    "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                    "<soap:Body><LoginServiceWithPackDirect xmlns=\"DkMServer.Services\">" +
                    "<packName>dkactas</packName>" +
                    $"<domain>{configuracion.Dominio}</domain>" +
                    $"<userName>{configuracion.Dominio}</userName>" +
                    $"<userPwd>{configuracion.Dominio}</userPwd>" +
                    "</LoginServiceWithPackDirect></soap:Body></soap:Envelope>";

                string responseBody = await Task.Run(() =>
                {
                    var req = (HttpWebRequest)WebRequest.Create(configuracion.UrlAutentificacion);
                    req.Method      = "POST";
                    req.ContentType = "text/xml; charset=utf-8";
                    req.Headers.Add("SOAPAction", "\"DkMServer.Services/LoginServiceWithPackDirect\"");
                    req.Timeout     = 60000;

                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(envelope);
                    req.ContentLength = bytes.Length;
                    using (var stream = req.GetRequestStream())
                        stream.Write(bytes, 0, bytes.Length);

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var reader = new System.IO.StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
                        return reader.ReadToEnd();
                });

                string resultInner    = HcGetXmlTag(responseBody, "LoginServiceWithPackDirectResult");
                string unescaped      = HcUnescapeXml(resultInner);
                string loginSucceeded = HcGetXmlTag(unescaped, "LoginSucceeded");
                string resultCode     = HcGetXmlTag(unescaped, "ResultCode");
                string resultMsg      = HcGetXmlTag(unescaped, "ResultMessage");

                if (string.Equals(loginSucceeded, "false", StringComparison.OrdinalIgnoreCase))
                {
                    // Loguear XML completo para diagnóstico (truncado a 800 chars)
                    string xmlLog = unescaped?.Length > 800 ? unescaped.Substring(0, 800) + "..." : unescaped;
                    EscribirLog($"🔍 [WS-Auth] XML respuesta: {xmlLog}");

                    // Detalle compacto para el mail
                    string detalle = string.IsNullOrEmpty(resultCode)
                        ? "LoginSucceeded=false"
                        : string.IsNullOrEmpty(resultMsg)
                            ? $"LoginSucceeded=false, ResultCode={resultCode}"
                            : $"LoginSucceeded=false, ResultCode={resultCode}, Mensaje={resultMsg}";
                    // Guardar XML truncado para auditoría en ws_estado.json
                    string xmlAudit = unescaped?.Length > 1000 ? unescaped.Substring(0, 1000) + "..." : unescaped;
                    return (false, detalle, xmlAudit);
                }

                string token = HcGetXmlTag(unescaped, "UserToken");
                if (string.IsNullOrEmpty(token))
                    return (false, "No se encontró UserToken en la respuesta SOAP", null);

                return (true, null, null);
            }
            catch (Exception ex)
            {
                return (false, $"Error en autenticación SOAP: {ex.Message}", null);
            }
        }

        private static string HcGetXmlTag(string xml, string tag)
        {
            if (string.IsNullOrEmpty(xml)) return "";
            string open = $"<{tag}>";
            int s = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (s < 0) return "";
            s += open.Length;
            int e = xml.IndexOf($"</{tag}>", s, StringComparison.OrdinalIgnoreCase);
            if (e < 0) return "";
            return xml.Substring(s, e - s).Trim();
        }

        private static string HcUnescapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("&lt;", "<").Replace("&gt;", ">")
                    .Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&apos;", "'");
        }

        private bool EnviarMailWS(bool esRecuperacion, WsEstado estado)
        {
            try
            {
                var hc = configuracion.HealthCheckSoap ?? new HealthCheckSoapConfig();
                var destinatarios = hc.Destinatarios;
                if (destinatarios == null || destinatarios.Count == 0)
                {
                    EscribirLog("⚠️ [WS] HealthCheckSoap.Destinatarios está vacío. Mail de health check omitido.");
                    return true; // sin destinatarios: no es un fallo SMTP, no reintentar
                }

                DateTime ahora    = DateTime.Now;
                string empresa    = configuracion.Empresa ?? "";
                string fecha      = ahora.ToString("dd/MM/yyyy HH:mm");
                string timestamp  = ahora.ToString("dd/MM/yyyy HH:mm:ss");
                string caidoDesde = estado.UltimaVezCaido.HasValue
                    ? estado.UltimaVezCaido.Value.ToString("dd/MM/yyyy HH:mm:ss")
                    : "desconocido";
                string detalleError = estado.DetalleError ?? "Sin detalles adicionales";

                // v6.4 — Texto plano para evitar Safelinks en clientes de correo corporativos
                string endpointHost = "Mlogis SmartFarm";

                string asuntoTemplate = esRecuperacion ? hc.AsuntoRecuperado : hc.AsuntoCaido;
                string cuerpoTemplate = esRecuperacion ? hc.CuerpoRecuperado  : hc.CuerpoCaido;

                string asunto = (asuntoTemplate ?? "")
                    .Replace("{Empresa}", empresa)
                    .Replace("{Fecha}", fecha);

                string cuerpo = (cuerpoTemplate ?? "")
                    .Replace("{Empresa}", empresa)
                    .Replace("{EndpointHost}", endpointHost)
                    .Replace("{UrlWS}", endpointHost)        // compat con templates viejos
                    .Replace("{Timestamp}", timestamp)
                    .Replace("{UltimaVezCaido}", caidoDesde)
                    .Replace("{DetalleError}", detalleError);

                // Usar credenciales SMTP desencriptadas
                string claveSMTP = CryptoHelper.IsEncrypted(configuracion.ClaveSMTP)
                    ? CryptoHelper.Decrypt(configuracion.ClaveSMTP)
                    : configuracion.ClaveSMTP;

                // Deduplicar destinatarios (case-insensitive) para evitar duplicados del usuario
                var destsUnicos = new System.Collections.Generic.HashSet<string>(
                    destinatarios.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                using (var cliente = new SmtpClient(configuracion.ServidorSMTP, configuracion.PuertoSMTP))
                {
                    cliente.Credentials = new NetworkCredential(configuracion.UsuarioSMTP, claveSMTP);
                    cliente.EnableSsl   = true;

                    using (var mensaje = new MailMessage())
                    {
                        mensaje.From       = new MailAddress(configuracion.Remitente);
                        mensaje.Subject    = asunto;
                        mensaje.Body       = cuerpo;
                        mensaje.IsBodyHtml = false;
                        foreach (var dest in destsUnicos)
                            mensaje.To.Add(dest);
                        if (mensaje.To.Count == 0) { EscribirLog("⚠️ [WS] Sin destinatarios válidos. Mail omitido."); return true; }
                        cliente.Send(mensaje);
                    }
                }

                EscribirLog($"📧 Mail WS {(esRecuperacion ? "recuperación" : "caída")} enviado a {string.Join(", ", destsUnicos)}");
                return true;
            }
            catch (Exception ex)
            {
                EscribirLog($"❌ Error enviando mail WS: {ex.Message}");
                return false;
            }
        }

        private void GuardarWsEstado(string path, WsEstado estado)
        {
            lock (_wsEstadoLock)
            {
                try   { File.WriteAllText(path, JsonConvert.SerializeObject(estado, Formatting.Indented)); }
                catch (Exception ex) { EscribirLog($"⚠️ Error guardando ws_estado.json: {ex.Message}"); }
            }
        }

        private class PendientesAlertaEstado
        {
            [JsonProperty("ultimo_envio")]
            public DateTime? UltimoEnvio { get; set; }

            [JsonProperty("cantidad_al_enviar")]
            public int CantidadAlEnviar { get; set; }
        }

        private class WsEstado
        {
            [JsonProperty("ultimo_estado")]
            public string UltimoEstado { get; set; } = "ok";

            [JsonProperty("ultima_vez_caido")]
            public DateTime? UltimaVezCaido { get; set; }

            [JsonProperty("ultima_vez_recuperado")]
            public DateTime? UltimaVezRecuperado { get; set; }

            [JsonProperty("alerta_caida_enviada")]
            public bool AlertaCaidaEnviada { get; set; } = false;

            [JsonProperty("detalle_error")]
            public string DetalleError { get; set; }

            // v6.4 — Auditoría diaria
            [JsonProperty("caidas_hoy")]
            public int CaidasHoy { get; set; } = 0;

            [JsonProperty("recuperaciones_hoy")]
            public int RecuperacionesHoy { get; set; } = 0;

            [JsonProperty("ultimo_error_xml")]
            public string UltimoErrorXml { get; set; }

            [JsonProperty("fecha_contadores")]
            public DateTime? FechaContadores { get; set; }

            [JsonProperty("historial_eventos")]
            public List<WsEvento> HistorialEventos { get; set; } = new List<WsEvento>();
        }

        private class WsEvento
        {
            [JsonProperty("timestamp")]
            public DateTime Timestamp { get; set; }

            [JsonProperty("evento")]
            public string Evento { get; set; } // "caida" | "recuperacion"

            [JsonProperty("detalle")]
            public string Detalle { get; set; }
        }
    }
}
