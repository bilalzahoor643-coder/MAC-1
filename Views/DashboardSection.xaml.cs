using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace MAC_1.Views
{
    public partial class DashboardSection : UserControl
    {
        public DashboardSection()
        {
            InitializeComponent();
            this.Loaded += DashboardSection_Loaded;
        }

        private void DashboardSection_Loaded(object sender, RoutedEventArgs e)
        {
            // Fade-in animation for the whole dashboard
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500));
            fadeIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            this.BeginAnimation(OpacityProperty, fadeIn);
        }
    }
}
