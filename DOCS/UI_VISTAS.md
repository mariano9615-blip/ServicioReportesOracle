# 💎 Detalle de Vistas y Lógica de la UI

### Dashboard (v7.0.5)
- Cards navegables con el mismo mecanismo del sidebar (`MainViewModel.Nav*Command`): Última corrida → `MlogisHistorialView`, Alertas hoy → `AlertasView`, Estado del Servicio → `ServiceControlView`.
- Card `WebService` solo informativa (no clickeable, sin `Command`, sin cursor `Hand`, sin hover).
- Card de pendientes expandible inline (toggle por clic): muestra `Logs\comparaciones_pendientes.json` en modo solo lectura con columnas `ID`, `Nrocomprobante`, `Primera vez visto` (`HH:mm dd/MM`), `Corrida origen` (`FULL|DELTA`) y `Esperando hace` (`Xh Ym` recalculado en runtime).
- Interacción visual de cards navegables: cursor `Hand` y overlay hover `#22FFFFFF` cubriendo la card completa (no solo el contenido interior), sin bordes/efectos extra fuera del tema.
- Sección Alertas hoy: contadores en `OnSurfaceBrush` y alineados al inicio de cada barra de progreso.

### Métricas (v7.1.0)
- Vista dedicada `MetricasView` con métricas de las últimas 48h usando `Logs\mlogis_historial.json`, `Logs\mlogis_historial_ayer.json` y `Logs\alertas_oracle_enviadas.json`.
- **Gráficos de barras verticales** (WPF puro, sin dependencias externas): `ItemsControl + UniformGrid` con `BarItem { BarHeightPx, Tooltip, Fill }` para `IDs procesados por corrida` y `duración por corrida`.
- Las alturas de barra se pre-calculan en el ViewModel (sin converters en XAML); hover reduce opacidad; tooltip muestra el valor exacto por barra.
- **Duración correcta**: `MlogisCorrida` expone `duracion_segundos` (double). El core escribe `nuevaCorrida.DuracionSegundos = sw.Elapsed.TotalSeconds` antes de persistir el historial. El ViewModel lee este campo directamente en lugar del cálculo erróneo con timestamps de registros individuales que reportaba hasta 46,635 segundos (13h).
- KPIs: `corridas hoy vs ayer`, `alertas enviadas hoy`, y barra de distribución `FULL vs DELTA` de hoy.
- `MetricasViewModel` usa `FileSystemWatcher` con debounce 2s (`mlogis_historial*.json` + `alertas_oracle_enviadas.json`) y refresco en Dispatcher.

### AlertasView (v7.2.0)
- Reemplaza la vista anterior (solo alertas Oracle del día) con un log unificado de todos los mails SMTP enviados por el core, leído desde `Logs\alertas_smtp_enviadas.json`.
- `AlertaSMTP` (Models): campos `Timestamp`, `Tipo`, `IdReferencia`, `Destinatarios` (lista), `Asunto`, `Detalle`, `Origen`. Computed: `TimestampFormateado` (`dd/MM HH:mm`), `TipoAmigable` (mapa legible de tipos), `DestinatariosStr`.
- `AlertasViewModel`: `ObservableCollection<AlertaSMTP>` con contadores `TotalAlertas` y `AlertasHoy`. `FileSystemWatcher` en `Logs\alertas_smtp_enviadas.json` con debounce 2s. `IDisposable` + `Unloaded` en code-behind para cleanup.
- Vista: DataGrid con columnas Fecha, Tipo, ID Ref., Destinatarios, Asunto, Detalle, Origen. Botones Exportar a Excel (ClosedXML, header indigo) y Actualizar. Ordenado por timestamp descendente.

### 🗂️ Rotación de mlogis_historial.json
- Al escribir `mlogis_historial.json` después de cada corrida, se aplica rotación diaria:
  - **hoy** (`fecha_ejecucion.Date == DateTime.Today`): van a `mlogis_historial.json`.
  - **ayer** (`fecha_ejecucion.Date == DateTime.Today.AddDays(-1)`): van a `Logs\mlogis_historial_ayer.json`. Solo se escribe si hay corridas de ayer (no sobreescribe si no las hay).
  - **anteriores**: se descartan.
- Solo es limpieza de disco; no afecta el flujo de comparación Oracle (que opera sobre `comparaciones_pendientes.json`).
- Se loguea: `🗑️ [Historial] Rotación diaria: N corridas hoy, M de ayer preservadas, K descartadas.`

### 🗂️ Vista Historial SOAP — mlogis_historial_ayer.json (UI v4.6)
- **MlogisHistorialViewModel** carga ambos archivos en cada refresh:
  - `mlogis_historial.json` (corridas de hoy)
  - `mlogis_historial_ayer.json` (corridas de ayer, si existe)
- **Panel izquierdo (modo Por Corrida)**: las corridas de hoy aparecen primero, luego un separador `— Ayer —` y las corridas de ayer con `Opacity=0.5`.
  - El separador se implementa como un `CorridaItem` con `IsSeparator=true`; no es seleccionable (`IsEnabled=False`, `IsHitTestVisible=False`, `Focusable=False`).
  - Las corridas de ayer tienen `IsDeAyer=true`; el `ItemContainerStyle` aplica `Opacity=0.5` vía `DataTrigger`.
- **FileSystemWatcher**: filtro cambiado a `"mlogis_historial*.json"` — detecta cambios en ambos archivos, incluida la creación de `mlogis_historial_ayer.json` a medianoche.
- **Modo Por ID único**: itera `_historial.Concat(_historialAyer)` para deduplicación — los IDs de ayer se incluyen sin diferenciación visual.
- **LineInfo**: muestra el total combinado (`_historial.Count + _historialAyer.Count` corridas).

### 🗂️ Sidebar Colapsable (UI v4.5)
- **Comportamiento**: botón ☰ en la parte superior del sidebar togglea entre expandido (260px) y colapsado (56px).
- **Animación**: `GridLengthAnimation` custom (`GridLengthAnimation.cs`) con `CubicEase EaseInOut` 200ms sobre `SidebarColumn.Width`.
- **Estado colapsado**: se ocultan `SidebarTitle`, `SidebarSubtitle`, labels de nav (`NavText1`–`NavText7`) y `VersionText`. Quedan visibles solo el botón ☰ y los íconos emoji de cada item.
- **Campo**: `private bool _sidebarExpanded = true;` en `MainWindow.xaml.cs`.
- **Colores**: sin hardcodeo — overlays semitransparentes `#22FFFFFF`/`#33FFFFFF` como el `TitleBarButtonStyle` existente.
