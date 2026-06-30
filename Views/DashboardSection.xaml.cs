using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MAC_1.ViewModels;

namespace MAC_1.Views
{
    public partial class DashboardSection : UserControl
    {
        public DashboardSection()
        {
            InitializeComponent();
            DataContext = DashboardViewModel.Instance;
            this.Loaded += DashboardSection_Loaded;
        }

        private void DashboardSection_Loaded(object sender, RoutedEventArgs e)
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500));
            fadeIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            this.BeginAnimation(OpacityProperty, fadeIn);
        }
    }
}
