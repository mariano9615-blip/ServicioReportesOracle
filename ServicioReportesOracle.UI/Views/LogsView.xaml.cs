using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ServicioReportesOracle.UI.ViewModels;

namespace ServicioReportesOracle.UI.Views
{
    public partial class LogsView : UserControl
    {
        public LogsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Unloaded += OnUnloaded;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is LogsViewModel oldVm)
                oldVm.Lines.CollectionChanged -= OnLinesChanged;
            if (e.NewValue is LogsViewModel newVm)
                newVm.Lines.CollectionChanged += OnLinesChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is LogsViewModel vm)
            {
                vm.Lines.CollectionChanged -= OnLinesChanged;
                vm.Dispose();
            }
        }

        // ── Ctrl+F: abrir/cerrar buscador ────────────────────────────────────
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is LogsViewModel vm)
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
            if (e.Key == Key.Escape && DataContext is LogsViewModel vm)
            {
                vm.ClearSearchCommand.Execute(null);
                LogsListBox.Focus();
                e.Handled = true;
            }
        }

        private void OnLinesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Solo reaccionar a nuevos ítems (no a Clear/Reset durante carga inicial o filtrado)
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            if (LogsListBox.Items.Count == 0) return;

            try
            {
                var sv = GetScrollViewer(LogsListBox);
                if (sv == null)
                {
                    // Árbol visual todavía no disponible: scroll incondicional
                    LogsListBox.ScrollIntoView(LogsListBox.Items[LogsListBox.Items.Count - 1]);
                    return;
                }

                // Solo auto-scroll si el usuario ya estaba al final (margen de 2px)
                bool nearBottom = sv.VerticalOffset + sv.ViewportHeight >= sv.ExtentHeight - 2.0;
                if (nearBottom)
                    LogsListBox.ScrollIntoView(LogsListBox.Items[LogsListBox.Items.Count - 1]);
            }
            catch { /* VirtualizationMode=Recycling puede tirar durante actualizaciones rápidas */ }
        }

        private static ScrollViewer GetScrollViewer(DependencyObject parent)
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv) return sv;
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
