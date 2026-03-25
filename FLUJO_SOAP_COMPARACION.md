# FLUJO_SOAP_COMPARACION.md — Documentación técnica del flujo SOAP + Comparación Oracle

Versión: v5.7 | Última actualización: 2026-03-24

---

## 1. Diagrama de flujo de punta a punta

```
[Timer 60s - TimerElapsed()]
        |
        v
¿HabilitarMlogis = true AND FrecuenciaSoapMinutos > 0?
        |
        |--- NO --> (skip SOAP)
        |
        v
¿(DateTime.Now - UltimaEjecucionSoap) >= FrecuenciaSoapMinutos?
        |
        |--- NO --> (skip SOAP)
        |
        v
Task.Run(() => InvocacionSoapMlogis())   ← background async
        |
        v
[Health Check] VerificarWebService()  ← HEAD request a UrlWS, timeout 60s
  ├─ WS caído + alerta no enviada → enviar mail caída → setear flag → RETURN
  ├─ WS caído + alerta ya enviada → loguear → RETURN
  └─ WS ok + estado previo "caido" → enviar mail recuperación → continuar
        |
        v
Determinar tipo de corrida
  ├─ ¿(DateTime.Now - UltimaReconciliacion) >= IntervaloReconciliacionHoras? → FULL
  └─ si no → DELTA
        |
        v
Lee filters.json → obtiene filtros ESTADOLOG / STATUS
        |
        v
[1ª llamada SOAP] ObtenerRegistrosGenerico(Mlogis, FECUPD >= desde AND FECUPD <= hasta)
        |
        v
Parsea respuesta → Lista<(ID, NROCOMPROBANTE)>
        |
        v
[2ª llamada SOAP] ObtenerRegistrosGenerico(MlogisLegal, MLOGISID IN (...) AND TIPO='1')
        |
        v
Parsea respuesta → Diccionario<ID, CTG>
        |
        v
Lee mlogis_historial.json (o crea nuevo si no existe)
Si tipo=FULL:
  ├─ Guarda última corrida existente en corridaRefFull (referencia de comparación)
  └─ Limpia historial (Corridas.Clear())
        |
        v
Por cada registro (ID, NROCOMPROBANTE, CTG):
  ├─ Busca estado previo:
  │    ├─ FULL → busca en corridaRefFull (snapshot pre-Clear)
  │    └─ DELTA → busca hacia atrás en historial acumulado
  ├─ Si previo existe:
  │    ├─ ¿CTG cambió?          → agrega MlogisCambio + pone en lista alertas[]
  │    └─ ¿NROCOMPROBANTE cambió? → agrega MlogisCambio + pone en lista alertas[]
  └─ Construye MlogisRegistro con historial acumulado
        |
        v
Rotación mlogis_historial.json: eliminar corridas > 7 días
Escribe Logs\mlogis_historial.json  ← [WRITE]
Escribe Logs\ids_history.json       ← [WRITE] (compatibilidad con EjecutarComparacionMlogis)
        |
        v
Por cada (registro, cambio) en alertas[]:
  └─ EnviarAlertaCambioSoap()   ← usa consultas_soap.json
     Tipo de alerta: cambio intra-SOAP (CTG o NROCOMPROBANTE entre corridas)
        |
        v
ActualizarComparacionesPendientes()   ← v5.5
  ├─ Si tipo=FULL → limpia comparaciones_pendientes.json antes de repoblar
  ├─ Lee comparaciones_pendientes.json (o crea vacío)
  ├─ Por cada registro de la corrida:
  │    ├─ Si ID ya existe en pendientes → actualizar nrocomprobante si cambió
  │    │    pero mantener primera_vez_visto original
  │    └─ Si ID es nuevo → agregar con primera_vez_visto = fechaEjecucion
  └─ Escribe comparaciones_pendientes.json  ← [WRITE]
        |
        v
CompararConOracle()   ← v5.1, mejorado v5.2/v5.3/v5.4/v5.5
  ├─ Lee comparaciones_pendientes.json
  ├─ Filtra por delay: primera_vez_visto + DelayComparacionMinutos <= DateTime.Now
  │    ├─ IDs que NO cumplen → loguea ⏳ y los deja en pendientes
  │    └─ Si ninguno cumple → loguea y sale sin query ni mail
  ├─ Por cada chunk de IDs listos:
  │    ├─ Loguea la query: 🔍 [Oracle Query] <sql>
  │    └─ Ejecuta query contra Oracle
  ├─ Por cada resultado Oracle:
  │    ├─ Caso OK: encontrado + nrocomprobante coincide → sin alerta, remover de pendientes
  │    ├─ Caso A: encontrado pero nrocomprobante difiere → alerta, remover de pendientes
  │    └─ Caso B: no encontrado en Oracle → alerta, remover de pendientes
  ├─ Escribe comparaciones_pendientes.json sin los IDs removidos  ← [WRITE]
  └─ EnviarAlertaOracleConsolidada() si hay alertas:   ← v5.4
       ├─ Lee alertas_oracle_enviadas.json (deduplicación)
       ├─ Filtra casos ya alertados con mismo valor_oracle
       ├─ Si quedan nuevos: envía UN mail consolidado con todos los casos
       │    Asunto: "⚠️ Diferencias detectadas en Mlogis vs Oracle — {N} registros — {Fecha}"
       └─ Escribe alertas_oracle_enviadas.json actualizado
        |
        v
Persiste UltimaEjecucionSoap (y UltimaReconciliacion si fue FULL) en config.json
```

