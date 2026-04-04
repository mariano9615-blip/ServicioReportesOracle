# 🗂️ Changelog
## [7.4.1] - 2026-04-04

### Fixed
- **MetricasView**: Scroll vertical habilitado + alturas optimizadas para resoluciones 1080p
- **MetricasView**: Eliminados labels "seg" repetidos en gráfico de duración, ahora solo en título ("Duración en segundos (últimas 20)")
- **MetricasView**: Manejo visual de outliers en barras de IDs procesados (truncado a P95, color amarillo, indicador ▲)

### Improved
- **MetricasView**: Espaciado vertical mejorado entre secciones (margins 10-15px)
- **MetricasView**: Contexto temporal en gráfico de tendencia histórica ("← inicio período ... hoy →")
- **MetricasView**: Botón "Cargar 30 días" destacado con color primario para mayor visibilidad

## v7.4.0 - Core: Métricas Históricas Mlogis Rolling 30 Días (2026-04-04)

### ✨ Feature — Historial de métricas Mlogis con ventana rolling de 30 días
- **Nuevos modelos**: `MlogisHistoricoMensual` y `MetricaDiaria` en `MlogisHistorial.cs`.
- **Nuevo archivo**: `Logs/json/mlogis_historico_mensual.json` — acumula una `MetricaDiaria` por día con:
  - `total_corridas`, `corridas_full`, `corridas_delta`
  - `total_registros_pico` (máximo de registros en una sola corrida del día)
  - `cambios_soap_detectados` (suma de cambios de CTG/NroComprobante detectados)
  - `alertas_oracle_enviadas` (alertas Caso A + Caso B del día, leídas de `alertas_smtp_enviadas.json`)
  - `duracion_promedio_segundos`
- **Nuevo método**: `ActualizarHistoricoMensual()` — llamado automáticamente tras guardar `mlogis_historial_ayer.json` en la rotación diaria; mantiene la ventana de 30 días eliminando entradas anteriores.
- **Nuevo helper**: `ContarAlertasDelDia(DateTime)` — cuenta alertas oracle (tipo `oracle_caso_*`) del día solicitado desde `alertas_smtp_enviadas.json`.
- **ANTIGRAVITY.md**: actualizado con entrada para `mlogis_historico_mensual.json` y versión bumpeada a v7.4.0.

## v7.3.10 - UI: Dashboard Fix Estructura JSON Alertas (2026-04-03)

### 🐛 Bug Crítico — Dashboard: Error de parseo en alertas SMTP
- **Causa**: El método `CargarAlertas()` intentaba parsear `alertas_smtp_enviadas.json` como un array plano, pero el archivo tiene una estructura de objeto con una propiedad `"alertas"`. Esto causaba una excepción silenciosa y los contadores permanecían en 0.
- **Fix**: Se corrigió el parseo para usar `JObject.Parse` y extraer el `JArray` de la propiedad correspondiente.

## v7.3.9 - UI: Dashboard Fix Watcher y UTC (2026-04-03)

### 🐛 Bug — Dashboard: No refresca automáticamente al recibir alertas
- **Causa**: El `FileSystemWatcher` en `DashboardViewModel` no tenía en su lista de disparadores el archivo `alertas_smtp_enviadas.json` (el log unificado), por lo que las cards no se actualizaban en tiempo real tras un envío.
- **Fix**: Se agregó `alertas_smtp_enviadas.json` al método `WatcherTrigger`.
- **Mejora**: Se añadió manejo explícito de `DateTimeKind.Utc` en `CargarAlertas()` para asegurar que el filtrado por "hoy" sea robusto independientemente de la serialización del core.

## v7.3.8 - UI: Dashboard Fix Alertas Unified Log (2026-04-03)

### 🐛 Bug — Dashboard: "Sin alertas enviadas hoy" incorrecto
- **Causa**: El Dashboard leía del archivo obsoleto `alertas_oracle_enviadas.json` (usado para deduplicación), el cual no reflejaba el log unificado de envíos SMTP introducido en v7.2.0.
- **Fix**: Se cambió el data source a `alertas_smtp_enviadas.json`.
- **Adaptación**: Se actualizó el parseo para manejar el formato de array plano y los nuevos nombres de campos (`tipo`, `timestamp`, `id_referencia`).
- **Mapeo**: Los contadores ahora filtran correctamente por `oracle_caso_a` y `oracle_caso_b`.

