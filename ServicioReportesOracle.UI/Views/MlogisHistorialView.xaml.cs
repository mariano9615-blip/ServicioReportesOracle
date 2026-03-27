using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ServicioReportesOracle.UI.ViewModels;

namespace ServicioReportesOracle.UI.Views
{
    public partial class MlogisHistorialView : UserControl
    {
        public MlogisHistorialView()
        {
            InitializeComponent();
            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MlogisHistorialViewModel vm)
                vm.Dispose();
        }

        // ── Ctrl+F: abrir/cerrar buscador ────────────────────────────────────
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is MlogisHistorialViewModel vm)
                {
                    vm.IsSearchVisible = !vm.IsSearchVisible;
                    if (vm.IsSearchVisible)
                        Dispatcher.BeginInvoke(new System.Action(() => SearchBox.Focus()), DispatcherPriority.Input);
                }
                e.Handled = true;
            }
            base.OnPreviewKeyDown(e);
        }

        // ── Escape en el buscador: cerrar ─────────────────────────────────────
        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && DataContext is MlogisHistorialViewModel vm)
            {
                vm.ClearSearchCommand.Execute(null);
                Focus();
                e.Handled = true;
            }
        }
    }
}