---

## 2. Archivos JSON escritos/leídos en cada paso

### Configuración (raíz — editados por el usuario)

| Paso | Archivo | Operación |
|------|---------|-----------|
| Health check | `config.json` (lee `UrlWS`, `Empresa`) | Lee |
| Inicio InvocacionSoap | `filters.json` | Lee filtros ESTADOLOG/STATUS |
| Alertas intra-SOAP y Oracle | `consultas_soap.json` | Lee destinatarios, asunto, cuerpo_template, query_oracle |
| Al finalizar | `config.json` | **Escribe** UltimaEjecucionSoap / UltimaReconciliacion |

### Operativos (`Logs\` — escritos por el servicio)

| Paso | Archivo | Operación |
|------|---------|-----------|
| Health check | `Logs\ws_estado.json` | Lee y **Escribe** (estado, timestamps, flag alerta) |
| Post-fetch | `Logs\mlogis_historial.json` | Lee historial previo |
| Post-procesamiento | `Logs\mlogis_historial.json` | **Escribe** corrida actual + rotación 7 días |
| Post-procesamiento | `Logs\ids_history.json` | **Escribe** IDs vistos (compatibilidad legacy) |
| Buffer de pendientes | `Logs\comparaciones_pendientes.json` | Lee y **Escribe** (acumula IDs entre corridas) |
| Alerta Oracle consolidada | `Logs\alertas_oracle_enviadas.json` | Lee (dedup) y **Escribe** tras envío |

Adicionalmente, `EjecutarComparacionMlogis()` (flujo paralelo por timer):

| Paso | Archivo | Operación |
|------|---------|-----------|
| Leer IDs SOAP previos | `Logs\ids_history.json` | Lee |
| Guardar IDs Oracle | `mlogis_oracle_ids.json` | **Escribe** (raíz) |
| Persistir estado | `Logs\status.json` | Lee y **Escribe** |

---

## 3. Condiciones que disparan cada tipo de alerta

### Alerta tipo "cambio intra-SOAP" (CTG o NROCOMPROBANTE)
- **Condición**: El campo `ctg` o `nrocomprobante` de un ID cambió entre la corrida anterior y la actual (comparando dentro de `mlogis_historial.json`).
- **Campo en mail**: `ctg` o `nrocomprobante`
- **ValorAnterior**: valor de la corrida previa
- **ValorNuevo**: valor de la corrida actual

### Alerta tipo "diferencia Oracle — nrocomprobante" (Caso A)
- **Condición**: El ID existe en Oracle pero el campo `nrocomprobante` en Oracle difiere del valor en `mlogis_historial.json` de la corrida actual.
- **Campo en mail**: `nrocomprobante`
- **ValorAnterior**: valor en ADMIS (Oracle)
- **ValorNuevo**: valor en Logística (Mlogis)
- **Deduplicación**: No se vuelve a alertar si `id + campo + valor_oracle` ya están registrados en `alertas_oracle_enviadas.json` con el mismo valor. Si el valor cambia, se alerta nuevamente.

