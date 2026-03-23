using System;
using System.Windows;

namespace ServicioReportesOracle.UI
{
    public partial class App : Application
    {
        private void App_Startup(object sender, StartupEventArgs e)
        {
            // ── Diagnóstico temporal: capturar excepciones no manejadas ──────────
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                MessageBox.Show($"Error crítico: {ex.ExceptionObject}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show($"Error en UI: {ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
            // ────────────────────────────────────────────────────────────────────

            // Evitar que WPF cierre la app cuando LoginWindow (único window abierto) se cierra
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var login = new LoginWindow();
            bool? result = login.ShowDialog();

            if (result == true)
            {
                ShutdownMode = ShutdownMode.OnLastWindowClose;
                var main = new MainWindow();
                Application.Current.MainWindow = main;
                main.Show();
            }
            else
            {
                Shutdown();
            }
        }
    }
}
