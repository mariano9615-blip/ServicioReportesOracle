using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.IO;
using ServicioOracleReportes;
using ServicioReportesOracle.UI.Models;
using ServicioReportesOracle.UI.Services;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class TasksViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<ConsultaTaskModel> _tasks;
        private ConsultaTaskModel _selectedTask;
        private ConsultaTaskModel _pendingDeleteTask;
        private bool _isBusy;
        private readonly ConfigService _service;

        public ObservableCollection<ConsultaTaskModel> Tasks
        {
            get => _tasks;
            set { _tasks = value; OnPropertyChanged(); }
        }

        public ConsultaTaskModel SelectedTask
        {
            get => _selectedTask;
            set { _selectedTask = value; OnPropertyChanged(); }
        }

        public ConsultaTaskModel PendingDeleteTask
        {
            get => _pendingDeleteTask;
            set { _pendingDeleteTask = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
        }

        public bool IsNotBusy => !_isBusy;

        public ICommand SaveCommand { get; }
        public ICommand AddTaskCommand { get; }
        public ICommand DeleteTaskCommand { get; }
        public ICommand ConfirmDeleteCommand { get; }
        public ICommand CancelDeleteCommand { get; }
        public ICommand TestEmailCommand { get; }

        public TasksViewModel()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(basePath, "..\\ServicioReportesOracle\\config.json");
            string consultasPath = Path.Combine(basePath, "..\\ServicioReportesOracle\\Consultas.json");

            _service = new ConfigService(configPath, consultasPath);
            var list = _service.LoadConsultas();
            Tasks = new ObservableCollection<ConsultaTaskModel>(list);

            if (Tasks.Any()) SelectedTask = Tasks.First();

            SaveCommand = new RelayCommand(_ => {
                _service.SaveConsultas(Tasks.ToList());
                MainViewModel.Instance.ShowNotification("Configuración guardada. El servicio recargará automáticamente sin reiniciarse.", "Success");
            });

            AddTaskCommand = new RelayCommand(_ => {
                var newTask = new ConsultaTaskModel { Nombre = "Nueva Consulta", Archivo = "query.sql", FrecuenciaMinutos = 10 };
                Tasks.Add(newTask);
                SelectedTask = newTask;
            });

            DeleteTaskCommand = new RelayCommand(p => {
                if (p is ConsultaTaskModel task)
                    PendingDeleteTask = task;
            });

            ConfirmDeleteCommand = new RelayCommand(_ => {
                if (PendingDeleteTask == null) return;
                var task = PendingDeleteTask;
                PendingDeleteTask = null;
                Tasks.Remove(task);
                if (SelectedTask == task) SelectedTask = Tasks.FirstOrDefault();
                MainViewModel.Instance.ShowNotification("Tarea eliminada.", "Success");
            });

            CancelDeleteCommand = new RelayCommand(_ => PendingDeleteTask = null);

            TestEmailCommand = new RelayCommand(
                _ => EnviarTestEmail(),
                _ => IsNotBusy
            );
        }

        private async void EnviarTestEmail()
        {
            if (SelectedTask == null) return;
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var config = _service.LoadConfig();
                if (config == null)
                {
                    MainViewModel.Instance.ShowNotification("No se pudo cargar la configuración SMTP.", "Error");
                    return;
                }

                string smtpPass = CryptoHelper.IsEncrypted(config.ClaveSMTP)
                    ? CryptoHelper.Decrypt(config.ClaveSMTP)
                    : config.ClaveSMTP;

                string asunto = SelectedTask.Mail?.AsuntoConError ?? "(sin asunto)";
                string cuerpo = (SelectedTask.Mail?.CuerpoConError ?? "") + "\n\nESTO ES UN TEST DE PRUEBA";

                var destinatarios = SelectedTask.Destinatarios;
                if (destinatarios == null || !destinatarios.Any())
                {
                    MainViewModel.Instance.ShowNotification("La tarea no tiene destinatarios configurados.", "Error");
                    return;
                }

                await Task.Run(() =>
                {
                    using (var smtp = new SmtpClient(config.ServidorSMTP, config.PuertoSMTP))
                    {
                        smtp.Timeout = 10000; // 10 segundos
                        smtp.EnableSsl = true;
                        smtp.Credentials = new NetworkCredential(config.UsuarioSMTP, smtpPass);

                        using (var msg = new MailMessage())
                        {
                            msg.From = new MailAddress(config.Remitente);
                            msg.Subject = $"[TEST] {asunto}";
                            msg.Body = cuerpo;
                            msg.IsBodyHtml = false;

                            foreach (var dest in destinatarios)
                            {
                                var trimmed = dest?.Trim();
                                if (!string.IsNullOrEmpty(trimmed))
                                    msg.To.Add(trimmed);
                            }

                            smtp.Send(msg);
                        }
                    }
                });

                MainViewModel.Instance.ShowNotification("Test de correo enviado correctamente.", "Success");
            }
            catch (Exception ex)
            {
                MainViewModel.Instance.ShowNotification($"Error al enviar test: {ex.Message}", "Error");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
