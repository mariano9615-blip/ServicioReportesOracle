using Newtonsoft.Json;
using System;
using System.IO;

namespace ServicioOracleReportes
{
    internal sealed class OracleCircuitBreaker
    {
        private readonly object _sync = new object();
        private readonly string _statePath;
        private readonly Action<string> _log;

        private OracleCircuitState _state;
        private int _umbral;
        private int _timeoutMinutos;
        private bool _pruebaHalfOpenEnCurso;

        public OracleCircuitBreaker(string statePath, int umbral, int timeoutMinutos, Action<string> logger)
        {
            _statePath = statePath;
            _log = logger ?? (_ => { });
            _umbral = Math.Max(1, umbral);
            _timeoutMinutos = Math.Max(1, timeoutMinutos);
            _state = CargarEstado() ?? new OracleCircuitState();
            GuardarEstado();
        }

        public void ActualizarPolitica(int umbral, int timeoutMinutos)
        {
            lock (_sync)
            {
                _umbral = Math.Max(1, umbral);
                _timeoutMinutos = Math.Max(1, timeoutMinutos);
            }
        }

        public OracleCircuitAccessDecision EvaluarAcceso(DateTime ahora)
        {
            lock (_sync)
            {
                if (_state.Estado == "closed")
                    return new OracleCircuitAccessDecision(true, false, null, Snapshot());

                if (_state.Estado == "open")
                {
                    if (!_state.TimestampApertura.HasValue)
                    {
                        _state.TimestampApertura = ahora;
                        GuardarEstado();
                    }

                    var espera = TimeSpan.FromMinutes(_timeoutMinutos);
                    var tiempoAbierto = ahora - _state.TimestampApertura.Value;
                    if (tiempoAbierto < espera)
                    {
                        string razon = $"Circuito OPEN ({tiempoAbierto.TotalMinutes:F1}/{espera.TotalMinutes:F0} min).";
                        return new OracleCircuitAccessDecision(false, false, razon, Snapshot());
                    }

                    if (_pruebaHalfOpenEnCurso)
                        return new OracleCircuitAccessDecision(false, false, "HALF-OPEN: prueba en curso.", Snapshot());

                    _state.Estado = "half_open";
                    _pruebaHalfOpenEnCurso = true;
                    GuardarEstado();
                    _log("[CircuitBreaker] Timeout cumplido en OPEN. Transición a HALF-OPEN para prueba de conexión.");
                    return new OracleCircuitAccessDecision(true, true, "HALF-OPEN: ejecutar prueba Oracle.", Snapshot());
                }

                // half_open
                if (_pruebaHalfOpenEnCurso)
                    return new OracleCircuitAccessDecision(false, false, "HALF-OPEN: prueba en curso.", Snapshot());

                _pruebaHalfOpenEnCurso = true;
                return new OracleCircuitAccessDecision(true, true, "HALF-OPEN: ejecutar prueba Oracle.", Snapshot());
            }
        }

        public OracleCircuitTransition RegistrarFalloConexion(DateTime ahora, bool esPruebaHalfOpen)
        {
            lock (_sync)
            {
                if (esPruebaHalfOpen)
                    _pruebaHalfOpenEnCurso = false;

                _state.FallosConsecutivos++;

                bool cambioAOpen = false;
                if (_state.Estado == "half_open")
                {
                    _state.Estado = "open";
                    _state.TimestampApertura = ahora;
                    cambioAOpen = true;
                }
                else if (_state.Estado == "closed" && _state.FallosConsecutivos >= _umbral)
                {
                    _state.Estado = "open";
                    _state.TimestampApertura = ahora;
                    cambioAOpen = true;
                }

                GuardarEstado();

                if (cambioAOpen)
                {
                    _log($"[CircuitBreaker] Circuito OPEN por fallo de conexión Oracle (fallos consecutivos: {_state.FallosConsecutivos}, prueba_half_open={esPruebaHalfOpen}).");
                }

                bool debeAlertarCaido = _state.Estado == "open" && !_state.AlertaEnviada;
                return new OracleCircuitTransition(
                    _state.Estado,
                    _state.FallosConsecutivos,
                    debeAlertarCaido,
                    false,
                    Snapshot());
            }
        }

