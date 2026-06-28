using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace MAC_1.Views
{
    public partial class AddUrlPopup : Window
    {
        public AddUrlPopup()
        {
            InitializeComponent();
        }

        // Feature: Drag window from anywhere
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => this.Close();

        private void Analyze_Click(object sender, RoutedEventArgs e)
        {
            InputPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
            FooterPanel.Visibility = Visibility.Visible;

            // Height Animate: 160 to 350 (Perfect fit for content)
            DoubleAnimation heightAnim = new DoubleAnimation(350, TimeSpan.FromSeconds(0.3));
            heightAnim.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            this.BeginAnimation(Window.HeightProperty, heightAnim);
        }
    }
}