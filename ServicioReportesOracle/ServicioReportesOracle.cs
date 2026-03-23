using ClosedXML.Excel;
using Newtonsoft.Json;
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

namespace ServicioOracleReportes
{
    public class ServicioOracleReportes : ServiceBase
    {
        private Timer timer;
        private Configuracion configuracion;
        private List<ConsultaSQL> consultas;
        private Dictionary<string, int> resumenFilas = new Dictionary<string, int>();
        private static readonly object lockObj = new object();
        private bool enEjecucion = false;
        private DateTime ultimoWriteTimeConsultas = DateTime.MinValue;


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
                consultas = JsonConvert.DeserializeObject<List<ConsultaSQL>>(File.ReadAllText(consultasPath));
                ultimoWriteTimeConsultas = File.GetLastWriteTime(consultasPath);

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

                    foreach (var c in consultas)
                    {
                        EscribirLog($"Consulta detectada → Nombre: {c.Nombre}, FrecuenciaMinutos: {c.FrecuenciaMinutos}");
                    }
                }

                // ================================
                //   EJECUTA CONSULTAS
                // ================================
                EjecutarConsultasSegunFrecuencia();

                // ================================
                //   LIMPIEZA AUTOMÁTICA (NUEVO)
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

                // Log grande
                string rutaLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
                if (File.Exists(rutaLog) && new FileInfo(rutaLog).Length > 10 * 1024 * 1024)
                {
                    File.WriteAllText(rutaLog, "");
                    EscribirLog("🧹 Log limpiado (superaba 10MB).");
                }
            }
            catch (Exception ex)
            {
                EscribirLog("⚠️ Error en EjecutarLimpiezaAutomatica(): " + ex.Message);
            }
        }



        private void EjecutarTodasLasConsultas()
        {
            EscribirLog("Iniciando ejecución de consultas...");

            List<string> archivosGenerados = new List<string>();
            string carpetaSQL = configuracion.RutaSQL;
            Directory.CreateDirectory(configuracion.RutaExcel);

            // Limpiar resumen anterior
            resumenFilas.Clear();

            try
            {
                using (OracleConnection conexion = new OracleConnection(configuracion.ConnectionString))
                {
                    conexion.Open();
                    EscribirLog($"Conexión a Oracle exitosa: {conexion.DataSource}");

                    foreach (var consulta in consultas)
                    {
                        try
                        {
                            string sqlPath = Path.Combine(carpetaSQL, consulta.Archivo);

                            if (!File.Exists(sqlPath))
                            {
                                EscribirLog($"Archivo SQL no encontrado: {sqlPath}");
                                continue;
                            }

                            EscribirLog($"Ejecutando archivo SQL: {sqlPath}");
                            string sql = File.ReadAllText(sqlPath);

                            DataTable tabla = EjecutarConsulta(conexion, sql);
                            resumenFilas[consulta.Archivo] = tabla.Rows.Count;

                            EscribirLog($"Consulta '{consulta.Nombre}' ejecutada correctamente. Filas: {tabla.Rows.Count}");

                            if (tabla.Rows.Count == 0)
                            {
                                EscribirLog($"Consulta '{consulta.Nombre}' no devolvió resultados. No se generará Excel.");
                                continue;
                            }

                            string nombreArchivo = $"Reporte_{consulta.Nombre}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                            string rutaArchivo = Path.Combine(configuracion.RutaExcel, nombreArchivo);
                            GuardarExcel(tabla, rutaArchivo, consulta.Nombre);
                            EscribirLog($"Excel generado: {rutaArchivo}");

                            archivosGenerados.Add(rutaArchivo);
                        }
                        catch (Exception ex)
                        {
                            EscribirLog($"Error ejecutando consulta '{consulta.Nombre}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EscribirLog($"Error de conexión a Oracle: {ex.Message}");
                return;
            }

            if (archivosGenerados.Count > 0)
            {
                if (configuracion.EnviarCorreo)
                {
                    EnviarCorreo(archivosGenerados);
                }
                else
                {
                    EscribirLog("EnviarCorreo = false → NO se enviaron archivos.");
                }
            }
            else
            {
                EscribirLog("No se generaron archivos. No se enviará ningún correo.");
            }
        }

        private void EjecutarConsultasSegunFrecuencia()
        {
            EscribirLog("Verificando consultas por frecuencia...");

            using (OracleConnection conexion = new OracleConnection(configuracion.ConnectionString))
            {
                conexion.Open();

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

                        EjecutarConsultaIndividual(conexion, consulta);
                        consulta.UltimaEjecucion = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        EscribirLog($"Error ejecutando consulta {consulta.Nombre}: {ex.Message}");
                    }
                }
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

        private void GuardarExcel(DataTable tabla, string ruta, string hoja)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(tabla, hoja);
                ws.Columns().AdjustToContents();
                wb.SaveAs(ruta);
            }
        }

        private void EnviarCorreo(List<string> archivos)
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
                        mensaje.Subject = configuracion.AsuntoCorreo;
                        mensaje.Body = GenerarCuerpoCorreo();
                        mensaje.IsBodyHtml = false;

                        foreach (var dest in configuracion.Destinatarios)
                            mensaje.To.Add(dest);

                        foreach (var archivo in archivos)
                            mensaje.Attachments.Add(new Attachment(archivo));

                        cliente.Send(mensaje);

                        // 🔥 LOG COMPLETO
                        EscribirLog($"[ENVÍO GENERAL] Correo enviado OK. Destinatarios: {string.Join(", ", configuracion.Destinatarios)} | Archivos: {string.Join(", ", archivos)}");
                    }
                }

                // Mover archivos a carpeta Enviados
                string carpetaEnviados = Path.Combine(configuracion.RutaExcel, "..", "Enviados");
                carpetaEnviados = Path.GetFullPath(carpetaEnviados);
                Directory.CreateDirectory(carpetaEnviados);

                foreach (var archivo in archivos)
                {
                    try
                    {
                        string destino = Path.Combine(carpetaEnviados, Path.GetFileName(archivo));
                        if (File.Exists(destino))
                            File.Delete(destino);

                        File.Move(archivo, destino);
                        EscribirLog($"Archivo movido a Enviados: {destino}");
                    }
                    catch (Exception ex)
                    {
                        EscribirLog($"Error moviendo archivo {archivo}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                EscribirLog("[ENVÍO GENERAL] ERROR: " + ex.Message);
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
                        mensaje.Subject = $"Reporte automático: {consulta.Nombre}";
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

                        mensaje.Subject = asunto
                            .Replace("{Nombre}", consulta.Nombre)
                            .Replace("{Fecha}", DateTime.Now.ToString("dd/MM/yyyy HH:mm"));

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



        private string GenerarCuerpoCorreo()
        {
            string cuerpo = "Estimado/a,\n\n";
            cuerpo += "Se adjuntan los reportes generados:\n\n";

            foreach (var item in resumenFilas)
            {
                cuerpo += $"Cantidad de filas obtenidas en el archivo {item.Key}: {item.Value}\n";
            }

            cuerpo += "\nEste correo fue generado automáticamente.";
            return cuerpo;
        }


        private void EscribirLog(string mensaje)
        {
            try
            {
                string rutaLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
                File.AppendAllText(rutaLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {mensaje}{Environment.NewLine}");
            }
            catch { }
        }

        protected override void OnStop()
        {
            timer?.Stop();
            timer?.Dispose();
            EscribirLog("Servicio detenido.");
        }
    }
}
