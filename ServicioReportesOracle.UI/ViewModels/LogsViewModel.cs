using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class LogsViewModel : INotifyPropertyChanged
    {
        private string _logContent;
        private readonly string _logPath;
        private readonly DispatcherTimer _timer;

        public string LogContent
        {
            get => _logContent;
            set { _logContent = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ClearCommand { get; }

        public LogsViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _logPath = Path.Combine(basePath, "..\\ServicioReportesOracle\\log.txt");

            RefreshCommand = new RelayCommand(_ => LoadLogs());
            ClearCommand = new RelayCommand(_ => ClearLogs());

            LoadLogs();

            // Auto-refresh every 5 seconds
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (s, e) => LoadLogs();
            _timer.Start();
        }

        private void LoadLogs()
        {
            try
            {
                if (File.Exists(_logPath))
                {
                    // Read with sharing to avoid locking
                    using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fs))
                    {
                        LogContent = reader.ReadToEnd();
                    }
                }
                else
                {
                    LogContent = "Archivo de log no encontrado en: " + _logPath;
                }
            }
            catch (Exception ex)
            {
                LogContent = "Error leyendo logs: " + ex.Message;
            }
        }

        private void ClearLogs()
        {
            try
            {
                File.WriteAllText(_logPath, $"--- Log limpiado el {DateTime.Now} ---" + Environment.NewLine);
                LoadLogs();
            }
            catch (Exception ex)
            {
                LogContent = "Error al limpiar logs: " + ex.Message;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
