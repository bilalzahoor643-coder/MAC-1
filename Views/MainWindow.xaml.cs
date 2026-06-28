using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MAC_1.Views
{
    public partial class MainWindow : Window
    {
        private bool _isSidebarExpanded = true;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void LogoArea_MouseEnter(object sender, MouseEventArgs e)
        {
            var anim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(0.2));
            CollapseOverlay.BeginAnimation(OpacityProperty, anim);
        }

        private void LogoArea_MouseLeave(object sender, MouseEventArgs e)
        {
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(0.3));
            CollapseOverlay.BeginAnimation(OpacityProperty, anim);
        }

        private void CollapseBtn_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarExpanded = !_isSidebarExpanded;

            // Animate sidebar width
            double toWidth = _isSidebarExpanded ? 220 : 70;
            var widthAnim = new DoubleAnimation(toWidth, TimeSpan.FromMilliseconds(0.3))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            SidebarBorder.BeginAnimation(WidthProperty, widthAnim);

            // Animate logo text
            double textOpacity = _isSidebarExpanded ? 1 : 0;
            var textAnim = new DoubleAnimation(textOpacity, TimeSpan.FromMilliseconds(0.25));
            LogoTextPanel.BeginAnimation(OpacityProperty, textAnim);
        }
    }
}
