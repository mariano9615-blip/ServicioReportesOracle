using System;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.IO;
using ServicioReportesOracle.UI.Services;
using Newtonsoft.Json;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class SqlEditorViewModel : INotifyPropertyChanged
    {
        private string _queryText = "SELECT * FROM dual";
        private DataTable _results;
        private string _status;

        public string QueryText
        {
            get => _queryText;
            set { _queryText = value; OnPropertyChanged(); }
        }

        public DataTable Results
        {
            get => _results;
            set { _results = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public ICommand ExecuteCommand { get; }

        public SqlEditorViewModel()
        {
            ExecuteCommand = new RelayCommand(_ => RunQuery());
        }

        private void RunQuery()
        {
            try
            {
                Status = "Ejecutando consulta...";
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string configPath = Path.Combine(basePath, "..\\ServicioReportesOracle\\config.json");
                
                if (!File.Exists(configPath)) {
                    Status = "Error: No se encontró config.json";
                    return;
                }

                var json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<dynamic>(json);
                string connString = config.ConnectionString;

                var sqlService = new SqlService(connString);
                Results = sqlService.ExecuteQuery(QueryText);
                Status = $"Éxito: {Results.Rows.Count} filas devueltas.";
            }
            catch (Exception ex)
            {
                Status = "Error: " + ex.Message;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
