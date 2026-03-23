using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ServicioReportesOracle.UI.Models
{
    public class ConsultaTaskModel : INotifyPropertyChanged
    {
        private string _nombre;
        public string Nombre { get => _nombre; set { _nombre = value; OnPropertyChanged(); } }

        private string _archivo;
        public string Archivo { get => _archivo; set { _archivo = value; OnPropertyChanged(); } }

        private int _frecuenciaMinutos;
        public int FrecuenciaMinutos { get => _frecuenciaMinutos; set { _frecuenciaMinutos = value; OnPropertyChanged(); } }

        private List<string> _destinatarios = new List<string>();
        public List<string> Destinatarios { get => _destinatarios; set { _destinatarios = value; OnPropertyChanged(); } }

        private bool _enviarCorreo;
        public bool EnviarCorreo { get => _enviarCorreo; set { _enviarCorreo = value; OnPropertyChanged(); } }

        private bool _track;
        public bool Track { get => _track; set { _track = value; OnPropertyChanged(); } }

        private string _campoTrack;
        public string CampoTrack { get => _campoTrack; set { _campoTrack = value; OnPropertyChanged(); } }

        private List<string> _excluirCampos = new List<string>();
        public List<string> ExcluirCampos { get => _excluirCampos; set { _excluirCampos = value; OnPropertyChanged(); } }

        private CamposCorreoModel _camposCorreo = new CamposCorreoModel();
        public CamposCorreoModel CamposCorreo { get => _camposCorreo; set { _camposCorreo = value; OnPropertyChanged(); } }

        private MailTemplatesModel _mail = new MailTemplatesModel();
        public MailTemplatesModel Mail { get => _mail; set { _mail = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class CamposCorreoModel : INotifyPropertyChanged
    {
        private List<string> _errores = new List<string>();
        public List<string> Errores { get => _errores; set { _errores = value; OnPropertyChanged(); } }

        private List<string> _resueltos = new List<string>();
        public List<string> Resueltos { get => _resueltos; set { _resueltos = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // Estructura plana que mapea directamente con el JSON:
    // "Mail": { "AsuntoConError": "...", "CuerpoConError": "...", "AsuntoSinError": "...", "CuerpoSinError": "..." }
    public class MailTemplatesModel : INotifyPropertyChanged
    {
        private string _asuntoConError;
        public string AsuntoConError { get => _asuntoConError; set { _asuntoConError = value; OnPropertyChanged(); } }

        private string _cuerpoConError;
        public string CuerpoConError { get => _cuerpoConError; set { _cuerpoConError = value; OnPropertyChanged(); } }

        private string _asuntoSinError;
        public string AsuntoSinError { get => _asuntoSinError; set { _asuntoSinError = value; OnPropertyChanged(); } }

        private string _cuerpoSinError;
        public string CuerpoSinError { get => _cuerpoSinError; set { _cuerpoSinError = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
