using System.Windows;
using System.Windows.Input;

namespace ServicioReportesOracle.UI
{
    public partial class LoginWindow : Window
    {
        private const string CorrectPassword = "Logistica2026";

        public bool Authenticated { get; private set; } = false;

        public LoginWindow()
        {
            InitializeComponent();
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

            // Limpiar error al tipear
            if (ErrorText.Visibility == Visibility.Visible)
            {
                ErrorText.Visibility = Visibility.Collapsed;
                ErrorText.Text = "";
            }
        }

        private void TryLogin()
        {
            if (PasswordBox.Password == CorrectPassword)
            {
                Authenticated = true;
                Close();
            }
            else
            {
                ErrorText.Text = "Contraseña incorrecta. Intentá de nuevo.";
                ErrorText.Visibility = Visibility.Visible;
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Authenticated = false;
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
