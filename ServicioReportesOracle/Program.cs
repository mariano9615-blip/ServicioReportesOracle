using System.ServiceProcess;

namespace ServicioOracleReportes
{
    static class Program
    {
        static void Main()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new ServicioOracleReportes()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}
