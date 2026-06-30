using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MAC_1.Views
{
    public partial class DownloadPopup : Window
    {
        private bool _isPaused = false;
        private DispatcherTimer? _mockTimer;
        private double _mockProgress = 0;
        private static readonly List<string> _customCategories = new();
        private static readonly Random _rng = new();

        private static readonly string _categoriesFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MAC-1", "categories.txt");

        public DownloadPopup()
        {
            InitializeComponent();
            PopulateMockChunks();
            LoadSavedCategories();
        }

        // CATEGORY PERSISTENCE
        private void LoadSavedCategories()
        {
            try
            {
                if (!File.Exists(_categoriesFilePath)) return;
                var saved = File.ReadAllLines(_categoriesFilePath);
                foreach (var name in saved)
                {
                    var trimmed = name.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    if (!_customCategories.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                        _customCategories.Add(trimmed);
                    if (CategoryCombo != null)
                        CategoryCombo.Items.Add(new ComboBoxItem { Content = trimmed });
                }
            }
            catch { /* non-fatal: persistence is best-effort */ }
        }

        private void SaveCategoriesToDisk()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_categoriesFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(_categoriesFilePath, _customCategories);
            }
            catch { /* non-fatal: persistence is best-effort */ }
        }

        // STATE MANAGEMENT
        private void ShowState1()
        {
            State1Panel.Visibility = Visibility.Visible;
            State2Panel.Visibility = Visibility.Collapsed;
            State3Panel.Visibility = Visibility.Collapsed;
            TitleText.Text = "Download File Info";
            this.Height = 460;
        }

        private void ShowState2()
        {
            State1Panel.Visibility = Visibility.Collapsed;
            State2Panel.Visibility = Visibility.Visible;
            State3Panel.Visibility = Visibility.Collapsed;
            TitleText.Text = "Downloading";
            AnimateHeight(520);
            AnimateFadeIn(State2Panel);
            StartMockDownload();
        }

        private void ShowState3()
        {
            State1Panel.Visibility = Visibility.Collapsed;
            State2Panel.Visibility = Visibility.Collapsed;
            State3Panel.Visibility = Visibility.Visible;
            TitleText.Text = "Download Complete";
            AnimateHeight(480);
            AnimateFadeIn(State3Panel);
        }

        private void AnimateFadeIn(UIElement element)
        {
            element.Opacity = 0;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(300));
            anim.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            element.BeginAnimation(OpacityProperty, anim);
        }

        // STATE 1 EVENTS
        private void StartDownload_Click(object sender, RoutedEventArgs e) { ShowState2(); }
        private void DownloadLater_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Download added to queue!", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        // STATE 2 EVENTS
        private void PauseResume_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            if (_isPaused) { PauseResumeBtn.Content = "RESUME"; _mockTimer?.Stop(); }
            else { PauseResumeBtn.Content = "PAUSE"; _mockTimer?.Start(); }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _mockTimer?.Stop();
            var result = MessageBox.Show("Are you sure you want to cancel this download?", "MAC-1", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) { this.Close(); }
            else { _mockTimer?.Start(); }
        }

        private bool _detailsVisible = true;
        private void HideDetails_Click(object sender, RoutedEventArgs e)
        {
            _detailsVisible = !_detailsVisible;
            if (_detailsVisible)
            {
                ConnectionSegmentsSection.Visibility = Visibility.Visible;
                ChunkDetailsSection.Visibility = Visibility.Visible;
                HideDetailsBtn.Content = "Hide Details";
                AnimateHeight(520);
            }
            else
            {
                ConnectionSegmentsSection.Visibility = Visibility.Collapsed;
                ChunkDetailsSection.Visibility = Visibility.Collapsed;
                HideDetailsBtn.Content = "Show Details";
                AnimateHeight(340);
            }
        }

        private void AnimateHeight(double targetHeight)
        {
            var anim = new System.Windows.Media.Animation.DoubleAnimation(targetHeight, TimeSpan.FromMilliseconds(200));
            anim.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
            this.BeginAnimation(HeightProperty, anim);
        }

        // STATE 3 EVENTS
        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            string filePath = GetDownloadPath();
            if (File.Exists(filePath))
            {
                try { Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"Could not open file: {ex.Message}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            else
            {
                MessageBox.Show("File path: " + filePath + "\n(File not found in demo mode)", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = GetDownloadFolder();
            if (Directory.Exists(folderPath))
            {
                try { Process.Start("explorer.exe", folderPath); }
                catch (Exception ex) { MessageBox.Show($"Could not open folder: {ex.Message}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            else
            {
                MessageBox.Show("Folder path: " + folderPath + "\n(Folder not found in demo mode)", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OpenWith_Click(object sender, RoutedEventArgs e)
        {
            string filePath = GetDownloadPath();
            if (File.Exists(filePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        Verb = "openas",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex) { MessageBox.Show($"Could not open dialog: {ex.Message}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            else
            {
                MessageBox.Show("In demo mode, the file does not exist yet.\nOpen With dialog will appear for real downloads.", "MAC-1 - Open With", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopyLink_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Link copied to clipboard!", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void NewCategory_Click(object sender, RoutedEventArgs e)
        {
            var popup = new NewCategoryPopup();
            if (popup.ShowDialog() == true)
            {
                var name = popup.CategoryName;
                if (!string.IsNullOrWhiteSpace(name) && !_customCategories.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    _customCategories.Add(name);
                    CategoryCombo.Items.Add(new ComboBoxItem { Content = name });
                    CategoryCombo.SelectedItem = CategoryCombo.Items[CategoryCombo.Items.Count - 1];
                    SaveCategoriesToDisk();
                }
            }
        }

        private Border CreateCard(SolidColorBrush bg, SolidColorBrush border)
        {
            return new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(12),
                BorderBrush = border,
                BorderThickness = new System.Windows.Thickness(1),
                Margin = new Thickness(24, 0, 24, 0)
            };
        }

        private Border CreateCompactCard(SolidColorBrush bg, SolidColorBrush border)
        {
            return new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(10),
                BorderBrush = border,
                BorderThickness = new System.Windows.Thickness(1),
                Margin = new Thickness(22, 0, 22, 0)
            };
        }

        private ControlTemplate CreateCloseButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background"));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.WidthProperty, 32.0);
            border.SetValue(Border.HeightProperty, 32.0);
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(contentPresenter);
            template.VisualTree = border;
            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(249, 250, 251)), "bd"));
            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(17, 24, 39))));
            template.Triggers.Add(trigger);
            return template;
        }

        private ControlTemplate CreateSecondaryBtnTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background"));
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush"));
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness"));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.HeightProperty, 36.0);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            template.VisualTree = border;
            var t1 = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            t1.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(249, 250, 251)), "bd"));
            t1.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(176, 181, 191)), "bd"));
            template.Triggers.Add(t1);
            return template;
        }

        private ControlTemplate CreatePrimaryBtnTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background"));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.HeightProperty, 36.0);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            template.VisualTree = border;
            var t1 = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            t1.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(67, 56, 202)), "bd"));
            template.Triggers.Add(t1);
            var t2 = new Trigger { Property = Button.IsPressedProperty, Value = true };
            t2.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(55, 48, 163)), "bd"));
            template.Triggers.Add(t2);
            var t3 = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            t3.Setters.Add(new Setter(Button.CursorProperty, Cursors.Arrow));
            template.Triggers.Add(t3);
            return template;
        }

        private ControlTemplate CreateBrowseButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background"));
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush"));
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness"));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.HeightProperty, 36.0);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            template.VisualTree = border;
            var t1 = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            t1.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(249, 250, 251)), "bd"));
            t1.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(176, 181, 191)), "bd"));
            template.Triggers.Add(t1);
            return template;
        }

        // WINDOW EVENTS
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        private void CloseBtn_Click(object sender, RoutedEventArgs e) { _mockTimer?.Stop(); this.Close(); }

        // HELPER METHODS
        private string GetDownloadPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Compressed", "Factory.Outlet.Simulator.rar");
        }

        private string GetDownloadFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Compressed");
        }

        // MOCK CHUNK DATA
        private void PopulateMockChunks()
        {
            var chunks = new List<ChunkInfo>
            {
                new ChunkInfo { ChunkNum = "1", Range = "24.26 MB", Info = "Receiving data...", ProgressPercent = 96, StartPos = "31.41 MB", Size = "25.00 MB", Speed = "5.2 MB/s" },
                new ChunkInfo { ChunkNum = "2", Range = "240.05 MB", Info = "Receiving data...", ProgressPercent = 85, StartPos = "56.41 MB", Size = "25.00 MB", Speed = "4.8 MB/s" },
                new ChunkInfo { ChunkNum = "3", Range = "17.70 MB", Info = "Receiving data...", ProgressPercent = 72, StartPos = "81.41 MB", Size = "25.00 MB", Speed = "3.9 MB/s" },
                new ChunkInfo { ChunkNum = "4", Range = "21.88 MB", Info = "Receiving data...", ProgressPercent = 58, StartPos = "106.41 MB", Size = "25.00 MB", Speed = "4.1 MB/s" },
                new ChunkInfo { ChunkNum = "5", Range = "19.17 MB", Info = "Receiving data...", ProgressPercent = 41, StartPos = "131.41 MB", Size = "25.00 MB", Speed = "3.5 MB/s" },
                new ChunkInfo { ChunkNum = "6", Range = "359.90 MB", Info = "Receiving data...", ProgressPercent = 25, StartPos = "156.41 MB", Size = "25.00 MB", Speed = "2.8 MB/s" },
                new ChunkInfo { ChunkNum = "7", Range = "128.40 MB", Info = "Queued", ProgressPercent = 0, StartPos = "181.41 MB", Size = "25.00 MB", Speed = "—" },
                new ChunkInfo { ChunkNum = "8", Range = "86.14 MB", Info = "Queued", ProgressPercent = 0, StartPos = "206.41 MB", Size = "25.00 MB", Speed = "—" },
            };
            ChunkItemsControl.ItemsSource = chunks;
        }

        // MOCK DOWNLOAD SIMULATION
        private void StartMockDownload()
        {
            _mockProgress = 0;
            _mockTimer = new DispatcherTimer();
            _mockTimer.Interval = TimeSpan.FromMilliseconds(150);
            _mockTimer.Tick += MockTimer_Tick;
            _mockTimer.Start();
        }

        private void MockTimer_Tick(object? sender, EventArgs e)
        {
            _mockProgress += _rng.NextDouble() * 1.5;

            if (_mockProgress >= 100)
            {
                _mockProgress = 100;
                _mockTimer?.Stop();
                DownloadProgressBar.Value = 100;
                PercentText.Text = "100%";
                PercentText2.Text = "100%";

                System.Threading.Thread.Sleep(600);
                ShowState3();
                return;
            }

            DownloadProgressBar.Value = _mockProgress;
            PercentText.Text = $"{_mockProgress:F1}%";
            PercentText2.Text = $"{_mockProgress:F1}%";

            double speed = 3 + _rng.NextDouble() * 4;
            SpeedText.Text = $"{speed:F1} MB/s";
            SpeedStat.Text = $"{speed:F1} MB/s";

            double avg = speed * 0.9;
            AvgSpeedStat.Text = $"{avg:F1} MB/s";

            double total = 4.53;
            double downloaded = total * _mockProgress / 100;
            double remaining = total - downloaded;

            SizeText.Text = $"{downloaded:F2} GB";
            SizeDetailRun.Text = $"{total:F2} GB";
            DownloadedRun.Text = $"{downloaded:F2} GB";
            RemainingStat.Text = $"{remaining:F2} GB";

            int completedParts = (int)(_mockProgress / 100 * 32);
            PartsText.Text = $"{completedParts} / 32";

            double remainingTime = remaining / speed;
            int mins = (int)(remainingTime * 60);
            string eta = $"{mins / 60:D2}:{mins % 60:D2}";
            TimeLeftText.Text = eta;
            TimeLeftStat.Text = eta;

            ChunkStatus.Text = $"{Math.Min(8, completedParts / 4)} / 8 completed";
        }
    }

    public class ChunkInfo
    {
        public string ChunkNum { get; set; } = "";
        public string Range { get; set; } = "";
        public string Info { get; set; } = "";
        public double ProgressPercent { get; set; }
        public string StartPos { get; set; } = "";
        public string Size { get; set; } = "";
        public string Speed { get; set; } = "";
    }
}
