# Guía de Actualización Manual (Producción)

Si ya tienes el servicio configurado en producción, agrega estas líneas a tus archivos JSON existentes para activar el monitoreo de Mlogis.

## 1. Actualizar `config.json`
Agrega estos campos dentro del objeto principal (recuerda las comas entre líneas):

```json
  "RutaSQL": "C:\\Ruta\\A\\Tu\\Carpeta\\sql",
  "Dominio": "TU_DOMINIO",
  "UrlAutentificacion": "https://auth.tu_url.com",
  "UrlWS": "https://ws.tu_url.com",
  "FrecuenciaSoapMinutos": 10,
  "DelayComparacionMinutos": 10
```

### Explicación:
- **`RutaSQL`**: Directorio donde el monitor buscará el archivo `.sql`.
- **`Dominio`**: Tu dominio Mlogis (usado para login).
- **`UrlAutentificacion`** y **`UrlWS`**: Direcciones de los servicios de Azure.
- **`FrecuenciaSoapMinutos`**: Tiempo entre cada descarga de datos desde Azure.
- **`DelayComparacionMinutos`**: Margen de tiempo (en minutos) para esperar antes de alertar que un ID falta en Oracle. Evita falsas alarmas por demoras de la red.

## 2. Actualizar `consultas.json`
Añade este nuevo objeto al final de tu lista de consultas para activar la comparación:

```json
  {
    "Nombre": "ComparacionMlogisOracle",
    "Archivo": "MlogisOracle.sql",
    "FrecuenciaMinutos": 3,
    "Destinatarios": ["tu_correo@bit.com.ar"],
    "EnviarCorreo": true,
    "Track": true,
    "CampoTrack": "ID",
    "ExcluirCampos": ["ID"],
    "CamposCorreo": {
      "Errores": ["ID", "CTG"],
      "Resueltos": ["ID", "CTG"]
    },
    "Mail": {
      "AsuntoConError": "⚠️ IDs Faltantes en Oracle – {Nombre} – {Fecha}",
      "AsuntoSinError": "✔️ Sincronización OK – {Nombre} – {Fecha}",
      "CuerpoConError": "Estimados,\n\nSe identificaron {Cantidad} IDs en SOAP que aún no llegan a Oracle (fuera del delay configurado).\n\nDetalle:\n{Lista}",
      "CuerpoSinError": "No se detectaron IDs faltantes.\nÚltima verificación: {Fecha}."
    }
  }
```

### Explicación:
- **`Nombre`**: Debe ser exactamente `ComparacionMlogisOracle` para que el servicio use la lógica de delay.
- **`Archivo`**: El nombre del archivo SQL que tendrás en la carpeta `sql\`.
- **`FrecuenciaMinutos`**: Cada cuánto quieres que el servicio revise Oracle (ej: cada 3 min).

## 3. Crear `filters.json` (Archivo Nuevo)
Si no existe, créalo en la raíz del ejecutable con este contenido mínimo:
```json
[
  {
    "Entidad": "Mlogis",
    "Filtro": "FECHA >= '{FECHA_DESDE}' AND FECHA <= '{FECHA_HASTA}'"
  }
]
```

## 4. Carpeta `sql\` y Archivo `.sql`
Asegúrate de crear la carpeta `sql` en la raíz del servicio y que dentro esté el archivo `MlogisOracle.sql` con tu consulta:
```sql
SELECT id as ID FROM mi_tabla_mlogis
```
*(Es importante el `as ID` para que el sistema encuentre la columna)*
