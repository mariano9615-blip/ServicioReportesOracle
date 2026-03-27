using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private object _selectedViewModel;
        private string _notificationMessage;
        private bool _isNotificationVisible;
        private string _notificationType; // "Success" or "Error"

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

        public ICommand NavGeneralCommand { get; }
        public ICommand NavTasksCommand { get; }
        public ICommand NavEditorCommand { get; }
        public ICommand NavLogsCommand { get; }
        public ICommand NavMlogisHistorialCommand { get; }
        public ICommand NavServiceCommand { get; }
        public ICommand NavChangePasswordCommand { get; }
        public ICommand NavUiSettingsCommand { get; }

        public static MainViewModel Instance { get; private set; }

        public MainViewModel()
        {
            Instance = this;

            NavGeneralCommand          = new RelayCommand(_ => SelectedViewModel = new GeneralConfigViewModel());
            NavTasksCommand            = new RelayCommand(_ => SelectedViewModel = new TasksViewModel());
            NavEditorCommand           = new RelayCommand(_ => SelectedViewModel = new SqlEditorViewModel());
            NavLogsCommand             = new RelayCommand(_ => SelectedViewModel = new LogsViewModel());
            NavMlogisHistorialCommand  = new RelayCommand(_ => SelectedViewModel = new MlogisHistorialViewModel());
            NavServiceCommand          = new RelayCommand(_ => SelectedViewModel = new ServiceControlViewModel());
            NavChangePasswordCommand   = new RelayCommand(_ => SelectedViewModel = new ChangePasswordViewModel());
            NavUiSettingsCommand       = new RelayCommand(_ => SelectedViewModel = new UiSettingsViewModel());

            // Default view
            SelectedViewModel = new GeneralConfigViewModel();
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