### Alerta tipo "ausente en Oracle" (Caso B)
- **Condición**: El ID aparece en la corrida actual de Mlogis pero no retorna ninguna fila en la query Oracle.
- **Campo en mail**: `presencia_oracle`
- **ValorAnterior**: `"Existe en ADMIS (Oracle)"`
- **ValorNuevo**: `"No encontrado en ADMIS"`
- **Deduplicación**: Igual que Caso A. Un ID ausente solo se alerta una vez hasta que aparezca en Oracle y desaparezca del registro.

### Mail consolidado Oracle (v5.4)
Los Casos A y B se acumulan durante toda la corrida y se envían en **un único mail** al final de `CompararConOracle()`. El asunto es: `"⚠️ Diferencias detectadas en Mlogis vs Oracle — {N} registros — {Fecha}"`. Si todos los casos ya fueron alertados (deduplicación), no se envía ningún mail.

---

## 4. Cómo configurar `consultas_soap.json`

```json
{
  "alertas_cambios": {
    "destinatarios": ["usuario@empresa.com", "otro@empresa.com"],
    "asunto": "⚠️ Cambio detectado en Mlogis — {Campo} — ID {ID}",
    "cuerpo_template": "Se detectó un cambio en el movimiento MLOGIS.\n\nID: {ID}\nCampo modificado: {Campo}\nValor anterior: {ValorAnterior}\nValor nuevo: {ValorNuevo}\nDetectado: {Timestamp}\nTipo de corrida: {Tipo}",
    "query_oracle": "SELECT id, nrocomprobante FROM mlogis WHERE id IN ({IDS})"
  }
}
```

### Campos explicados

| Campo | Requerido | Descripción |
|-------|-----------|-------------|
| `destinatarios` | Sí | Lista de mails. Si está vacía, las alertas se omiten sin error (se loguea). |
| `asunto` | No | Asunto del mail. Soporta `{ID}` y `{Campo}`. Si está vacío, usa default hardcodeado. |
| `cuerpo_template` | No | Cuerpo del mail. Si está vacío, usa default hardcodeado. |
| `query_oracle` | Sí (para comparación Oracle) | Query SQL a ejecutar contra Oracle. **Debe** contener `{IDS}` como placeholder. |

### Placeholders disponibles en `asunto` y `cuerpo_template`

| Placeholder | Valor |
|-------------|-------|
| `{ID}` | ID del movimiento Mlogis |
| `{Campo}` | Campo que cambió (`ctg`, `nrocomprobante`, `presencia_oracle`) |
| `{ValorAnterior}` | Valor previo |
| `{ValorNuevo}` | Valor nuevo |
| `{Timestamp}` | Fecha/hora de detección (`dd/MM/yyyy HH:mm:ss`) |
| `{Tipo}` | Tipo de corrida: `full` o `delta` |

### Placeholder en `query_oracle`

| Placeholder | Valor |
|-------------|-------|
| `{IDS}` | Lista de IDs separados por coma (ej: `1234, 5678, 9012`). Oracle los interpreta como números. Si son strings, ajustar la query: `id IN ('{IDS}')` no es correcto para listas; se recomienda dejarlos numéricos. |

> **Nota**: Los IDs se insertan directamente en la query. Provienen del WS SOAP (no de input de usuario), por lo que el riesgo de inyección SQL es bajo. Aun así, verificar que los IDs retornados por Mlogis sean siempre numéricos antes de usar esta configuración en producción.

---

## 5. Estado de mejoras y pendientes

### Resuelto en v5.7 (UI v4.0)

- ~~**Destinatario de health check hardcodeado**~~: Corregido. La sección `HealthCheckSoap` en `config.json` permite configurar `Destinatarios`, `AsuntoCaido`, `CuerpoCaido`, `AsuntoRecuperado` y `CuerpoRecuperado`. Se inyecta automáticamente en instalaciones existentes via `MigrarConfigSiFaltan()`. Si `Destinatarios` está vacío, el mail se loguea y se omite sin error.
- **UI**: la vista Configuración General muestra el estado del WS SOAP en tiempo real (lee `Logs\ws_estado.json`) y permite editar los destinatarios y plantillas de health check con botón "Guardar Alertas Health Check".
- **Versión UI**: estandarizada a v4.0.

### Resuelto en v5.6

