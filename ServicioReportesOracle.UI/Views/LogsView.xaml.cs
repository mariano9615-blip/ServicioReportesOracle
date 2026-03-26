using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServicioReportesOracle.UI.ViewModels;

namespace ServicioReportesOracle.UI.Views
{
    public partial class LogsView : UserControl
    {
        public LogsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is LogsViewModel oldVm)
                oldVm.Lines.CollectionChanged -= OnLinesChanged;
            if (e.NewValue is LogsViewModel newVm)
                newVm.Lines.CollectionChanged += OnLinesChanged;
        }

        private void OnLinesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Solo reaccionar a nuevos ítems (no a Clear/Reset durante carga inicial)
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            if (LogsListBox.Items.Count == 0) return;

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
