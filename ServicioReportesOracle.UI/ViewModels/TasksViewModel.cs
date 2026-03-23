using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.IO;
using ServicioReportesOracle.UI.Models;
using ServicioReportesOracle.UI.Services;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class TasksViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<ConsultaTaskModel> _tasks;
        private ConsultaTaskModel _selectedTask;
        private ConsultaTaskModel _pendingDeleteTask;
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

        public ICommand SaveCommand { get; }
        public ICommand AddTaskCommand { get; }
        public ICommand DeleteTaskCommand { get; }
        public ICommand ConfirmDeleteCommand { get; }
        public ICommand CancelDeleteCommand { get; }

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
                MainViewModel.Instance.ShowNotification("Tareas guardadas correctamente.", "Success");
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
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