        public OracleCircuitTransition RegistrarConexionExitosa(DateTime ahora, bool esPruebaHalfOpen)
        {
            lock (_sync)
            {
                if (esPruebaHalfOpen)
                    _pruebaHalfOpenEnCurso = false;

                bool recuperado = _state.Estado == "half_open" || _state.Estado == "open";
                int fallosPrevios = _state.FallosConsecutivos;

                if (recuperado)
                {
                    _state.Estado = "closed";
                    _state.FallosConsecutivos = 0;
                    _state.TimestampApertura = null;
                    _state.AlertaEnviada = false;
                    GuardarEstado();
                    _log($"[CircuitBreaker] Circuito CLOSED tras recuperación Oracle (prueba_half_open={esPruebaHalfOpen}, fallos_previos={fallosPrevios}).");

                    return new OracleCircuitTransition(
                        _state.Estado,
                        fallosPrevios,
                        false,
                        true,
                        Snapshot());
                }

                if (_state.FallosConsecutivos != 0)
                {
                    _state.FallosConsecutivos = 0;
                    GuardarEstado();
                }

                return new OracleCircuitTransition(
                    _state.Estado,
                    _state.FallosConsecutivos,
                    false,
                    false,
                    Snapshot());
            }
        }

        public bool IntentarMarcarAlertaCaidaEnviada()
        {
            lock (_sync)
            {
                if (_state.Estado != "open" || _state.AlertaEnviada)
                    return false;

                _state.AlertaEnviada = true;
                GuardarEstado();
                return true;
            }
        }

        public OracleCircuitState Snapshot()
        {
            lock (_sync)
            {
                return new OracleCircuitState
                {
                    Estado = _state.Estado,
                    FallosConsecutivos = _state.FallosConsecutivos,
                    TimestampApertura = _state.TimestampApertura,
                    AlertaEnviada = _state.AlertaEnviada
                };
            }
        }

        private OracleCircuitState CargarEstado()
        {
            try
            {
                if (!File.Exists(_statePath))
                    return null;

                var estado = JsonConvert.DeserializeObject<OracleCircuitState>(File.ReadAllText(_statePath));
                if (estado == null)
                    return null;

                if (string.IsNullOrWhiteSpace(estado.Estado))
                    estado.Estado = "closed";

                estado.Estado = estado.Estado.Trim().ToLowerInvariant();
                if (estado.Estado != "closed" && estado.Estado != "open" && estado.Estado != "half_open")
                    estado.Estado = "closed";

                if (estado.FallosConsecutivos < 0)
                    estado.FallosConsecutivos = 0;

                return estado;
            }
            catch (Exception ex)
            {
                _log("[CircuitBreaker] Error cargando oracle_circuit_state.json: " + ex.Message);
                return null;
            }
        }

        private void GuardarEstado()
        {
            try
            {
                string dir = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(_statePath, JsonConvert.SerializeObject(_state, Formatting.Indented));
            }
            catch (Exception ex)
            {
                _log("[CircuitBreaker] Error guardando oracle_circuit_state.json: " + ex.Message);
            }
        }
    }

    internal sealed class OracleCircuitAccessDecision
    {
        public OracleCircuitAccessDecision(bool permitir, bool requierePrueba, string motivo, OracleCircuitState estado)
        {
            Permitir = permitir;
            RequierePrueba = requierePrueba;
            Motivo = motivo;
            Estado = estado;
        }

        public bool Permitir { get; }
        public bool RequierePrueba { get; }
        public string Motivo { get; }
        public OracleCircuitState Estado { get; }
    }

    internal sealed class OracleCircuitTransition
    {
        public OracleCircuitTransition(string estado, int fallosConsecutivos, bool debeAlertarCaido, bool debeAlertarRecuperado, OracleCircuitState snapshot)
        {
            Estado = estado;
            FallosConsecutivos = fallosConsecutivos;
            DebeAlertarCaido = debeAlertarCaido;
            DebeAlertarRecuperado = debeAlertarRecuperado;
            Snapshot = snapshot;
        }

        public string Estado { get; }
        public int FallosConsecutivos { get; }
        public bool DebeAlertarCaido { get; }
        public bool DebeAlertarRecuperado { get; }
        public OracleCircuitState Snapshot { get; }
    }

    internal sealed class OracleCircuitState
    {
        [JsonProperty("estado")]
        public string Estado { get; set; } = "closed";

        [JsonProperty("fallos_consecutivos")]
        public int FallosConsecutivos { get; set; } = 0;

        [JsonProperty("timestamp_apertura")]
        public DateTime? TimestampApertura { get; set; }

        [JsonProperty("alerta_enviada")]
        public bool AlertaEnviada { get; set; } = false;
    }
}