## v7.3.7 - UI: Dashboard Fix Parseo de Fechas (2026-04-03)

### 🐛 Bug — Dashboard: Cálculo de tiempo "Más antiguo" erróneo
- **Causa**: `DashboardViewModel` seguía usando `DateTime.TryParse` sobre el string de `primera_vez_visto`, lo que fallaba en entornos con cultura regional argentina (dd/MM/yyyy). Al fallar, devolvía `DateTime.MinValue`, resultando en etiquetas de "hace 43200 min" o "+30 días".
- **Fix**: Se alineó la lógica con el Core (v7.3.6), utilizando `Value<DateTime?>()` para un parseo robusto directo desde el JToken.
- **Robustez**: Se añadió el manejo de `DateTimeKind.Utc` para asegurar que el cálculo de `EsperandoHace` sea siempre sobre tiempos locales correctos.

## v7.3.6 - Core: Fix de Delay y Parseo Robusto de Fechas (2026-04-03)

### 🐛 Bug Crítico — Delay de comparación Oracle no se respetaba
- **Causa**: Fallo de parseo en `CompararConOracle` al usar `.ToString()` sobre un token `JValue` ya parseado. En sistemas con cultura regional (ej: Argentina), la fecha se convertía a `dd/MM/yyyy`, lo que hacía fallar a `InvariantCulture.TryParse`, resultando en `DateTime.MinValue` y disparando alertas inmediatas.
- **Fix**: Reemplazado parseo manual por `entry.Value<DateTime?>()`, que utiliza la lógica interna de Newtonsoft para detectar y convertir tipos de forma segura.
- **Mejora**: Se sincronizó el cálculo de `minutosPasados` usando `fechaEjecucion` (timestamp de inicio de la corrida) como referencia única, eliminando discrepancias por el uso de `DateTime.Now` en medio del proceso.
- **Logging**: Mejora en la visibilidad del buffer; ahora el log detalla cuántos IDs esperan delay e incluye un ejemplo con los minutos restantes (ej: `IDs waiting: 3 (ej: SIL-8407366, esperando 12m más)`).

## v7.3.5 - Core: Fix de Serialización de Fechas (2026-04-02)

### 🐛 Bug — Root cause de cálculo absurdo en Dashboard
- **Fix**: Se modificó `ActualizarComparacionesPendientes` en el Servicio Core para usar el formato **ISO 8601 Sortable ("s")** al guardar `primera_vez_visto`.
- **Análisis**: Anteriormente se guardaba usando el formato regional del servidor, lo que causaba que la UI interpretara `04/02` como 4 de Febrero (en lugar de 2 de Abril), provocando el desfase de 1368 horas.
- **Robustez**: Se añadió `InvariantCulture` en el parsing del Core para total consistencia.

## v7.3.4 - UI: Fixes Visuales en Dashboard y Métricas (2026-04-02)

### 🐛 Bug — MetricasView: "Alertas enviadas hoy" incorrecto
- **Fix**: Se añadió `CultureInfo.InvariantCulture` al parsear el `timestamp` del JSON.
- **Robustez**: Ahora el contador soporta tanto el formato de array plano (v6.9.1) como el formato de objeto legacy (`{ "alertas": [] }`), y verifica los campos `timestamp` y `ultima_vez_alertado`.

### 🐛 Bug — Dashboard: Cálculo de minutos "Más antiguo" absurdo
- **Causa**: Fallo de parseo regional (MM/dd vs dd/MM) y desajuste entre UTC/Local que provocaba desfases de hasta 57 días (82090 min) o `DateTime.MinValue`.
- **Fix**: 
  - Se implementó `ToLocalTime()` y `AssumeLocal` con `InvariantCulture` para sincronizar el tiempo del JSON con el del sistema.
  - Se añadió una validación en el timer para capar valores imposibles y asegurar que el cálculo de `TotalMinutes` sea siempre sobre tiempos locales coherentes.

