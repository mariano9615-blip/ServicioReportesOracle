using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private object _selectedViewModel;
        private string _notificationMessage;
        private bool _isNotificationVisible;
        private string _notificationType; // "Success" or "Error"

        private string _sidebarAlertText;
        private bool _sidebarAlertVisible;

        private FileSystemWatcher _watcherAlertas;
        private DispatcherTimer _timerAlertas;
        private bool _isUpdatingAlertas;
        private readonly object _alertasLock = new object();
        private string _alertasEnviadasPath;
        private string _alertasLeidasPath;

        public object SelectedViewModel
        {
            get => _selectedViewModel;
            set { _selectedViewModel = value; OnPropertyChanged(); }
        }

        public string NotificationMessage
        {
            get => _notificationMessage;
            set { _notificationMessage = value; OnPropertyChanged(); }
        }

        public bool IsNotificationVisible
        {
            get => _isNotificationVisible;
            set { _isNotificationVisible = value; OnPropertyChanged(); }
        }

        public string NotificationType
        {
            get => _notificationType;
            set { _notificationType = value; OnPropertyChanged(); }
        }

        public string SidebarAlertText
        {
            get => _sidebarAlertText;
            set { _sidebarAlertText = value; OnPropertyChanged(); }
        }

        public bool SidebarAlertVisible
        {
            get => _sidebarAlertVisible;
            set { _sidebarAlertVisible = value; OnPropertyChanged(); }
        }

        public ICommand NavDashboardCommand { get; }
        public ICommand NavGeneralCommand { get; }
        public ICommand NavTasksCommand { get; }
        public ICommand NavEditorCommand { get; }
        public ICommand NavLogsCommand { get; }
        public ICommand NavMlogisHistorialCommand { get; }
        public ICommand NavMetricasCommand { get; }
        public ICommand NavServiceCommand { get; }
        public ICommand NavChangePasswordCommand { get; }
        public ICommand NavUiSettingsCommand { get; }
        public ICommand NavAlertasCommand { get; }

        public static MainViewModel Instance { get; private set; }

        public MainViewModel()
        {
            Instance = this;

            NavDashboardCommand        = new RelayCommand(_ => SelectedViewModel = new DashboardViewModel());
            NavGeneralCommand          = new RelayCommand(_ => SelectedViewModel = new GeneralConfigViewModel());
            NavTasksCommand            = new RelayCommand(_ => SelectedViewModel = new TasksViewModel());
            NavEditorCommand           = new RelayCommand(_ => SelectedViewModel = new SqlEditorViewModel());
            NavLogsCommand             = new RelayCommand(_ => SelectedViewModel = new LogsViewModel());
            NavMlogisHistorialCommand  = new RelayCommand(_ => SelectedViewModel = new MlogisHistorialViewModel());
            NavMetricasCommand         = new RelayCommand(_ => SelectedViewModel = new MetricasViewModel());
            NavServiceCommand          = new RelayCommand(_ => SelectedViewModel = new ServiceControlViewModel());
            NavChangePasswordCommand   = new RelayCommand(_ => SelectedViewModel = new ChangePasswordViewModel());
            NavUiSettingsCommand       = new RelayCommand(_ => SelectedViewModel = new UiSettingsViewModel());
            NavAlertasCommand          = new RelayCommand(_ => SelectedViewModel = new AlertasViewModel());

            // Default view
            SelectedViewModel = new DashboardViewModel();

            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string logsDir = Path.GetFullPath(Path.Combine(basePath, @"..\ServicioReportesOracle\Logs\json"));

            _alertasEnviadasPath = Path.Combine(logsDir, "alertas_smtp_enviadas.json");
            _alertasLeidasPath = Path.Combine(logsDir, "alertas_leidas.json");

            ConfigurarWatcher();
            ConfigurarTimer();
            _ = CargarAlertasSidebarAsync();
        }

        private void ConfigurarWatcher()
        {
            try
            {
                string dir = Path.GetDirectoryName(_alertasEnviadasPath);
                if (Directory.Exists(dir))
                {
                    _watcherAlertas = new FileSystemWatcher(dir)
                    {
                        Filter = "alertas_smtp_enviadas.json",
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    _watcherAlertas.Changed += (s, e) => { _ = CargarAlertasSidebarAsync(); };
                    _watcherAlertas.Created += (s, e) => { _ = CargarAlertasSidebarAsync(); };
                }
            }
            catch { }
        }

        private void ConfigurarTimer()
        {
            _timerAlertas = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _timerAlertas.Tick += (s, e) => { _ = CargarAlertasSidebarAsync(); };
            _timerAlertas.Start();
        }

        public async Task CargarAlertasSidebarAsync()
        {
            lock (_alertasLock)
            {
                if (_isUpdatingAlertas) return;
                _isUpdatingAlertas = true;
            }

            try
            {
                await Task.Delay(2000); // 2 segundos de debounce
                
                int totalAlertasNoLeidasHoy = 0;
                var idsLeidos = new HashSet<string>();

                // Cargar IDs leídos
                await Task.Run(() =>
                {
                    try
                    {
                        if (File.Exists(_alertasLeidasPath))
                        {
                            var json = File.ReadAllText(_alertasLeidasPath);
                            var arr = JArray.Parse(json);
                            foreach (var token in arr)
                            {
                                string id = token.ToString();
                                if (!string.IsNullOrEmpty(id))
                                    idsLeidos.Add(id);
                            }
                        }
                    }
                    catch { }
                });

                // Contar alertas del día no leídas desde alertas_smtp_enviadas.json
                if (File.Exists(_alertasEnviadasPath))
                {
                    string json = null;
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            using (var fs = new FileStream(_alertasEnviadasPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var sr = new StreamReader(fs))
                                json = sr.ReadToEnd();
                            break;
                        }
                        catch { await Task.Delay(500); }
                    }

                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            var container = JObject.Parse(json);
                            var alertas = container["alertas"] as JArray ?? new JArray();
                            
                            foreach (var a in alertas)
                            {
                                string tsStr = a["timestamp"]?.ToString();
                                string tipo = a["tipo"]?.ToString();

                                if (DateTime.TryParse(tsStr, out var dt) && dt.Date == DateTime.Today)
                                {
                                    // Key compacta según ajuste v7.3.2
                                    string alertaId = $"{dt:yyyy-MM-ddTHH:mm:ss}_{tipo}";
                                    if (!idsLeidos.Contains(alertaId))
                                    {
                                        totalAlertasNoLeidasHoy++;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }

                await Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (totalAlertasNoLeidasHoy == 0)
                    {
                        SidebarAlertVisible = false;
                        SidebarAlertText = "";
                    }
                    else
                    {
                        SidebarAlertText = totalAlertasNoLeidasHoy > 99 ? "99+" : totalAlertasNoLeidasHoy.ToString();
                        SidebarAlertVisible = true;
                    }
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] Badge actualizado: {totalAlertasNoLeidasHoy} alertas no leídas de hoy");
                });
            }
            catch { }
            finally
            {
                lock (_alertasLock)
                {
                    _isUpdatingAlertas = false;
                }
            }
        }

        public void StopBackgroundTasks()
        {
            if (_watcherAlertas != null)
            {
                _watcherAlertas.EnableRaisingEvents = false;
                _watcherAlertas.Dispose();
            }
            if (_timerAlertas != null)
            {
                _timerAlertas.Stop();
            }
        }

        public void ShowNotification(string message, string type = "Success")
        {
            NotificationMessage = message;
            NotificationType = type;
            IsNotificationVisible = true;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) => {
                IsNotificationVisible = false;
                timer.Stop();
            };
            timer.Start();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add { System.Windows.Input.CommandManager.RequerySuggested += value; }
            remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
        }
    }
}
