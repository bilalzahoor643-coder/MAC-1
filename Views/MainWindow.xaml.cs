using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FontAwesome.WPF;

namespace MAC_1.Views
{
    public partial class MainWindow : Window
    {
        private bool _isSidebarExpanded = true;
        private bool _isPaused = false;
        private Button? _activeNavButton;

        public MainWindow()
        {
            InitializeComponent();
            NavigateTo(NavDashboard);
        }

        // --- Sidebar ---
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

            double toWidth = _isSidebarExpanded ? 220 : 70;
            var widthAnim = new DoubleAnimation(toWidth, TimeSpan.FromMilliseconds(0.3))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            SidebarBorder.BeginAnimation(WidthProperty, widthAnim);

            double textOpacity = _isSidebarExpanded ? 1 : 0;
            var textAnim = new DoubleAnimation(textOpacity, TimeSpan.FromMilliseconds(0.25));
            LogoTextPanel.BeginAnimation(OpacityProperty, textAnim);
        }

        // --- Navigation ---
        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                NavigateTo(btn);
            }
        }

        private void NavigateTo(Button navButton)
        {
            _activeNavButton = navButton;
            string page = navButton.Content.ToString() ?? "";

            switch (page)
            {
                case "Dashboard":
                    MainContent.Content = new DashboardSection();
                    break;
                case "Downloading":
                    ShowEmptyPage("Downloading", "No Active Downloads", "Your downloads will appear here", FontAwesomeIcon.Download);
                    break;
                case "Queue":
                    ShowEmptyPage("Queue", "Queue is Empty", "Add downloads to the queue to start", FontAwesomeIcon.ListOl);
                    break;
                case "Torrent":
                    ShowEmptyPage("Torrent", "No Torrents", "Add a magnet link or torrent file to begin", FontAwesomeIcon.Magnet);
                    break;
                case "Finished":
                    ShowEmptyPage("Finished", "No Completed Downloads", "Finished downloads will show up here", FontAwesomeIcon.CheckCircle);
                    break;
                case "History":
                    ShowEmptyPage("History", "No History Yet", "Your download history will appear here", FontAwesomeIcon.History);
                    break;
                case "Settings":
                    ShowEmptyPage("Settings", "Settings", "Configure your MAC-1 Downloader", FontAwesomeIcon.Gear);
                    break;
                default:
                    MainContent.Content = new DashboardSection();
                    break;
            }
        }

        private void ShowEmptyPage(string title, string emptyTitle, string emptySubtitle, FontAwesomeIcon icon)
        {
            var empty = new EmptyStateSection();
            empty.SetPage(title, emptyTitle, emptySubtitle, icon);
            MainContent.Content = empty;
        }

        // --- Search Bar ---
        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var anim = new DoubleAnimation(300, 420, TimeSpan.FromMilliseconds(0.25))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            SearchBox.BeginAnimation(WidthProperty, anim);
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var anim = new DoubleAnimation(420, 300, TimeSpan.FromMilliseconds(0.25))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            SearchBox.BeginAnimation(WidthProperty, anim);
        }

        // --- Pause / Resume ---
        private void PauseResumeBtn_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;

            var pauseContent = PauseResumeBtn.Template.FindName("PauseContent", PauseResumeBtn) as System.Windows.Controls.StackPanel;
            if (pauseContent == null) return;

            var icon = pauseContent.Children[0] as ImageAwesome;
            var label = pauseContent.Children[1] as System.Windows.Controls.TextBlock;
            if (icon == null || label == null) return;

            if (_isPaused)
            {
                icon.Icon = FontAwesomeIcon.PlayCircle;
                icon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                label.Text = "RESUME";

                Services.DownloadService.Instance.PauseAll();
            }
            else
            {
                icon.Icon = FontAwesomeIcon.PauseCircle;
                icon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800"));
                label.Text = "PAUSE";

                Services.DownloadService.Instance.ResumeAll();
            }
        }
    }
}