## v7.3.3 - UI: Alertas Redesign & Export CSV (2026-03-31)

### ✨ Mejora — Rediseño de AlertasView
- **Visualización**: Zebra striping (filas alternas), altura de fila de 32px y mejores anchos de columna.
- **Interactividad**: Tooltips en celdas para Asunto, Detalle y Destinatarios para lectura sin truncar.
- **Emoji Fix**: Eliminación de símbolos duplicados en tipos de alerta (e.g. `🔴 WebService Caído`).

### 🛠️ Robustez — Exportación CSV Simple
- **Simplificación**: Reemplazada la exportación a Excel (ClosedXML/EPPlus) por formato **CSV (UTF-8)** con delimitadores de comillas para máxima compatibilidad.
- **Portabilidad**: Se eliminan dependencias gráficas pesadas, asegurando el funcionamiento en cualquier entorno de servidor.

## v7.3.2 - UI: Fix Badge de Alertas y Sistema de Leído Persistido (2026-03-31)

### 🐛 Bug — Badge del sidebar estancado en "1"
- **Causa**: `MainViewModel` estaba leyendo el archivo antiguo `alertas_oracle_enviadas.json` y usando una lógica de filtrado de "leídos" manual e inconsistente.
- **Fix**: 
  - Actualizado a `alertas_smtp_enviadas.json` como fuente única.
  - Implementado sistema de IDs persistente en `alertas_leidas.json` usando key compacta `{timestamp}_{tipo}`.
  - Mejorado `FileSystemWatcher` con `NotifyFilters.Size` y debounce de 2s para actualizaciones en tiempo real.

### ✨ Mejora — Sistema de Leído/No Leído
- Al navegar a **Alertas**, todos los registros de hoy se marcan automáticamente como leídos.
- **Purga automática**: El archivo `alertas_leidas.json` ahora mantiene solo los últimos 7 días de historial para optimizar el rendimiento.
- **Notificación Directa**: `AlertasViewModel` notifica inmediatamente al `MainViewModel` para resetear el badge a 0 al visualizar las alertas.
- **Fix de Threading (Dispatcher)**: Se movió la ejecución de `MarcarComoLeidas()` dentro del bloque `InvokeAsync` para asegurar que la colección de alertas esté poblada antes de intentar persistir su estado de lectura.

## v7.3.1 - Fix crítico Health Check WS — Anti-spam y UltimaVezCaido (2026-03-31)

### 🐛 Bug 1 — 26 mails de caída en lugar de 1 (oscilación caido↔auth_error)
- **Causa raíz**: La condición de reset de `AlertaCaidaEnviada` usaba `!= "caido"` (resp. `!= "auth_error"`) en cada rama. Si el WS oscilaba entre ambos estados de falla (e.g., primer check sin respuesta HTTP → `caido`, siguiente check responde HTTP pero falla SOAP → `auth_error`), cada transición reseteaba el flag a `false` y disparaba un nuevo mail, generando N mails en lugar de 1 por ciclo de caída.
- **Fix**: El flag `AlertaCaidaEnviada` solo se resetea a `false` al transicionar DESDE `"ok"` (estado sano) a cualquier estado de falla. Las transiciones entre estados de falla (`caido` ↔ `auth_error`) ya NO resetean el flag. Idem para `UltimaVezCaido`.

### 🐛 Bug 2 — `ultima_vez_caido` se pisaba en cada caída consecutiva
- **Causa**: `estado.UltimaVezCaido = ahora` se ejecutaba incondicionalmente en cada check de falla (comentado como `v7.1.1 — actualizar siempre`), sobreescribiendo el timestamp de la primera caída del ciclo.
- **Fix**: `UltimaVezCaido` solo se actualiza al transicionar desde `"ok"` (primera caída del ciclo). La primera caída preserva el timestamp hasta la próxima recuperación, permitiendo que el mail de recuperación muestre correctamente `{UltimaVezCaido}`.

### 🔍 Logging
- Agregados logs explícitos con prefijo `[HealthCheckSoap]` para ambas ramas (caida/auth_error):
  - `[HealthCheckSoap] ⚠️ WS caído — Enviando alerta (primera caída del ciclo)`
  - `[HealthCheckSoap] ⚠️ WS caído — Alerta ya enviada, omitiendo reenvío`
  - `[HealthCheckSoap] ✅ WS recuperado — Enviando notificación de recuperación`

