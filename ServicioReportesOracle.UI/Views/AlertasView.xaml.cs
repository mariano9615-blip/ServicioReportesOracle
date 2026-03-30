using System;
using System.Windows.Controls;

namespace ServicioReportesOracle.UI.Views
{
    /// <summary>
    /// Interaction logic for AlertasView.xaml
    /// </summary>
    public partial class AlertasView : UserControl
    {
        public AlertasView()
        {
            InitializeComponent();
            this.Unloaded += (s, e) => (DataContext as IDisposable)?.Dispose();
        }
    }
}
