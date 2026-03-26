# ANTIGRAVITY.md - Guía de Arquitectura del Proyecto (v6.5)

## 🚀 Resumen del Proyecto
**Nombre**: ServicioReportesOracle
**Versión Actual**: v6.5 (UI v4.2)
**Tecnología**: .NET Framework 4.8 (C#)

## 📁 Novedades v6.5 (Blindaje PROD)
- **Health Check Pro**: Validación de Auth SOAP (ResultCode) + 3 Retries antes de alerta.
- **Auditoría**: 'ws_estado.json' con contadores diarios ('caidas_hoy', 'recuperaciones_hoy') e historial de eventos.
- **Logs UX**: Carga incremental (Delta) en UI para evitar bloqueos y Auto-Scroll inteligente.
- **Seguridad**: Implementación de 'FileShare.ReadWrite' para evitar bloqueos de archivos entre Servicio y UI.
- **Mails**: URLs en texto plano para bypass de Safelinks y deduplicación de envíos (No hay Recuperado sin Caída previa).

## 🗂️ Estructura Operativa (Logs\)
- **ws_estado.json**: Estado de salud, contadores y log de errores XML.
- **mlogis_historial.json**: Auditoría de cambios en comprobantes (Rotación 7 días).
- **Log_<Dia>.txt**: Logs detallados con rotación semanal.

## 📋 Reglas de Oro
- **UI**: No bloquear el hilo principal. Usar IsBusy solo para cargas iniciales de día.
- **Concurrencia**: Siempre usar 'lock' al escribir en JSONs operativos.