## v7.2.0 - UI: Refactor AlertasView para log unificado SMTP (2026-03-30)

### 🎨 UI — AlertasView refactorizada
- **Antes**: mostraba solo alertas Oracle del día (`alertas_oracle_enviadas.json`, campos `id`/`tipo_caso`/`timestamp`/`nrocomprobante`).
- **Ahora**: muestra log unificado de todos los mails SMTP enviados por el core, leído desde `Logs\alertas_smtp_enviadas.json`.
- **`AlertaSMTP` (nuevo modelo)**: campos `Timestamp`, `Tipo`, `IdReferencia`, `Destinatarios` (lista), `Asunto`, `Detalle`, `Origen`. Computed `TipoAmigable` (mapa de tipos internos a etiquetas legibles) y `DestinatariosStr`.
- **`AlertasViewModel` reescrito**: `ObservableCollection<AlertaSMTP>`, contadores `TotalAlertas` y `AlertasHoy`, `FileSystemWatcher` + debounce 2s sobre `alertas_smtp_enviadas.json`, `IDisposable` + cleanup en `Unloaded`.
- **Vista reemplazada**: DataGrid con columnas Fecha, Tipo, ID Ref., Destinatarios, Asunto, Detalle, Origen. Ordenado por timestamp descendente. Exportar a Excel (ClosedXML, header indigo `#4F46E5`).

---

## v7.1.2 - Fix log SMTP recuperación WS (2026-03-30)

### 🐛 Bug — Retorno de EnviarMailWS(esRecuperacion: true) ignorado
- **Causa**: En el bloque de recuperación, la llamada `EnviarMailWS(esRecuperacion: true, ...)` no capturaba el `bool` retornado, logueando siempre "Mail enviado" aunque el SMTP hubiera fallado.
- **Fix**: Resultado capturado en `bool mailEnviado`; el log usa expresión ternaria: "Mail enviado" si `true`, "FALLO al enviar mail SMTP" si `false`.

## v7.1.1 - Fix WS Health Check (2026-03-30)

### 🐛 Bug 1 — UltimaVezCaido no se actualizaba tras la primera caída
- **Causa**: `if (estado.UltimaVezCaido == null)` impedía actualizar el timestamp en caídas sucesivas, reportando siempre la primera caída como "última".
- **Fix**: Eliminada la condición — `estado.UltimaVezCaido = ahora;` se asigna siempre, en ambas ramas (`caido` y `auth_error`).

### 🐛 Bug 2 — AlertaCaidaEnviada no se reseteaba al cambiar de día
- **Causa**: El bloque de reset diario de contadores (`CaidasHoy`, `RecuperacionesHoy`) no incluía `AlertaCaidaEnviada`, por lo que al comenzar un nuevo día con el WS caído no se enviaba alerta nueva.
- **Fix**: Agregado `estado.AlertaCaidaEnviada = false;` en el bloque de reset diario (`v7.1.1`).

### 🐛 Bug 3 — AlertaCaidaEnviada se seteaba true aunque SMTP fallara silenciosamente
- **Causa**: `EnviarMailWS()` tenía tipo de retorno `void` y capturaba la excepción SMTP internamente, pero el llamador seteaba `AlertaCaidaEnviada = true` incondicionalmente.
- **Fix**: `EnviarMailWS()` ahora retorna `bool` (`true` = enviado OK o sin destinatarios configurados, `false` = excepción SMTP). Los dos call sites con `esRecuperacion: false` usan el resultado para setear el flag solo si el mail fue enviado; en caso contrario loguean "FALLO al enviar alerta SMTP. Se reintentará en la próxima corrida."

## v4.8 - Versión Estable (2026-03-28)

### 🎯 Estado del Proyecto
- ✅ **Versión 4.8 declarada como estable** - Todas las funcionalidades principales probadas y funcionando correctamente
- 🚀 **Próxima versión v5.0**: Inicio de migración a SQLite como base de datos principal

