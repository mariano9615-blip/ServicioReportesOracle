# 🗂️ Archivos de Configuración SOAP (detalle)

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
  - Corrida FULL inteligente (v6.7): ya no limpia ciegamente el buffer. Compara `fecupd` de Mlogis vs `primera_vez_visto` del buffer antes de actualizar el historial. Solo los IDs verdaderamente nuevos o actualizados (fecupd posterior a `primera_vez_visto`) van a `comparaciones_pendientes.json`; el resto se descarta sin generar alerta.
  - `CompararConOracle()` solo incluye en la query Oracle los IDs donde `primera_vez_visto + DelayComparacionMinutos <= fechaEjecucion` (usando el timestamp de la corrida como referencia). Los IDs comparados (OK, Caso A o Caso B) se remueven del buffer.
  - **v7.3.6 — Fix parseo**: se utiliza `entry.Value<DateTime?>()` para evitar fallos por cultura del sistema que antes reseteaban `primera_vez_visto` a `MinValue` y disparaban alertas inmediatas.

### alertas_oracle_enviadas.json
- **Ubicación**: `Logs\` (generado por el servicio, no editar manualmente).
- **Propósito**: Historial acumulativo de alertas Oracle enviadas. Usado para deduplicación: si ya se envió una alerta para el mismo ID + TipoCaso en el día actual, se omite el reenvío.
- **Escrito por**: `EnviarAlertaOracleConsolidada()` en `ServicioReportesOracle.cs`.
- **Leído por**: `EnviarAlertaOracleConsolidada()`.
- **Estructura** (array plano — v6.9.1):
```json
[
  {
    "id": "SIL-8374477",
    "tipo_caso": "B",
    "timestamp": "2026-03-27T10:15:00",
    "nrocomprobante": "ABC123"
  }
]
```
- **Lógica**:
  - **Dedup**: clave = `id + tipo_caso + DateTime.Today`. Si ya existe una entrada con esa clave para hoy, la alerta no se reenvía.
  - **Acumulación**: al escribir, se re-lee el array del disco, se purgan entradas cuyo `timestamp.Date < DateTime.Today` (para no crecer indefinidamente) y se agregan las nuevas alertas de la corrida actual.
  - **Primera corrida del día**: la purga de entradas del día anterior ocurre automáticamente en la primera escritura del día.
- **Nota**: el formato cambió en v6.9.1 de `{"alertas": [...]}` a un array plano `[...]` con campos `id`, `tipo_caso`, `timestamp`, `nrocomprobante`. Archivos con el formato anterior son migrados automáticamente por `LeerDedupCompatible()` (servicio) y por el fallback en `CargarAlertas()` (UI) — mapeo: `ultima_vez_alertado → timestamp`, `campo → tipo_caso` (default "A").

### consultas_soap.json
- **Ubicación**: raíz del directorio de ejecución del servicio core.
- **Propósito**: Configura las alertas de cambios SOAP y la query Oracle para comparación cruzada.
- **Leído por**: `EnviarAlertaCambioSoap()` y `CompararConOracle()` en `ServicioReportesOracle.cs`.
- **Campos clave**:
  - `alertas_cambios.destinatarios`: lista de mails. Si está vacía, las alertas se loguean y se omiten sin error.
  - `alertas_cambios.asunto` / `cuerpo_template`: plantillas con placeholders `{ID}`, `{Campo}`, `{ValorAnterior}`, `{ValorNuevo}`, `{Timestamp}`, `{Tipo}`.
  - `alertas_cambios.query_oracle`: query SQL con dos placeholders:
    - `{IDS}`: lista de IDs Mlogis exactos para el match directo.
    - `{IDS_TRUNCADOS}`: lista de IDs Mlogis truncados a 15 chars para el match de anulados via `SUBSTR(id, 3, 15)`. El trigger Oracle forma el ID anulado como `'AN' + SUBSTR(idOriginal, 1, 15) + sufijo_numérico`, por lo que el match correcto es `SUBSTR(id, 3, 15) IN ({IDS_TRUNCADOS})`. Ambos placeholders son reemplazados por `CompararConOracle()` antes de ejecutar la query.

### AlertaPendientes (config.json)
- **Ubicación**: sección `AlertaPendientes` dentro de `config.json` (raíz del servicio core).
- **Propósito**: alerta proactiva por crecimiento de `comparaciones_pendientes.json` para detectar fallas silenciosas de Oracle.
- **Campos**:
  - `Destinatarios`: lista de mails para alertas/resolución.
  - `UmbralCantidad`: cantidad mínima de pendientes para disparar alerta (default 50).
  - `CooldownHoras`: ventana anti-spam entre alertas consecutivas por umbral (default 4h).
  - `AsuntoAlerta` / `CuerpoAlerta`: plantilla de alerta al superar umbral.
  - `AsuntoResolucion` / `CuerpoResolucion`: plantilla cuando pendientes vuelve por debajo del umbral.
- **Placeholders soportados**: `{CantidadActual}`, `{IdMasAntiguo}`, `{HorasEnBuffer}`, `{Timestamp}`, `{Empresa}`.
- **Persistencia anti-spam**: `Logs\pendientes_alerta_estado.json` con:
  - `ultimo_envio`: último envío de alerta por umbral.
  - `cantidad_al_enviar`: cantidad registrada al enviar la alerta.
