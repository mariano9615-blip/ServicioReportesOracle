# ANTIGRAVITY.md - Guía de Arquitectura del Proyecto

Este archivo ayuda a Antigravity (tu asistente) a entender la estructura y reglas del proyecto rápidamente.

## 🚀 Resumen del Proyecto
**Nombre**: ServicioReportesOracle
**Tecnología**: .NET Framework 4.8 (C#)
**Propósito**: Servicio de Windows que ejecuta consultas SQL en Oracle, genera reportes Excel (ClosedXML) y los envía por SMTP. Incluye un módulo de comparación con Mlogis (SOAP Bit/Azure).

## 📁 Estructura del Código
- `ServicioReportesOracle.cs`: Lógica principal del servicio, timer, ejecución de colas y comparación SOAP.
- `SoapClient.cs`: Cliente SOAP ligero que maneja autenticación y obtención de registros (soporta JSON/XML).
- `Configuracion.cs`: Modelos de deserialización para `config.json` y `Consultas.json`.
- `Consultas.json`: Catálogo de tareas SQL, frecuencias, destinatarios y configuración de tracking.
- `config.json`: Configuración global (DB, SMTP, SOAP, Rutas).
- `TestSoap/`: Proyecto de consola para pruebas manuales de la integración Mlogis.

## 🛠️ Flujo Mlogis (v2.7)
1. **Source SOAP**: `InvocacionSoapMlogis` llama a Bit, guarda IDs actuales en `soap_ids.json`.
2. **Source Oracle**: Tarea programada en `Consultas.json` lee de Oracle, guarda en `oracle_ids.json`.
3. **Comparación**: Proceso que cruza `soap_ids.json` vs `oracle_ids.json` y detecta faltantes aplicando el delay configurado.

## 📋 Reglas de Desarrollo
- **Logging**: Usar siempre `EscribirLog(msg)` para persistir en `log.txt`.
- **Excel**: Usar `GuardarExcel(dt, path, sheetName)` para reportes consistentes.
- **Parsing**: Las respuestas SOAP de Bit suelen ser JSON dentro de un XML; usar el motor híbrido JSON/XML en `SoapClient.cs`.
- **Versionado**: Mantener tags de versión (ej: v2.7) al realizar cambios significativos.