## v7.1.0 - Fix Duración Corridas + Gráficos de Barras (2026-03-28)

### 🐛 Fix crítico — Duración reportada incorrectamente
- **Causa raíz diagnosticada**: `MetricasViewModel.CalcularDuracionSegundos()` calculaba `maxUltimaVezVisto - minPrimeraVezVisto` de los **registros individuales** de la corrida, no la duración de la corrida en sí. `PrimeraVezVisto`/`UltimaVezVisto` son timestamps de cuándo se vio el ID por primera/última vez históricamente → un ID presente desde el inicio del día producía una "duración" de ~13h (46,635 segundos). El **Stopwatch** en `InvocacionSoapMlogis()` era correcto pero su valor nunca se persistía en el JSON.
- **Fix en core** (`MlogisHistorial.cs`): nuevo campo `duracion_segundos` (double, `JsonProperty`) en `MlogisCorrida`.
- **Fix en core** (`ServicioReportesOracle.cs`): `nuevaCorrida.DuracionSegundos = sw.Elapsed.TotalSeconds` antes de añadir la corrida al historial — el Stopwatch arranca al inicio de `InvocacionSoapMlogis()` y mide toda la corrida SOAP incluida la comparación Oracle.
- **Fix en UI** (`MetricasViewModel.cs`): `CalcularDuracionSegundos()` eliminado; la serie de duración lee `c.DuracionSegundos` directamente. Corridas históricas (sin el campo) muestran 0s como fallback por default del campo.

### 🎨 Refactorización — Gráficos de barras verticales
- **`MetricasViewModel.cs`**: reemplaza `PointCollection`/sparklines por `ObservableCollection<BarItem>` (`IdsBarItems`, `DuracionBarItems`). `BarItem` expone `BarHeightPx` (altura en px pre-calculada, máximo 82px normalizado al valor máximo de la serie, mínimo 2px visible), `Tooltip` (valor exacto) y `Fill` (brush del tema). Eliminados `BuildSparklinePoints`, `BuildSparklineFill`, `PointCollection`. Nuevo método `BuildBarItems()`.
- **`MetricasView.xaml`**: reemplaza `Canvas + Polyline + Polygon` por `ItemsControl` con `UniformGrid(Rows=1)` distribuyendo las barras equidistantes, `Border` con `CornerRadius="2,2,0,0"` y `VerticalAlignment="Bottom"`. Hover reduce `Opacity` a 0.75. Sin converters ni dependencias externas (WPF puro). Labels renombrados: `IdsBarLabel` / `DuracionBarLabel`.

## v7.1.0 - Features de Productividad + Fix Duración SOAP (2026-03-28)

