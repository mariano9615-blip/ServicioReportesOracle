using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using ServicioOracleReportes;

namespace ServicioReportesOracle.UI.ViewModels
{
    public class ChangePasswordViewModel : INotifyPropertyChanged
    {
        // Contraseñas en texto plano (expuestas para PasswordBoxBehavior en code-behind)
        private string _currentPassword = "";
        private string _newPassword = "";
        private string _confirmPassword = "";
        private bool _showMismatchError;
        private bool _showCurrentWrongError;

        public string CurrentPassword
        {
            get => _currentPassword;
            set { _currentPassword = value; OnPropertyChanged(); ClearErrors(); }
        }

        public string NewPassword
        {
            get => _newPassword;
            set { _newPassword = value; OnPropertyChanged(); ClearErrors(); }
        }

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set { _confirmPassword = value; OnPropertyChanged(); ClearErrors(); }
        }

        public bool ShowMismatchError
        {
            get => _showMismatchError;
            set { _showMismatchError = value; OnPropertyChanged(); }
        }

        public bool ShowCurrentWrongError
        {
            get => _showCurrentWrongError;
            set { _showCurrentWrongError = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public ChangePasswordViewModel()
        {
            SaveCommand = new RelayCommand(_ => Save());
            CancelCommand = new RelayCommand(_ => Cancel());
        }

        private void ClearErrors()
        {
            ShowMismatchError = false;
            ShowCurrentWrongError = false;
        }

        private void Save()
        {
            // Validar contraseña actual
            string storedEncrypted = ReadStoredClaveUI();
            string stored = CryptoHelper.IsEncrypted(storedEncrypted)
                ? CryptoHelper.Decrypt(storedEncrypted)
                : storedEncrypted;

            if (_currentPassword != stored)
            {
                ShowCurrentWrongError = true;
                return;
            }

            // Validar que las nuevas coincidan
            if (_newPassword != _confirmPassword || string.IsNullOrEmpty(_newPassword))
            {
                ShowMismatchError = true;
                return;
            }

            try
            {
                string configPath = GetConfigPath();
                var json = JObject.Parse(File.ReadAllText(configPath));
                json["ClaveUI"] = CryptoHelper.Encrypt(_newPassword);
                File.WriteAllText(configPath, json.ToString(Newtonsoft.Json.Formatting.Indented));

                // Limpiar campos
                CurrentPassword = "";
                NewPassword = "";
                ConfirmPassword = "";

                MainViewModel.Instance.ShowNotification("Contraseña cambiada correctamente.", "Success");
            }
            catch (Exception ex)
            {
                MainViewModel.Instance.ShowNotification($"Error al guardar: {ex.Message}", "Error");
            }
        }

        private void Cancel()
        {
            CurrentPassword = "";
            NewPassword = "";
            ConfirmPassword = "";
            ClearErrors();
            MainViewModel.Instance.ShowNotification("Cambio de contraseña cancelado.", "Success");
            // Volver a la vista de configuración general
            MainViewModel.Instance.SelectedViewModel = new GeneralConfigViewModel();
        }

        private string ReadStoredClaveUI()
        {
            try
            {
                string configPath = GetConfigPath();
                if (!File.Exists(configPath)) return "Logistica2026";
                var json = JObject.Parse(File.ReadAllText(configPath));
                string claveUI = json["ClaveUI"]?.ToString();
                if (string.IsNullOrEmpty(claveUI))
                {
                    // Primera vez: guardar default
                    json["ClaveUI"] = CryptoHelper.Encrypt("Logistica2026");
                    File.WriteAllText(configPath, json.ToString(Newtonsoft.Json.Formatting.Indented));
                    return "Logistica2026";
                }
                return claveUI;
            }
            catch
            {
                return "Logistica2026";
            }
        }

        private static string GetConfigPath()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(basePath, "..\\ServicioReportesOracle\\config.json");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
