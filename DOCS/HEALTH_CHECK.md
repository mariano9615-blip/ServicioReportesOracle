# 🌐 Health Check del WebService SOAP y Circuit Breaker Oracle

## Health Check del WebService SOAP
- **Trigger**: antes de cada `InvocacionSoapMlogis()`.
- **Timeout**: 60 segundos (HEAD request al endpoint `UrlWS`).
- **Estado persistido**: `Logs\ws_estado.json` con campos `ultimo_estado`, `ultima_vez_caido`, `ultima_vez_recuperado`, `alerta_caida_enviada`, `detalle_error`, `caidas_hoy`, `recuperaciones_hoy`, `ultimo_error_xml`, `historial_eventos` (últimos 100, reset diario automático).
- **Estados posibles**: `"ok"`, `"caido"` (sin respuesta HTTP), `"auth_error"` (LoginSucceeded=false o token ausente).
- **Lógica de alertas**:
  - WS caído/auth_error + `alerta_caida_enviada = false` → envía mail de caída, setea flag, saltea corrida.
  - WS caído/auth_error + `alerta_caida_enviada = true` → solo loguea, saltea corrida (no reenvía mail).
  - WS recuperado + estado previo era falla + `alerta_caida_enviada = true` → envía mail de recuperación, resetea flags.
  - WS recuperado + estado previo era falla + `alerta_caida_enviada = false` → solo loguea, incrementa `recuperaciones_hoy` (sin mail — anti-spam).
  - WS ok + estado previo `"ok"` → flujo normal.
- **Configuración**: sección `HealthCheckSoap` en `config.json`. Se inyecta automáticamente en instalaciones existentes via `MigrarConfigSiFaltan()`.
  - `Destinatarios`: lista de mails. Si está vacía, el mail se loguea y se omite sin error.
  - `AsuntoCaido` / `CuerpoCaido`: plantillas para mail de caída. Placeholders: `{Empresa}`, `{Fecha}`, `{UrlWS}`, `{Timestamp}`.
  - `AsuntoRecuperado` / `CuerpoRecuperado`: plantillas para mail de recuperación. Agrega placeholder `{UltimaVezCaido}`.
- **Editable desde la UI**: vista Configuración General → sección "Configuración de Alertas Health Check", con campo Destinatarios y tabs "Plantilla CAÍDO" / "Plantilla RECUPERADO".

## ⚡ Circuit Breaker Oracle
- **Trigger**: se evalúa antes de cualquier apertura de `OracleConnection` en el core (`EjecutarConsultasSegunFrecuencia()` y `CompararConOracle()`, incluyendo `EjecutarComparacionMlogis()`/consultas individuales al compartir conexión).
- **Estados**:
  - `closed`: ejecución normal de Oracle.
  - `open`: se saltean corridas Oracle sin intentar conexión.
  - `half_open`: se ejecuta una prueba `SELECT 1 FROM DUAL`.
- **Umbral de apertura**: `CircuitBreakerUmbral` en `config.json` (default 3 fallos consecutivos de conexión).
- **Timeout**: `CircuitBreakerTimeoutMinutos` en `config.json` (default 15). Al cumplirse, pasa a `half_open`.
- **Prueba HALF-OPEN**:
  - Si falla: vuelve a `open` y reinicia `timestamp_apertura`.
  - Si funciona: vuelve a `closed`, resetea `fallos_consecutivos` y envía mail de recuperación.
- **Persistencia**: `Logs\oracle_circuit_state.json` con campos `estado`, `fallos_consecutivos`, `timestamp_apertura`, `alerta_enviada`.
- **Alertas SMTP** (`config.json` → `CircuitBreakerAlerta`):
  - `Destinatarios`, `AsuntoCaido`, `CuerpoCaido`, `AsuntoRecuperado`, `CuerpoRecuperado`.
  - Placeholders soportados: `{Empresa}`, `{Fecha}`, `{Timestamp}`, `{FallosConsecutivos}`.
  - **Anti-spam**: en `open` se envía una sola alerta de caída por ciclo; no se reenvía mientras `alerta_enviada=true`.
- **Logging**: todas las transiciones/eventos del breaker se registran con prefijo `[CircuitBreaker]` en `Log_<DiaSemana>.txt`.
