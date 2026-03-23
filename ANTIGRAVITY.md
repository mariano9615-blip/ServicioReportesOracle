# ANTIGRAVITY.md - Guía de Arquitectura del Proyecto (v3.2)

Este archivo es la fuente de verdad para Antigravity. Mantenlo actualizado para un trabajo óptimo.

## 🚀 Resumen del Proyecto (v3.2)
**Nombre**: ServicioReportesOracle
**Versión Actual**: v3.2 (Premium UI & Core Estable)
**Tecnología**: .NET Framework 4.8 (C#)
**Propósito**: Ecosistema para ejecución de reportes Oracle, envío de correos SMTP e integración SOAP con Mlogis.

## 📁 Estructura de la Solución
### 1. ⚙️ ServicioReportesOracle (Core)
- **Lógica**: Manejo de cronogramas, ejecución SQL (Oracle), generación Excel (ClosedXML) y envío de mails.
- **Mlogis**: Integración SOAP para comparación de registros.
- **Configs**: `config.json` (Global) y `Consultas.json` (Tareas).

### 2. 💎 ServicioReportesOracle.UI (WPF)
- **Arquitectura**: MVVM pura.
- **Vistas**: `GeneralConfigView`, `TasksView` (Gestión ABM), `SqlEditorView` (Testing), `LogsView`.
- **Diseño**: Tema oscuro premium con notificaciones tipo "Toast" incorporadas.
- **Modelos**: Estructura anidada para configuración de mails (`Mail.ConError.Asunto`, etc.).

### 3. 🧪 TestSoap (Console)
- Herramienta rápida para debuggear la conectividad con el WS de Mlogis sin levantar todo el servicio.

## 📋 Reglas de Desarrollo
- **Interfaz (WPF)**: Usar siempre el sistema de colores de `App.xaml`. Evitar hardcodear colores en las vistas.
- **Notificaciones**: Utilizar `MainViewModel.Instance.ShowNotification(msg)` en lugar de `MessageBox`.
- **Bindings**: Usar `UpdateSourceTrigger=PropertyChanged` para una UI reactiva y moderna.
- **Models**: Los modelos que representen JSON (`ConfigModel`, `ConsultaTaskModel`) deben implementar `INotifyPropertyChanged` y seguir la estructura anidada del archivo físico.
- **Logging**: El servicio core loguea en `log.txt` en su raíz. La UI lee este archivo para la vista de Logs.

## 🛠️ Flujo de Compilación
- Compilar siempre la solución completa `ServicioReportesOracle.sln` en modo **Release** para despliegue.
- La UI espera encontrar los archivos `.json` en `..\ServicioReportesOracle\` relativo a su ejecución.