### UI - Mejoras DataGrid
- ⌨️ Habilitado Ctrl+C nativo en todos los DataGrids (formato TSV con headers)
- 📊 Export Excel en MlogisHistorialView (respeta modo Por Corrida / Por ID)
- 📊 Export Excel en AlertasView
- **v7.0.9 (UI)**: Polish visual de `MetricasView`. Sparklines ampliados a 80px con área rellena (`Polygon`) bajo la curva, estado vacío `"Sin datos aun"` cuando hay 0/1 puntos y ocultación de labels `Min/Max` en ese estado. Cards de gráficos ajustadas con mejor aprovechamiento vertical (`MinHeight`), contador `Alertas enviadas hoy` con color semántico (`SuccessBrush`=0, `WarningBrush`=>0) y bloque `FULL vs DELTA` reordenado con labels alineados y barra pill de 12px.
- **v7.0.8 (UI)**: Nueva vista `MetricasView` + `MetricasViewModel` en `ServicioReportesOracle.UI` con métricas de 48h basadas en `mlogis_historial.json`, `mlogis_historial_ayer.json` y `alertas_oracle_enviadas.json`. Incluye dos sparklines en WPF puro (`Canvas + Polyline`) para IDs por corrida y duración por corrida, contador `corridas hoy / ayer`, contador de alertas de hoy y barra visual FULL vs DELTA de corridas de hoy. Integración en navegación lateral (`MainWindow.xaml` + `MainViewModel`), `FileSystemWatcher` con debounce 2s, y cleanup por `Dispose` al salir de la vista.
- **v7.0.7 (core)**: Alerta proactiva por crecimiento de `comparaciones_pendientes.json`. Nuevo método `VerificarUmbralPendientes()` al final de `ActualizarComparacionesPendientes()` con umbral configurable (`AlertaPendientes.UmbralCantidad`, default 50), cooldown anti-spam (`AlertaPendientes.CooldownHoras`, default 4), envío de mail de alerta y mail de resolución al normalizar. Estado persistido en `Logs\\pendientes_alerta_estado.json` (`ultimo_envio`, `cantidad_al_enviar`). `MigrarConfigSiFaltan()` inyecta la nueva sección `AlertaPendientes` con plantillas y placeholders (`{CantidadActual}`, `{IdMasAntiguo}`, `{HorasEnBuffer}`, `{Timestamp}`, `{Empresa}`).
- **v7.0.6 (core)**: Nuevo Circuit Breaker para Oracle. `ServicioReportesOracle.cs` ahora protege todas las aperturas de `OracleConnection` (corridas por frecuencia y comparación Oracle) con estados `closed/open/half_open`. Al superar `CircuitBreakerUmbral` (default 3) abre circuito y omite corridas Oracle; en `open` envía una sola alerta SMTP por ciclo. Tras `CircuitBreakerTimeoutMinutos` (default 15) ejecuta prueba `SELECT 1 FROM DUAL`: si falla vuelve a `open`, si recupera vuelve a `closed` y envía mail de recuperación. Estado persistido en `Logs\\oracle_circuit_state.json`. `MigrarConfigSiFaltan()` ahora inyecta también campos faltantes anidados (`CircuitBreakerAlerta.*`) en `config.json`.
- **v7.0.5 (UI fix)**: DashboardView.xaml corrige interacción de cards en dashboard. WebService queda solo informativa (sin Command, sin cursor Hand, sin hover) y Estado del Servicio vuelve a ser navegable a ServiceControlView con DashboardCardButtonStyle (hover completo #22FFFFFF sobre toda la card y cursor Hand).
- **v7.0.4 (UI fix)**: Ajustes visuales en `DashboardView.xaml`. El hover `MouseOver` de cards ahora cubre la card completa (overlay `#22FFFFFF` sobre todo el `Border`), la card `WebService` deja de ser clickeable (sin `Command`, sin cursor `Hand`, sin hover), los contadores `Caso B / Caso A / Anulados` usan `OnSurfaceBrush` (blanco) y los valores quedan alineados al inicio de cada barra de progreso.
- **v7.0.3 (UI)**: Dashboard con cards navegables y panel inline de pendientes. `DashboardView.xaml` ahora permite click en cards con cursor `Hand` y overlay hover `#22FFFFFF` (estilo tema oscuro): WebService/Estado Servicio navegan a `ServiceControlView`, Última corrida a `MlogisHistorialView`, Alertas hoy a `AlertasView`. La card de pendientes abre/cierra un panel expandible de solo lectura con datos de `Logs\comparaciones_pendientes.json` (columnas `ID`, `Nrocomprobante`, `Primera vez visto` `HH:mm dd/MM`, `Corrida origen`, `Esperando hace` calculado en runtime como `Xh Ym`) y estado vacío `"Sin comparaciones pendientes"`.
- **v7.0.2**: Fix compatibilidad `alertas_oracle_enviadas.json` formato viejo. `ServicioReportesOracle.cs`: nuevo método `LeerDedupCompatible()` que migra el formato heredado `{"alertas":[{id, campo, ultima_vez_alertado}]}` al array plano actual — evita re-envío de alertas ya enviadas al actualizar desde versiones pre-v6.9.1. `AlertasViewModel.cs`: fallback en `CargarAlertas()` para leer el formato viejo cuando `JArray.Parse` falla, permitiendo mostrar alertas en instalaciones con el archivo heredado.
- **v7.0.1 (UI fix)**: Fix race condition en `AlertasViewModel.CargarAsync()`: eliminado el `OnPropertyChanged(HasAlertas)` suelto que se encolaba en el Dispatcher *después* del `InvokeAsync` de `CargarAlertas()`. El setter ya notifica; el notify extra podía disparar una segunda evaluación con la colección aún vacía, dejando `HasAlertas=false` aunque `Alertas` tuviera items.
- **v7.0**: DashboardView agregado como pantalla principal por defecto al iniciar la UI. Reúne en un vistazo el estado del WebService de Mlogis (health check de `ws_estado.json`), información de la última corrida, cantidad de procesos `pendientes`, alertas enviadas hoy (Casos A, B y Anulados con barras proporcionales), y una lista compacta de todas las corridas de hoy desde `mlogis_historial.json`. Todo reactivo basado en `FileSystemWatcher` con debounce de 2 segundos.
- **v6.9.2 (UI v4.6)**: Historial SOAP muestra corridas de hoy + ayer. `MlogisHistorialViewModel` carga `mlogis_historial.json` y `mlogis_historial_ayer.json` (si existe). Panel izquierdo: separador `— Ayer —` entre hoy y ayer; corridas de ayer con `Opacity=0.5`. `FileSystemWatcher` con filtro `mlogis_historial*.json` detecta ambos archivos. Modo Por ID único: deduplicación combinada de ambos historiales. `LineInfo` refleja total combinado. Ciclo `comparaciones_pendientes.json` verificado sin bugs: formato consistente y operaciones secuenciales.
- **UI v4.5**: Sidebar colapsable con botón ☰. Animación 260↔56px con GridLengthAnimation + CubicEase 200ms. Estado colapsado muestra solo íconos emoji de nav centrados.
- **v6.9.1**: Fix anulados: `SUBSTR(id,3,15) IN ({IDS_TRUNCADOS})` en `consultas_soap.json` y match C# via `SUBSTR(idOracle,2,15) == idMlogis[:15]` (equivalente al trigger `TRG_MPE_RENOMBRAMLOGIS`). Fix `alertas_oracle_enviadas.json`: array acumulativo plano con purga diaria (formato `[{id, tipo_caso, timestamp, nrocomprobante}]`). Dedup por `id+tipo_caso+DateTime.Today` — ya no se pierde el historial del día al reescribir el archivo.
- **v6.9**: Rotación diaria de `mlogis_historial.json`: corridas de hoy en `mlogis_historial.json`, corridas de ayer en `mlogis_historial_ayer.json` (solo si existen, sin sobreescribir si no hay), anteriores descartadas. Log: `🗑️ [Historial] Rotación diaria: N corridas hoy, M de ayer preservadas, K descartadas.`
- **v6.8 (UI v4.4)**: Buscador en LogsView. `Ctrl+F` abre/cierra barra de búsqueda; `Esc` la cierra. Filtrado case-insensitive en memoria sobre `_allLines` (copia maestra de hasta 1.000 líneas), sin releer el archivo. Líneas nuevas del watcher también se filtran. `LineInfo` muestra conteo de coincidencias. Fast-path sin filtro preserva auto-scroll; con filtro activo reconstruye `Lines` via `AplicarFiltroInterno()`. `ToggleSearchCommand` y `ClearSearchCommand` en ViewModel; foco automático al abrir via `Dispatcher.BeginInvoke(DispatcherPriority.Input)`.
- **v6.7 (UI v4.3)**: Corrida FULL inteligente — compara `fecupd` Mlogis vs `primera_vez_visto` antes de actualizar historial; solo IDs nuevos o actualizados van a `comparaciones_pendientes.json`. `MlogisRegistro` con nuevos campos `FecUpd`, `Anulado` (bool), `IdAnuladoOracle` (string). `CompararConOracle()`: match AN% marca `anulado=true` en historial sin generar Caso B ni alerta. `consultas_soap.json`: query anulados corregida a `SUBSTR(id,3) IN ({IDS})`. Log compacto una línea por corrida: `[HH:mm] Run {FULL|DELTA}: {total} IDs | N:{nuevos} U:{actualizados} A:{anulados} S:{sinCambios} | {segundos}s`. UI: `LogsViewModel` fix memory leak (`SemaphoreSlim` anti-concurrencia, `IDisposable`+`Unloaded` para cleanup de watcher/timer, try/catch global, `ScrollIntoView` protegido).
- **v6.6.1**: Fix precisión anulados. `query_oracle`: `OR (id LIKE 'AN%' AND (id LIKE '%SIL-%' OR id LIKE '%-%'))`. C#: `CompararConOracle` recolecta todos los rows Oracle primero; Caso B usa `Any(ora => ora.id == idMlogis || (ora.id.StartsWith("AN") && ora.id.Contains(idMlogis)))` — cubre GUIDs y SIL con prefijo AN. Anulados nunca generan Caso A ni Caso B. (Reemplazado por v6.7.)
- **v6.6**: Soporte inicial anulados Oracle (SUBSTR approach). Reemplazado por v6.6.1.
- **v6.4 (UI v4.1)**: Auditoría diaria en `ws_estado.json` (`caidas_hoy`, `recuperaciones_hoy`, `ultimo_error_xml`, `historial_eventos` — últimos 100 eventos, reset automático al inicio del día). Anti-spam recuperación: mail "Recuperado" solo si `alerta_caida_enviada=true`. Lock `_wsEstadoLock` en `GuardarWsEstado`. Mail: `{EndpointHost}` → "Mlogis SmartFarm" (sin Safelinks). Logs UI: `RefreshCommand` incremental sin spinner; IsBusy solo al cambiar día.
- **v5.9 (UI v4.1)**: Health Check SOAP refactorizado: valida conectividad HTTP (UrlAutentificacion + UrlWS) y luego POST SOAP real de autenticación. Estado `"auth_error"` para LoginSucceeded=false. `ws_estado.json` incluye `detalle_error`. Mail de caída con placeholder `{DetalleError}`.
- **v5.5 (UI v4.0)**: HealthCheckSoap configurable en `config.json` (destinatarios y plantillas para caída/recuperación, editable desde la UI). Carga asíncrona de `config.json` en GeneralConfigView con indicador IsBusy. Estado del WS SOAP visible en la UI (lee `Logs\ws_estado.json` en tiempo real). Clave maestra de recuperación de acceso en LoginWindow. Versión UI estandarizada a v4.0.
- **v5.4**: Archivos operativos centralizados en `Logs\` (migración automática al iniciar). Health check del WS SOAP con `ws_estado.json` y mails de caída/recuperación. Rotación de `mlogis_historial.json` (>7 días). UI LogsView: carga asíncrona con `Task.Run`, últimas 1.000 líneas, ListBox virtualizado, indicador de líneas totales vs mostradas.
- **v5.0**: Filtro SOAP cambiado de FECHA a FECUPD (delta por última actualización). Segunda llamada a MLOGISLEGAL para obtener CTG (mlogislegal.valor) por IDs. Reconciliación horaria automática (UltimaReconciliacion en config.json): corrida full cada 1h (fecupd desde 00:00), delta entre medias. Nueva estructura mlogis_historial.json con historial de corridas (tipo, registros, primera_vez_visto, ultima_vez_visto, cambios_detectados). Detección de cambios en ctg y nrocomprobante con auditoría acumulativa. Nuevo método EnviarAlertaCambioSoap() separado del sistema de mails existente, configurado via consultas_soap.json. UI: UltimaReconciliacion en ConfigModel + display property en GeneralConfigViewModel.
- **v4.0**: Limpieza modelo global (eliminados DiaEjecucion, HoraEjecucion, Destinatarios, AsuntoCorreo, CuerpoCorreo, EnviarCorreo), saneado automático de config.json, UltimaEjecucionSoap persistida en disco tras cada SOAP exitoso, sistema de logs particionado por día (`Logs/Log_<DiaSemana>.txt`) con rotación semanal, UI de Logs con selector de día, robustez en escrituras a disco.
- **v3.9**: Fix timer SOAP (respeta FrecuenciaSoapMinutos), test email con IsBusy/timeout 10s, login error via binding, ClaveUI encriptada en config, vista Cambiar Contraseña.
- **v3.8**: Fixes login, botones, hot-reload y campo Empresa.
- **v3.7**: Login lockout, encriptación SMTP, validación config, ícono, título custom.
- **v3.6**: ServiceControlView con UAC manifest y admin check.
- **v3.5**: Vista ServiceControlView para control del servicio Windows.

