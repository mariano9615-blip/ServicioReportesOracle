using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class ServiceControlViewModel : INotifyPropertyChanged
    {
        private const string ServiceName = "ServicioReportesOracle";

        private string _statusText = "Desconocido";
        private bool _isRunning;
        private bool _isStopped;
        private string _uptimeText = "—";
        private readonly DispatcherTimer _timer;

        public bool IsAdmin { get; }
        public bool IsNotAdmin => !IsAdmin;

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStop));
            }
        }

        public bool IsStopped
        {
            get => _isStopped;
            set
            {
                _isStopped = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStart));
            }
        }

        public bool CanStart => IsAdmin && IsStopped;
        public bool CanStop  => IsAdmin && IsRunning;

        public string UptimeText
        {
            get => _uptimeText;
            set { _uptimeText = value; OnPropertyChanged(); }
        }

        public ICommand StartCommand    { get; }
        public ICommand StopCommand     { get; }
        public ICommand InstallCommand  { get; }
        public ICommand UninstallCommand { get; }

        public ServiceControlViewModel()
        {
            IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                          .IsInRole(WindowsBuiltInRole.Administrator);

            StartCommand     = new RelayCommand(_ => StartService());
            StopCommand      = new RelayCommand(_ => StopService());
            InstallCommand   = new RelayCommand(_ => RunBat("install.bat"));
            UninstallCommand = new RelayCommand(_ => RunBat("uninstall.bat"));

            RefreshStatus();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (s, e) => RefreshStatus();
            _timer.Start();
        }

        private void RefreshStatus()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    var status = sc.Status;
                    IsRunning = status == ServiceControllerStatus.Running;
                    IsStopped = status == ServiceControllerStatus.Stopped;

                    switch (status)
                    {
                        case ServiceControllerStatus.Running:      StatusText = "Running";        break;
                        case ServiceControllerStatus.Stopped:      StatusText = "Stopped";        break;
                        case ServiceControllerStatus.StartPending: StatusText = "Iniciando...";   break;
                        case ServiceControllerStatus.StopPending:  StatusText = "Deteniendo...";  break;
                        case ServiceControllerStatus.Paused:       StatusText = "Pausado";        break;
                        default:                                   StatusText = status.ToString(); break;
                    }

                    UptimeText = IsRunning ? GetUptime() : "—";
                }
            }
            catch (InvalidOperationException)
            {
                StatusText = "No instalado";
                IsRunning  = false;
                IsStopped  = false;
                UptimeText = "—";
            }
            catch (Exception ex)
            {
                StatusText = "Error";
                UptimeText = ex.Message;
            }
        }

        private string GetUptime()
        {
            try
            {
                var procs = Process.GetProcessesByName(ServiceName);
                if (procs.Length == 0) return "—";

                var uptime = DateTime.Now - procs[0].StartTime;
                var parts  = new List<string>();

                if (uptime.Days > 0)    parts.Add(uptime.Days == 1 ? "1 día" : $"{uptime.Days} días");
                if (uptime.Hours > 0)   parts.Add($"{uptime.Hours} hs");
                if (uptime.Minutes > 0) parts.Add($"{uptime.Minutes} min");
                if (parts.Count == 0)   parts.Add($"{uptime.Seconds} seg");

                return string.Join(", ", parts);
            }
            catch
            {
                return "—";
            }
        }

        private void StartService()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    RefreshStatus();
                    MainViewModel.Instance.ShowNotification("Servicio iniciado correctamente.", "Success");
                }
            }
            catch (InvalidOperationException)
            {
                MainViewModel.Instance.ShowNotification("Servicio no encontrado. ¿Está instalado?", "Error");
            }
            catch (System.TimeoutException)
            {
                RefreshStatus();
                MainViewModel.Instance.ShowNotification("El servicio tardó demasiado en responder. Verificá el estado manualmente.", "Error");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                MainViewModel.Instance.ShowNotification("Sin permisos. Ejecutá la UI como administrador.", "Error");
            }
            catch (Exception ex)
            {
                MainViewModel.Instance.ShowNotification($"Error al iniciar: {ex.Message}", "Error");
            }
        }

        private void StopService()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    RefreshStatus();
                    MainViewModel.Instance.ShowNotification("Servicio detenido.", "Success");
                }
            }
            catch (InvalidOperationException)
            {
                MainViewModel.Instance.ShowNotification("Servicio no encontrado. ¿Está instalado?", "Error");
            }
            catch (System.TimeoutException)
            {
                RefreshStatus();
                MainViewModel.Instance.ShowNotification("El servicio tardó demasiado en responder. Verificá el estado manualmente.", "Error");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                MainViewModel.Instance.ShowNotification("Sin permisos. Ejecutá la UI como administrador.", "Error");
            }
            catch (Exception ex)
            {
                MainViewModel.Instance.ShowNotification($"Error al detener: {ex.Message}", "Error");
            }
        }

        private void RunBat(string batFile)
        {
            try
            {
                string batPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, batFile);
                if (!File.Exists(batPath))
                {
                    MainViewModel.Instance.ShowNotification(
                        $"No se encontró {batFile} en la carpeta del ejecutable.", "Error");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName        = batPath,
                    UseShellExecute = true,
                    Verb            = "runas"
                });

                MainViewModel.Instance.ShowNotification($"Ejecutando {batFile}...", "Success");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                MainViewModel.Instance.ShowNotification("Operación cancelada por el usuario.", "Error");
            }
            catch (Exception ex)
            {
                MainViewModel.Instance.ShowNotification($"Error: {ex.Message}", "Error");
            }
        }

        public void Cleanup() => _timer?.Stop();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
