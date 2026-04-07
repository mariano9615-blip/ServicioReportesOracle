# ANTIGRAVITY.md - Guía de Arquitectura del Proyecto (v7.8.1)

## 🚀 Resumen del Proyecto
**Nombre**: ServicioReportesOracle | **Versión**: v7.8.1 | **UI**: v5.4 | **Tech**: .NET Framework 4.8 (C#)
**Propósito**: Ecosistema para ejecución de reportes Oracle, envío de correos SMTP e integración SOAP con Mlogis.

## 📁 Arquitectura
- **Core (ServicioReportesOracle)**: Lógica de cronogramas, ejecución SQL (Oracle), generación Excel y SOAP Mlogis.
- **UI (ServicioReportesOracle.UI)**: Dashboard WPF (MVVM), gestión de tareas, logs y métricas.
- **TestSoap**: Consola para debug de conectividad SOAP.

## 🗂️ Archivos Críticos

| Archivo | Propósito |
|---------|-----------|
| `config.json` | Configuración global (Oracle, SMTP, SOAP, flags). |
| `consultas.json` | Definición de tareas/consultas SQL. |
| `Logs/json/mlogis_historico_mensual.json` | Métricas Mlogis diarias en ventana rolling de 30 días. Estructura: `{"generado": ..., "dias": [...]}`. |
| `DOCS/ARCHIVOS_JSON.md` | Detalle técnico de archivos JSON operativos y de configuración. |
| `DOCS/HEALTH_CHECK.md` | Detalle del Health Check SOAP y Circuit Breaker Oracle. |
| `DOCS/UI_VISTAS.md` | Detalle de las vistas y componentes de la UI WPF. |
| `DOCS/CHANGELOG.md` | Historial completo de cambios y versiones. |

## 🆕 Features destacadas

| Feature | Archivos | Notas |
|---------|----------|-------|
| Ventana Analítica (Power BI style) | `AnaliticaWindow.xaml` / `.xaml.cs` | Tema light independiente; gráficos WPF nativos sin NuGet. Datos mock; estructura lista para DataEngine. |
| Botón sidebar "Analítica" | `MainWindow.xaml` / `.xaml.cs` | Abre `AnaliticaWindow` como ventana secundaria (`Owner = this`). Soporta colapso sidebar. |

### 📐 Arquitectura de AnaliticaWindow
- **Tema**: Power BI light (`#F5F7FA` / `#FFFFFF`). Colores definidos como recursos en `Window.Resources`, no en App.xaml.
- **Gráficos**: todos dibujados en `Canvas` con `PathGeometry`, `Polyline`, `Polygon` (WPF puro). Se dibujan en `SizeChanged` (canvases stretch) o en `Loaded` (canvases con tamaño fijo).
- **Gauge**: `AddGaugeRing()` usa recursión para dividir arcos > 50% del gauge y evitar el bug WPF con arcos de exactamente 180°.
- **Donut**: `AddPieSlice()` con convención "0° = cima, sentido horario" (`SweepDirection.Clockwise`). Hueco interior = `Ellipse` blanca superpuesta.
- **DataGrid local**: override completo del estilo dark global para presentación light.

## ⚠️ ERRORES COMUNES Y TRAMPAS

### 1. Parseo de JSONs - SIEMPRE verificar estructura real
**Problema:** Asumir estructura sin verificar el archivo físico causa fallos silenciosos.
- `alertas_smtp_enviadas.json`: estructura `{"alertas": [...]}` (objeto con key), NO `[...]` (array plano).
- `alertas_oracle_enviadas.json`: array plano `[...]` desde v6.9.1.
- `comparaciones_pendientes.json`: `{"pendientes": [...]}`.
**Regla:** Antes de parsear: 1. Abrir archivo físico en `Logs\`. 2. Verificar estructura (objeto vs array).

### 2. Parseo de Fechas - DateTimeKind y cultura
**Problema:** `DateTime.TryParse` falla en Argentina (dd/MM vs MM/dd).
**Solución:** Parseo directo del JToken:
```csharp
DateTime? dt = token.Value<DateTime?>("campo_fecha");
if (dt.HasValue && dt.Value.Kind == DateTimeKind.Utc)
    dt = dt.Value.ToLocalTime();
```
**NUNCA** usar `.ToString()` + `TryParse` - usar `Value<DateTime?>()`.

### 3. FileSystemWatcher - Nombres actualizados
**Problema:** Watcher busca nombre viejo cuando archivo cambia de nombre.
**Regla:** Actualizar `_path` en constructor y filtro en `WatcherTrigger()` al cambiar rutas.

### 5. Dashboard "Alertas enviadas hoy" — tipos de alerta cubiertos
**Problema:** El panel mostraba "Sin alertas enviadas hoy" aunque `alertas_smtp_enviadas.json` contenía alertas del día.
**Causa:** `CargarAlertas()` en `DashboardViewModel` no contaba todos los tipos. Historial de fixes:
- v7.7.2: se agregaron `tarea_sql` → `AlertasTareas` / fila "Tareas SQL" (azul).
- v7.7.3: se agregaron `ws_caido` y `ws_recuperado` → `AlertasHealthCheck` / fila "Health Check" (rojo `#EF4444`).

**Tipos cubiertos actualmente en el dashboard:**
| Tipo en JSON | Contador | Fila en UI |
|---|---|---|
| `oracle_caso_a` | `AlertasCasoA` | Caso A (amarillo) |
| `oracle_caso_b` | `AlertasCasoB` | Caso B (rojo) |
| `tarea_sql` | `AlertasTareas` | Tareas SQL (azul) |
| `ws_caido`, `ws_recuperado` | `AlertasHealthCheck` | Health Check (rojo) |

**Regla:** Al agregar nuevos tipos de alerta al servicio (`RegistrarAlertaEnviada(tipo: "...")`), agregar el tipo a `CargarAlertas()`, una propiedad y barra en el ViewModel, y una fila en el XAML.

### 4. Delay de comparación Oracle
**Problema:** `CompararConOracle()` ignora `DelayComparacionMinutos` si parseo de fechas falla.
**Fix:** Usar `entry.Value<DateTime?>()` y referenciar `fechaEjecucion` (no `DateTime.Now`).

### 6. Health Check SOAP — Delay de gracia (v7.8.1)
**Contexto:** Microcortes (<2 min) disparaban alertas innecesarias que se autoresolvían.
**Solución:** `VerificarWebService()` aplica un período de gracia de 120s antes de confirmar cualquier caída:
- Primer fallo detectado → `_primerFalloHealthCheck = DateTime.Now`, log informativo, `return false` (sin tocar estado ni enviar mail).
- Fallos sucesivos dentro de 120s → log "esperando gracia...", `return false`.
- Fallo persistente ≥ 120s → log "Fallo confirmado", reset `_primerFalloHealthCheck = null`, continúa con lógica habitual de registro/alerta.
- Recuperación dentro de gracia → log "recuperado dentro de gracia (Xs)", reset, `return true` (sin registrar en `ws_estado.json` ni enviar mail).
**Aplica a:** `!wsDisponible` (timeout HTTP) y `!authOk` (error de autenticación SOAP).

## 📋 Reglas Esenciales
- **UI**: Usar colores de `App.xaml`. `MainViewModel.Instance.ShowNotification()` para avisos.
- **Threading**: Siempre `Application.Current.Dispatcher.InvokeAsync()` para actualizar la UI.
- **Logs**: Core loguea en `Logs/Log_<DiaSemana>.txt`. Rotación semanal automática.
- **Export Excel**: ClosedXML con header indigo `#4F46E5`. Usar `SaveFileDialog`.

## 🔗 Navegación entre vistas (v7.5.0+)

- Para navegar programáticamente a una vista con datos precargados: crear la instancia del ViewModel, configurarla, luego asignar `MainViewModel.Instance.SelectedViewModel = vm`.
- `MlogisHistorialViewModel` acepta `new MlogisHistorialViewModel(skipInitialLoad: true)` para evitar la carga inicial automática. Luego llamar `CargarConDatosPrecargados(fecha, registros)`.
- El `RefreshCommand` de `MlogisHistorialViewModel` resetea el modo precargado (`_modoFiltradoPorFecha = false`) y recarga desde JSON local.
- El `CargarAsync` verifica `_modoFiltradoPorFecha` y retorna inmediatamente si está en modo precargado (protege contra watchers y timers que disparen durante la sesión precargada).

## ⛔ Patrones Prohibidos
- NUNCA usar `dotnet build` — siempre MSBuild.exe directo.
- NUNCA hardcodear colores en vistas.
- NUNCA usar ComboBox sin Style explícito (ver `LogsView.xaml`).
- NUNCA escribir en archivos operativos de `Logs\` desde la UI (excepto alertas_leidas.json).

## 📎 Snippets Esenciales

**FileSystemWatcher con debounce 2s:**
```csharp
private void IniciarWatcher(string path) {
    _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)) { Filter = Path.GetFileName(path), NotifyFilter = NotifyFilters.LastWrite, EnableRaisingEvents = true };
    _watcher.Changed += (s, e) => { _debounceTimer.Stop(); _debounceTimer.Start(); };
    _debounceTimer = new System.Timers.Timer(2000) { AutoReset = false };
    _debounceTimer.Elapsed += (s, e) => Application.Current.Dispatcher.InvokeAsync(() => CargarDatos());
}
```

**Cleanup en Unloaded (IDisposable):**
```csharp
public void Dispose() { _watcher?.Dispose(); _debounceTimer?.Dispose(); }
// En code-behind: this.Unloaded += (s, e) => (DataContext as IDisposable)?.Dispose();
```

## 🛠️ Flujo de Compilación
- Compilar solución `.sln` en modo **Release**.
- Comando: `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" ServicioReportesOracle.sln -p:Configuration=Release -t:Rebuild -m`

---
**Para más detalles técnicos, consultar los archivos en la carpeta `DOCS/`.**
