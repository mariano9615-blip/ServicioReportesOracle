using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ServicioOracleReportes
{
    public class MlogisHistorial
    {
        [JsonProperty("corridas")]
        public List<MlogisCorrida> Corridas { get; set; } = new List<MlogisCorrida>();
    }

    public class MlogisCorrida
    {
        [JsonProperty("fecha_ejecucion")]
        public DateTime FechaEjecucion { get; set; }

        [JsonProperty("tipo")]
        public string Tipo { get; set; } = "";

        [JsonProperty("duracion_segundos")]
        public double DuracionSegundos { get; set; } = 0;

        [JsonProperty("registros")]
        public List<MlogisRegistro> Registros { get; set; } = new List<MlogisRegistro>();
    }

    public class MlogisRegistro
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("nrocomprobante")]
        public string NroComprobante { get; set; } = "";

        [JsonProperty("ctg")]
        public string Ctg { get; set; } = "";

        [JsonProperty("primera_vez_visto")]
        public DateTime PrimeraVezVisto { get; set; }

        [JsonProperty("ultima_vez_visto")]
        public DateTime UltimaVezVisto { get; set; }

        [JsonProperty("fecupd")]
        public string FecUpd { get; set; } = "";

        [JsonProperty("planta")]
        public string Planta { get; set; } = "";

        [JsonProperty("tipocomprobante")]
        public string TipoComprobante { get; set; } = "";

        [JsonProperty("anulado")]
        public bool Anulado { get; set; } = false;

        [JsonProperty("id_anulado_oracle")]
        public string IdAnuladoOracle { get; set; } = "";

        [JsonProperty("cambios_detectados")]
        public List<MlogisCambio> CambiosDetectados { get; set; } = new List<MlogisCambio>();
    }

    public class MlogisCambio
    {
        [JsonProperty("campo")]
        public string Campo { get; set; } = "";

        [JsonProperty("valor_anterior")]
        public string ValorAnterior { get; set; } = "";

        [JsonProperty("valor_nuevo")]
        public string ValorNuevo { get; set; } = "";

        [JsonProperty("detectado")]
        public DateTime Detectado { get; set; }
    }

    public class MlogisHistoricoMensual
    {
        [JsonProperty("generado")]
        public DateTime Generado { get; set; }

        [JsonProperty("dias")]
        public List<MetricaDiaria> Dias { get; set; } = new List<MetricaDiaria>();
    }

    public class MetricaDiaria
    {
        [JsonProperty("fecha")]
        public string Fecha { get; set; } = "";

        [JsonProperty("total_corridas")]
        public int TotalCorridas { get; set; }

        [JsonProperty("corridas_full")]
        public int CorridasFull { get; set; }

        [JsonProperty("corridas_delta")]
        public int CorridasDelta { get; set; }

        [JsonProperty("total_registros_pico")]
        public int TotalRegistrosPico { get; set; }

        [JsonProperty("cambios_soap_detectados")]
        public int CambiosSoapDetectados { get; set; }

        [JsonProperty("alertas_oracle_enviadas")]
        public int AlertasOracleEnviadas { get; set; }

        [JsonProperty("duracion_promedio_segundos")]
        public double DuracionPromedioSegundos { get; set; }
    }
}