- ~~**Archivos operativos sueltos en raíz**~~: Corregido. Los 5 archivos operativos (`mlogis_historial.json`, `comparaciones_pendientes.json`, `alertas_oracle_enviadas.json`, `ids_history.json`, `status.json`) se mueven a `Logs\`. Al iniciar el servicio, si existen en la raíz se migran automáticamente y se loguea `📁 [Migración]`. `ws_estado.json` también vive en `Logs\`.
- ~~**Historial crece sin límite**~~: Corregido. Al escribir `mlogis_historial.json` se eliminan las corridas con `fecha_ejecucion > 7 días` y se loguea `🗑️ [Historial] N corridas eliminadas por rotación`.
- ~~**Sin detección de caída del WS SOAP**~~: Corregido. `VerificarWebService()` hace un HEAD request al endpoint antes de cada corrida (timeout 60s). Envía mail de caída una sola vez y mail de recuperación cuando vuelve. Estado persistido en `Logs\ws_estado.json`.
- ~~**UI de logs se congela con archivos grandes**~~: Corregido. `LogsView` usa un `ListBox` con virtualización (`VirtualizingPanel.VirtualizationMode=Recycling`), carga asíncrona via `Task.Run` y muestra solo las últimas 1.000 líneas con indicador "Mostrando últimas 1.000 líneas de N totales".

### Resuelto en v5.5

- ~~**IDs de corridas previas se perdían sin comparar**~~: Corregido. `comparaciones_pendientes.json` acumula IDs entre corridas SOAP. Solo se comparan contra Oracle cuando `primera_vez_visto + DelayComparacionMinutos <= DateTime.Now`. Los IDs se remueven del buffer una vez comparados (con o sin diferencia).
- ~~**Corrida FULL acumulaba IDs obsoletos**~~: Corregido. En corridas FULL, el buffer se limpia antes de repoblarlo con los IDs de la corrida actual.

### Resuelto en v5.4

- ~~**Un mail por ID en alertas Oracle**~~: Corregido. Los Casos A y B se acumulan en `alertasPendientes` y se envían en un único mail consolidado via `EnviarAlertaOracleConsolidada()`. `EnviarAlertaCambioSoap()` no se modifica.
- ~~**Sin deduplicación de alertas Oracle**~~: Corregido. `alertas_oracle_enviadas.json` registra cada combinación `id + campo + valor_oracle` ya alertada. No se reenvía mientras el valor no cambie.
- ~~**Query Oracle no logueable**~~: Corregido. Se loguea el SQL completo construido por chunk antes de ejecutarlo: `🔍 [Oracle Query] <sql>`.

### Resuelto en v5.3

- ~~**IDs como strings en Oracle**~~: Corregido. La columna `id` es `VARCHAR2`. La construcción del `IN` ahora envuelve cada ID entre comillas simples: `WHERE id IN ('1234', '5678', '9012')`. Aplica a todos los chunks del particionado.

### Resuelto en v5.2

- ~~**Corrida FULL rompe detección intra-SOAP**~~: Corregido. Se guarda `corridaRefFull` antes del `Clear()` y se usa como referencia de comparación para la corrida FULL. Las corridas DELTA no se ven afectadas.
- ~~**Límite de 1000 IDs en Oracle**~~: Corregido. `CompararConOracle()` particiona automáticamente en chunks de 999 elementos, reutilizando la misma conexión Oracle.
- ~~**`filters.json` no documentado**~~: Documentado en ANTIGRAVITY.md (sección "Archivos de Configuración SOAP").
- ~~**Coexistencia de mecanismos no documentada**~~: Documentado en ANTIGRAVITY.md con roles, archivos y ámbitos de cada uno.

### Pendiente / Acción requerida

1. **`destinatarios` vacío en producción**: `consultas_soap.json` tiene `"destinatarios": []`. Ningún mail se envía hasta configurar al menos un destinatario real. **Acción**: editar `consultas_soap.json` y agregar mails.

2. **Doble alerta Caso A vs intra-SOAP**: Si el `nrocomprobante` cambia entre corridas SOAP Y además difiere de Oracle, se enviarán dos mails para el mismo ID (uno intra-SOAP via `EnviarAlertaCambioSoap`, otro Oracle via `EnviarAlertaOracleConsolidada`). La deduplicación Oracle no cubre alertas intra-SOAP. Evaluar si el doble aviso es aceptable en producción.
