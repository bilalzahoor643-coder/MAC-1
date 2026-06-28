using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FontAwesome.WPF;

namespace MAC_1.Views
{
    public partial class QueueSection : UserControl
    {
        private int _selectedCount = 0;
        private readonly bool[] _checked = new bool[6];

        public QueueSection()
        {
            InitializeComponent();
            _checked[1] = true;
            _checked[2] = true;
            _selectedCount = 2;
            this.Loaded += QueueSection_Loaded;
        }

        private void QueueSection_Loaded(object sender, RoutedEventArgs e)
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(0.35));
            fadeIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void Cb_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border cb) return;
            if (cb.Tag is not string tagStr || !int.TryParse(tagStr, out int index)) return;

            _checked[index] = !_checked[index];

            Border? border = null;
            ImageAwesome? icon = null;

            switch (index)
            {
                case 1: border = Cb1; icon = CbIcon1; break;
                case 2: border = Cb2; icon = CbIcon2; break;
                case 3: border = Cb3; icon = CbIcon3; break;
                case 4: border = Cb4; icon = CbIcon4; break;
                case 5: border = Cb5; icon = CbIcon5; break;
            }

            if (border == null || icon == null) return;

            if (_checked[index])
            {
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                border.BorderBrush = Brushes.Transparent;
                border.BorderThickness = new Thickness(0);
                icon.Icon = FontAwesomeIcon.Check;
                icon.Foreground = new SolidColorBrush(Colors.White);
                icon.Width = 9;
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                _selectedCount++;
            }
            else
            {
                border.Background = new SolidColorBrush(Colors.White);
                border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB"));
                border.BorderThickness = new Thickness(1.5);
                icon.Width = 0;
                _selectedCount--;
            }

            UpdateBulkBar();
        }

        private void UpdateBulkBar()
        {
            if (_selectedCount > 0)
            {
                BulkActionBar.Visibility = Visibility.Visible;
                SelectedCountText.Text = $"{_selectedCount} selected";
            }
            else
            {
                BulkActionBar.Visibility = Visibility.Collapsed;
            }
        }

        private void BulkBarClose_Click(object sender, MouseButtonEventArgs e)
        {
            _selectedCount = 0;
            for (int i = 1; i <= 5; i++) _checked[i] = false;

            UncheckItem(Cb1, CbIcon1);
            UncheckItem(Cb2, CbIcon2);
            UncheckItem(Cb3, CbIcon3);
            UncheckItem(Cb4, CbIcon4);
            UncheckItem(Cb5, CbIcon5);

            BulkActionBar.Visibility = Visibility.Collapsed;
        }

        private void UncheckItem(Border cb, ImageAwesome icon)
        {
            cb.Background = new SolidColorBrush(Colors.White);
            cb.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB"));
            cb.BorderThickness = new Thickness(1.5);
            icon.Width = 0;
        }
    }
}
