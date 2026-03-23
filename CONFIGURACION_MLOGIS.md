# Guía de Configuración: Monitor Mlogis vs Oracle

Sigue estos pasos para configurar correctamente la comparación entre el servicio externo (Azure/SOAP) y tu base de datos Oracle.

## 1. Configuración General (`config.json`)
Abre el archivo `config.json` en la carpeta raíz del servicio y asegúrate de completar estos campos:

- **`Dominio`**: Tu dominio de autenticación Mlogis.
- **`UrlAutentificacion`**: La URL para obtener el token.
- **`UrlWS`**: La URL del servicio web (Web Service).
- **`FrecuenciaSoapMinutos`**: Cada cuánto tiempo el monitor descarga los IDs de Azure (ej. `10`).
- **`DelayComparacionMinutos`**: Cuántos minutos esperar antes de reportar un ID como faltante (ej. `15`). Esto evita alertas falsas por demoras de la red mlogis-oracle.
- **`RutaSQL`**: La ruta absoluta a la carpeta `sql` de tu proyecto.

## 2. Configuración de Filtros SOAP (`filters.json`)
En este archivo defines qué datos traer de Azure:
- **`Entidad`**: Nombre de la entidad (ej. `"Mlogis"`).
- **`Filtro`**: La condición SQL-like para Azure (ej. `FECHA >= '{FECHA_DESDE}'`). El sistema reemplaza automáticamente las fechas de los últimos 3 días.

## 3. Configuración de Consultas y Correo (`consultas.json`)
Busca o agrega la entrada con `"Nombre": "ComparacionMlogisOracle"`. Aquí configuras todo lo relacionado con Oracle y el envío de alertas:

- **`Archivo`**: Debe decir `"MlogisOracle.sql"`.
- **`FrecuenciaMinutos`**: Cada cuánto tiempo el monitor consulta a Oracle y hace la comparación (ej. `3`).
- **`Destinatarios`**: Lista de correos que recibirán las alertas.
- **`Mail`**: Aquí puedes editar el **Asunto** y el **Cuerpo** del mensaje que se envía cuando faltan IDs.

## 4. La Consulta Oracle (`sql/MlogisOracle.sql`)
En este archivo escribe el `SELECT` que obtiene los IDs desde tu base de datos Oracle:
```sql
SELECT id as ID FROM mi_tabla_mlogis
```
*Nota: Asegúrate de usar `as ID` para que coincida con lo que el monitor espera.*

## 5. Auditoría y Logs
- **`log.txt`**: Revisa este archivo para confirmar que el monitor se está autenticando correctamente y ver cuántos IDs encontró.
- **`ids_history.json`**: Este archivo se crea solo. Puedes abrirlo para ver todos los IDs que el monitor ha detectado en SOAP y en qué horario los vio por primera vez.
- **`status.json`**: Aquí el monitor guarda qué IDs ya reportó como error para no enviarte el mismo correo muchas veces.

---
**¿Cómo probar rápido?**
1. Cambia `FrecuenciaSoapMinutos` a `1`.
2. Cambia `FrecuenciaMinutos` en `consultas.json` a `1`.
3. Baja el `DelayComparacionMinutos` a `1`.
4. Reinicia el servicio y revisa el `log.txt`.
