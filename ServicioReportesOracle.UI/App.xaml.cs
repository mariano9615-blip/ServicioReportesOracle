using System.Windows;

namespace ServicioReportesOracle.UI
{
    public partial class App : Application
    {
        private void App_Startup(object sender, StartupEventArgs e)
        {
            var login = new LoginWindow();
            login.ShowDialog();

            if (login.Authenticated)
            {
                var main = new MainWindow();
                main.Show();
            }
            else
            {
                Shutdown();
            }
        }
    }
}
