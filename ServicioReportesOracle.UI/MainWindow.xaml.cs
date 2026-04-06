using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ServicioReportesOracle.UI.Helpers;
using ServicioReportesOracle.UI.ViewModels;

namespace ServicioReportesOracle.UI
{
    public partial class MainWindow : Window
    {
        private bool _sidebarExpanded = true;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var uiSettings = new UiSettingsService().Load();
            double widthFactor = uiSettings.WindowWidthPercent / 100.0;
            this.Width = SystemParameters.PrimaryScreenWidth * widthFactor;
            this.Height = SystemParameters.PrimaryScreenHeight * 0.80;
            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = (SystemParameters.PrimaryScreenHeight - this.Height) / 2;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                    ToggleMaximize();
                else
                    DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.StopBackgroundTasks();
            }
        }

        private void Window_StateChanged(object sender, System.EventArgs e)
        {
            if (MaxRestoreIcon != null)
                MaxRestoreIcon.Text = WindowState == WindowState.Maximized ? "\u2750" : "\u25A1";
        }

        private static readonly Thickness _paddingExpanded  = new Thickness(15, 12, 15, 12);
        private static readonly Thickness _paddingCollapsed = new Thickness(4, 8, 4, 8);

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _sidebarExpanded = !_sidebarExpanded;

            var targetWidth = _sidebarExpanded ? 260.0 : 56.0;
            var anim = new GridLengthAnimation
            {
                From = SidebarColumn.Width,
                To = new GridLength(targetWidth),
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, anim);

            var vis     = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
            var padding = _sidebarExpanded ? _paddingExpanded : _paddingCollapsed;

            // Textos: mostrar/ocultar
            SidebarTitle.Visibility    = vis;
            SidebarSubtitle.Visibility = vis;
            NavText0.Visibility        = vis;
            NavText1.Visibility        = vis;
            NavText2.Visibility        = vis;
            NavText3.Visibility        = vis;
            NavText4.Visibility        = vis;
            NavText5.Visibility        = vis;
            NavText9.Visibility        = vis;
            NavText6.Visibility        = vis;
            NavText7.Visibility        = vis;
            NavText8.Visibility         = vis;
            NavTextAnalitica.Visibility = vis;
            NavTextSettings.Visibility  = vis;
            VersionText.Visibility     = vis;

            // Íconos: ajustar padding del RadioButton para centrarlos en 56px
            NavBtn0.Padding = padding;
            NavBtn1.Padding = padding;
            NavBtn2.Padding = padding;
            NavBtn3.Padding = padding;
            NavBtn4.Padding = padding;
            NavBtn5.Padding = padding;
            NavBtn9.Padding = padding;
            NavBtn6.Padding = padding;
            NavBtn7.Padding = padding;
            NavBtn8.Padding         = padding;
            NavBtnAnalitica.Padding = padding;
            NavBtnSettings.Padding  = padding;

            // Botón hamburguesa: centrar cuando colapsado, alinear a la derecha cuando expandido
            if (_sidebarExpanded)
            {
                HamburgerBtn.HorizontalAlignment = HorizontalAlignment.Right;
                HamburgerBtn.Margin = new Thickness(0, 0, 8, 0);
            }
            else
            {
                HamburgerBtn.HorizontalAlignment = HorizontalAlignment.Center;
                HamburgerBtn.Margin = new Thickness(0, 0, 0, 0);
            }
        }
        private void AnaliticaButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new AnaliticaWindow();
            win.Owner = this;
            win.Show();
        }
    }
}
