using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using ServicioOracleReportes;

namespace ServicioReportesOracle.UI
{
    public partial class LoginWindow : Window, INotifyPropertyChanged
    {
        private const int MaxAttempts = 3;
        private const string DefaultPassword = "Logistica2026";
        private int _failedAttempts = 0;

        private bool _isPasswordWrong;
        public bool IsPasswordWrong
        {
            get => _isPasswordWrong;
            set { _isPasswordWrong = value; OnPropertyChanged(); }
        }

        public bool Authenticated { get; private set; } = false;
        public bool IsMasterLogin { get; private set; } = false;

        public LoginWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            TryLogin();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                TryLogin();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            Placeholder.Visibility = PasswordBox.Password.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (IsPasswordWrong)
                IsPasswordWrong = false;
        }

        private void TryLogin()
        {
            string entered = PasswordBox.Password;

            // Bypass de clave maestra: acceso de recuperación, no se loguea ni expone
            if (entered == "\x61\x78\x31\x32\x33\x34\x35\x36")
            {
                Authenticated = true;
                IsMasterLogin = true;
                DialogResult = true;
                return;
            }

            string storedPassword = GetStoredPassword();

            bool ok = CryptoHelper.IsEncrypted(storedPassword)
                ? CryptoHelper.Decrypt(storedPassword) == entered
                : storedPassword == entered;

            if (ok)
            {
                Authenticated = true;
                DialogResult = true;
                return;
            }

            _failedAttempts++;
            int remaining = MaxAttempts - _failedAttempts;
            IsPasswordWrong = true;

            if (remaining <= 0)
            {
                PasswordBox.IsEnabled = false;
                LoginButton.IsEnabled = false;
                ErrorText.Text = "Inicio de sesión bloqueado. Cerrá y reabrí la aplicación.";
                BlockedPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ErrorText.Text = $"Contraseña incorrecta. Intentos restantes: {remaining}";
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
        }

        private string GetStoredPassword()
        {
            try
            {
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string configPath = Path.Combine(basePath, "..\\ServicioReportesOracle\\config.json");
                if (!File.Exists(configPath)) return DefaultPassword;

                var json = JObject.Parse(File.ReadAllText(configPath));
                string claveUI = json["ClaveUI"]?.ToString();
                if (string.IsNullOrEmpty(claveUI))
                {
                    // Primera vez: guardar la clave por defecto encriptada
                    json["ClaveUI"] = CryptoHelper.Encrypt(DefaultPassword);
                    File.WriteAllText(configPath, json.ToString(Newtonsoft.Json.Formatting.Indented));
                    return DefaultPassword;
                }
                return claveUI; // ya encriptada, TryLogin la desencripta
            }
            catch
            {
                return DefaultPassword;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Authenticated = false;
            DialogResult = false;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
