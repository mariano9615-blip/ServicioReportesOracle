# ANTIGRAVITY.md - Guía de Arquitectura del Proyecto (v6.6)

Este archivo es la fuente de verdad para Antigravity. Mantenlo actualizado para un trabajo óptimo.

## 🚀 Resumen del Proyecto (v6.6)
**Nombre**: ServicioReportesOracle
**Versión Actual**: v6.6 (UI v4.2)
**Tecnología**: .NET Framework 4.8 (C#)
**Propósito**: Ecosistema para ejecución de reportes Oracle, envío de correos SMTP e integración SOAP con Mlogis.

## 📁 Estructura de la Solución
### 1. ⚙️ ServicioReportesOracle (Core)
- **Lógica**: Manejo de cronogramas, ejecución SQL (Oracle), generación Excel (ClosedXML) y envío de mails.
- **Mlogis**: Integración SOAP para comparación de registros. El timer respeta `FrecuenciaSoapMinutos` de `config.json`.
  - **`EjecutarComparacionMlogis()` (legacy)**: Se invoca desde `EjecutarConsultasSegunFrecuencia()` cuando la consulta se llama `"ComparacionMlogisOracle"`. Usa `ids_history.json` (IDs históricos vistos por SOAP) y ejecuta un SQL desde archivo (`.sql`) contra Oracle. Compara *presencia/ausencia* de IDs. Envía mail via `EnviarCorreoTracking()`. **Se mantiene por compatibilidad.**
  - **`CompararConOracle()` (activo, v5.1)**: Se invoca al final de cada `InvocacionSoapMlogis()`. Usa los registros de la corrida actual en `mlogis_historial.json` y ejecuta `query_oracle` de `consultas_soap.json` contra Oracle. Compara *nrocomprobante* (Caso A) y *presencia* (Caso B). Envía mail via `EnviarAlertaCambioSoap()`. **Este es el mecanismo principal activo.**
  - Ambos métodos coexisten: el legacy cubre IDs históricos acumulados en `ids_history.json`; el nuevo cubre la corrida actual con validación de datos, no solo presencia.
- **Configs**: `config.json` (Global) y `Consultas.json` (Tareas).
- **Auto-heal**: Al iniciar, `MigrarConfigSiFaltan()` inyecta atributos faltantes en `config.json` sin pisar valores existentes.

### 2. 💎 ServicioReportesOracle.UI (WPF)
- **Arquitectura**: MVVM pura.
- **Vistas**: `GeneralConfigView`, `TasksView` (Gestión ABM), `SqlEditorView` (Testing), `LogsView`, `ServiceControlView`, `ChangePasswordView`.
- **Diseño**: Tema oscuro premium con notificaciones tipo "Toast" incorporadas.
- **Modelos**: Estructura anidada para configuración de mails (`Mail.ConError.Asunto`, etc.).

### 3. 🧪 TestSoap (Console)
- Herramienta rápida para debuggear la conectividad con el WS de Mlogis sin levantar todo el servicio.

## 🗂️ Archivos del Servicio Core

### Configuración (raíz del directorio de ejecución — editados por el usuario)

| Archivo | Propósito |
|---------|-----------|
| `config.json` | Configuración global (conexión Oracle, SMTP, SOAP, flags). |
| `filters.json` | Filtros SOAP para `InvocacionSoapMlogis()` (ESTADOLOG/STATUS). |
| `consultas.json` | Definición de tareas/consultas SQL. |
| `consultas_soap.json` | Configuración de alertas SOAP y query Oracle de comparación. |

### Operativos (carpeta `Logs\` — escritos por el servicio, no editar manualmente)

| Archivo | Propósito |
|---------|-----------|
| `mlogis_historial.json` | Historial estructurado de corridas SOAP (rotación 7 días). |
| `comparaciones_pendientes.json` | Buffer de IDs SOAP pendientes de comparar contra Oracle. |
| `alertas_oracle_enviadas.json` | Historial de deduplicación de alertas Oracle. |
| `ids_history.json` | IDs vistos por SOAP (compatibilidad legacy con `EjecutarComparacionMlogis`). |
| `status.json` | Estado de errores/resueltos del flujo legacy `ComparacionMlogisOracle`. |
| `ws_estado.json` | Estado del health check del WebService SOAP (último estado, timestamps, flag de alerta enviada). |
| `Log_<DiaSemana>.txt` | Logs de ejecución del servicio (rotación semanal). |

> **Migración automática**: al iniciar, el servicio mueve los 5 archivos operativos de la raíz a `Logs\` si existen allí (instalaciones existentes).

---

## 🗂️ Archivos de Configuración SOAP (detalle)
### filters.json
- **Ubicación**: raíz del directorio de ejecución del servicio core.
- **Propósito**: Define los filtros que se aplican a las llamadas SOAP a Mlogis. Sin este archivo, `InvocacionSoapMlogis()` aborta inmediatamente.
- **Leído por**: `InvocacionSoapMlogis()` en `ServicioReportesOracle.cs`.
- **Formato actual (v5.5) — Condiciones compuestas**:
```json
[
  {
    "Entidad": "Mlogis",
    "Overlay": 1,
    "Condiciones": [
      { "EstadoLog": "6", "Status": "2" },
      { "EstadoLog": "4", "Status": "1" }
    ]
  }
]
```
  Cada objeto dentro de `Condiciones` es un par AND. Entre objetos la relación es OR. El ejemplo anterior genera:
  `AND ((ESTADOLOG='6' AND STATUS='2') OR (ESTADOLOG='4' AND STATUS='1'))`

- **Formato viejo (compatibilidad hacia atrás)** — sigue siendo válido:
```json
[
  {
    "Entidad": "Mlogis",
    "Overlay": 5,
    "EstadoLog": "4,6",
    "Status": "1,2"
  }
]
```
  Genera: `AND ESTADOLOG IN ('4','6') AND STATUS IN ('1','2')`

- **Campos**:
  - `Entidad`: nombre de la entidad SOAP. Solo se procesa la entrada con `Entidad == "Mlogis"`.
  - `Overlay`: reservado (no usado actualmente en el filtro SOAP, puede ignorarse).
  - `Condiciones`: array de pares `{EstadoLog, Status}`. OR entre pares, AND dentro de cada par. Toma precedencia sobre `EstadoLog`/`Status` planos.
  - `EstadoLog` / `Status` (formato viejo): strings separados por coma, usados si `Condiciones` no está presente.
- **Nota**: El rango temporal del filtro (`FECUPD >= desde AND FECUPD <= hasta`) lo calcula el código en runtime según el tipo de corrida (full/delta). `filters.json` solo aporta los filtros de estado.

### comparaciones_pendientes.json
- **Ubicación**: raíz del directorio de ejecución del servicio core.
- **Propósito**: Buffer persistente de IDs SOAP que aún no fueron comparados contra Oracle. Acumula IDs entre corridas para respetar `DelayComparacionMinutos`.
- **Escrito por**: `ActualizarComparacionesPendientes()` (post-corrida) y `CompararConOracle()` (post-comparación).
- **Leído por**: `CompararConOracle()`.
- **Estructura**:
```json
{
  "pendientes": [
    {
      "id": "SIL-8353914",
      "nrocomprobante": "ABC123",
      "primera_vez_visto": "2026-03-24T18:00:00",
      "corrida_origen": "delta"
    }
  ]
}
```
- **Lógica**:
  - Corrida DELTA: los IDs nuevos se agregan con `primera_vez_visto`. Los existentes actualizan `nrocomprobante` si cambió.
  - Corrida FULL: el buffer se limpia antes de repoblarse con los IDs de la corrida actual.
  - `CompararConOracle()` solo incluye en la query Oracle los IDs donde `primera_vez_visto + DelayComparacionMinutos <= DateTime.Now`. Los IDs comparados (OK, Caso A o Caso B) se remueven del buffer.

### consultas_soap.json
- **Ubicación**: raíz del directorio de ejecución del servicio core.
- **Propósito**: Configura las alertas de cambios SOAP y la query Oracle para comparación cruzada.
- **Leído por**: `EnviarAlertaCambioSoap()` y `CompararConOracle()` en `ServicioReportesOracle.cs`.
- **Campos clave**:
  - `alertas_cambios.destinatarios`: lista de mails. Si está vacía, las alertas se loguean y se omiten sin error.
  - `alertas_cambios.asunto` / `cuerpo_template`: plantillas con placeholders `{ID}`, `{Campo}`, `{ValorAnterior}`, `{ValorNuevo}`, `{Timestamp}`, `{Tipo}`.
  - `alertas_cambios.query_oracle`: query SQL con placeholder `{IDS}` que se ejecuta contra Oracle para comparar nrocomprobante.

## 🌐 Health Check del WebService SOAP

- **Trigger**: antes de cada `InvocacionSoapMlogis()`.
- **Timeout**: 60 segundos (HEAD request al endpoint `UrlWS`).
- **Estado persistido**: `Logs\ws_estado.json` con campos `ultimo_estado`, `ultima_vez_caido`, `ultima_vez_recuperado`, `alerta_caida_enviada`.
- **Lógica de alertas**:
  - WS caído + `alerta_caida_enviada = false` → envía mail de caída, setea flag, saltea corrida.
  - WS caído + `alerta_caida_enviada = true` → solo loguea, saltea corrida (no reenvía mail).
  - WS recuperado + estado previo `"caido"` → envía mail de recuperación, resetea flags.
  - WS ok + estado previo `"ok"` → flujo normal.
- **Configuración**: sección `HealthCheckSoap` en `config.json`. Se inyecta automáticamente en instalaciones existentes via `MigrarConfigSiFaltan()`.
  - `Destinatarios`: lista de mails. Si está vacía, el mail se loguea y se omite sin error.
  - `AsuntoCaido` / `CuerpoCaido`: plantillas para mail de caída. Placeholders: `{Empresa}`, `{Fecha}`, `{UrlWS}`, `{Timestamp}`.
  - `AsuntoRecuperado` / `CuerpoRecuperado`: plantillas para mail de recuperación. Agrega placeholder `{UltimaVezCaido}`.
- **Editable desde la UI**: vista Configuración General → sección "Configuración de Alertas Health Check", con campo Destinatarios y tabs "Plantilla CAÍDO" / "Plantilla RECUPERADO".

## 🗂️ Rotación de mlogis_historial.json

- Al escribir `mlogis_historial.json` después de cada corrida, se eliminan automáticamente las corridas con `fecha_ejecucion < DateTime.Now.AddDays(-7)`.
- Solo es limpieza de disco; no afecta el flujo de comparación Oracle (que opera sobre `comparaciones_pendientes.json`).
- Se loguea: `🗑️ [Historial] N corridas eliminadas por rotación (>7 días)`.

## 📋 Reglas de Desarrollo
- **Interfaz (WPF)**: Usar siempre el sistema de colores de `App.xaml`. Evitar hardcodear colores en las vistas.
- **Notificaciones**: Utilizar `MainViewModel.Instance.ShowNotification(msg)` en lugar de `MessageBox`.
- **Bindings**: Usar `UpdateSourceTrigger=PropertyChanged` para una UI reactiva y moderna.
- **Models**: Los modelos que representen JSON (`ConfigModel`, `ConsultaTaskModel`) deben implementar `INotifyPropertyChanged` y seguir la estructura anidada del archivo físico.
- **Logging**: El servicio core loguea en `Logs/Log_<DiaSemana>.txt` (ej: `Log_Lunes.txt`). Rotación semanal automática. La UI lee estos archivos con selector de día. La vista de Logs carga de forma asíncrona las últimas 1.000 líneas (con ListBox virtualizado) y muestra el total real del archivo.
- **PasswordBox**: No soporta binding directo. Sincronizar en code-behind via `PasswordChanged` → `vm.Property = box.Password`.
- **RelayCommand**: Acepta `canExecute` opcional. `CanExecuteChanged` usa `CommandManager.RequerySuggested` para re-evaluar automáticamente.

## 🔐 Seguridad
- **CryptoHelper**: AES-256 con prefijo `ENC:`. Usado para SMTP y para la clave UI.
- **ClaveUI**: Contraseña de acceso a la consola, guardada encriptada en `config.json["ClaveUI"]`. Primera vez: se auto-genera con valor "Logistica2026" encriptado.
- **Login**: `LoginWindow` implementa `INotifyPropertyChanged`. La visibilidad del error se controla via `IsPasswordWrong` binding.
- **Clave maestra de recuperación**: El login acepta una clave de recuperación hardcodeada que permite acceso independientemente de la `ClaveUI` configurada. No se loguea, no se expone en la UI, no puede ser cambiada desde la interfaz. Usar solo en caso de pérdida de acceso.
- **Cambiar Contraseña**: Vista `ChangePasswordView` + `ChangePasswordViewModel` en el menú lateral. Solo modifica `ClaveUI`; la clave maestra permanece intacta siempre.

## 🛠️ Flujo de Compilación
- Compilar siempre la solución completa `ServicioReportesOracle.sln` en modo **Release** para despliegue.
- La UI espera encontrar los archivos `.json` en `..\ServicioReportesOracle\` relativo a su ejecución.

## 🗂️ Changelog
- **v6.6**: Soporte anulados Oracle en `CompararConOracle`. `query_oracle` con `OR (id LIKE 'AN%' AND SUBSTR(id, 3) IN ({IDS}))` para Index Range Scan. C#: `idOracle.StartsWith("AN") && Contains(mlogisId)` → marcado como Encontrado (OK), sin Caso B.
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
