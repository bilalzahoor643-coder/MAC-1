using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MAC_1.Models;
using MAC_1.Services;
using MAC_1.ViewModels;

namespace MAC_1.Views
{
    public partial class DownloadPopup : Window
    {
        private readonly DownloadTask _task;
        private readonly DownloadViewModel _viewModel;
        private readonly DownloadSession? _session;
        private DispatcherTimer? _progressTimer;

        public event Action<DownloadTask>? DownloadStarted;

        private const string NA = "Not Available";

        public DownloadPopup(DownloadTask task, DownloadSession? session = null)
        {
            InitializeComponent();
            _task = task;
            _viewModel = new DownloadViewModel(task);
            _session = session;

            PopulateAllFields();
            LoadSavedCategories();

            task.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DownloadTask.State))
                    Dispatcher.Invoke(OnStateChanged);
            };
        }

        private string S(string? value, string fallback = "Not Available")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private string FormatSize(long bytes)
        {
            return bytes > 0 ? DownloadTask.FormatSize(bytes) : "Not Available";
        }

        private void PopulateAllFields()
        {
            string filename = S(_task.Filename, "Not Available");
            string url = S(_task.Url, "Not Available");
            string website = _session?.Website ?? "Not Available";
            string websiteTitle = _session?.WebsiteTitle ?? "";
            string mimeType = _session?.MimeType ?? "Not Available";
            string fileExt = _session?.FileExtension ?? "Not Available";
            long fileSize = _session?.FileSize ?? _task.TotalSize;
            string referrer = _session?.Referrer ?? "Not Available";
            string userAgent = _session?.UserAgent ?? "Not Available";
            string method = _session?.RequestMethod ?? "Not Available";
            string source = _session?.DownloadSource ?? "Not Available";
            bool resumeSupported = _session?.ResumeSupported ?? true;

            FileNameText.Text = filename;
            FileSizeText.Text = FormatSize(fileSize);
            UrlText.Text = url;
            SavePathText.Text = _task.SaveFolder;

            string descParts = "";
            if (mimeType != NA) descParts += $"MIME: {mimeType}";
            if (fileExt != NA) descParts += (descParts.Length > 0 ? " | " : "") + $"Type: .{fileExt}";
            if (website != NA) descParts += (descParts.Length > 0 ? " | " : "") + $"From: {website}";
            DescriptionBox.Text = descParts;

            PopulateInfoCard(fileSize, resumeSupported, fileExt, mimeType, website, websiteTitle, referrer, method, source, userAgent);
            PopulateDiskInfo();
        }

        private void PopulateInfoCard(long fileSize, bool resumeSupported, string fileExt, string mimeType, string website, string websiteTitle, string referrer, string method, string source, string userAgent)
        {
            InfoFileSize.Text = FormatSize(fileSize);

            int connections = DataService.Instance.Settings.DefaultConnections;
            InfoConnections.Text = connections.ToString();

            if (fileSize > 0)
            {
                long partSize = fileSize / connections;
                InfoParts.Text = connections.ToString();
                InfoStartPosition.Text = "0 Bytes";
            }
            else
            {
                InfoParts.Text = NA;
                InfoStartPosition.Text = NA;
            }

            InfoResumeSupport.Text = resumeSupported ? "Yes" : "No";
            InfoResumeSupport.Foreground = resumeSupported
                ? (Brush)FindResource("Success")
                : (Brush)FindResource("TextMuted");

            InfoEstTime.Text = NA;
        }

        private void PopulateDiskInfo()
        {
            try
            {
                string savePath = _task.SaveFolder;
                if (!string.IsNullOrEmpty(savePath) && Directory.Exists(savePath))
                {
                    var driveInfo = new DriveInfo(savePath);
                    InfoDiskSpace.Text = DownloadTask.FormatSize(driveInfo.TotalSize);
                    InfoFreeSpace.Text = DownloadTask.FormatSize(driveInfo.AvailableFreeSpace);
                }
                else
                {
                    InfoDiskSpace.Text = "Not Available";
                    InfoFreeSpace.Text = "Not Available";
                }
            }
            catch
            {
                InfoDiskSpace.Text = "Not Available";
                InfoFreeSpace.Text = "Not Available";
            }
        }

        private void LoadSavedCategories()
        {
            string categoriesFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MAC-1", "categories.txt");

            if (!File.Exists(categoriesFile)) return;

            try
            {
                foreach (var name in File.ReadAllLines(categoriesFile))
                {
                    var trimmed = name.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        bool exists = false;
                        foreach (var item in CategoryCombo.Items)
                        {
                            if (((System.Windows.Controls.ComboBoxItem)item).Content.ToString() == trimmed)
                            { exists = true; break; }
                        }
                        if (!exists)
                            CategoryCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = trimmed });
                    }
                }
            }
            catch { }

            for (int i = 0; i < CategoryCombo.Items.Count; i++)
            {
                if (((System.Windows.Controls.ComboBoxItem)CategoryCombo.Items[i]).Content.ToString() == _task.Category)
                {
                    CategoryCombo.SelectedIndex = i;
                    break;
                }
            }
            if (CategoryCombo.SelectedIndex < 0 && CategoryCombo.Items.Count > 0)
                CategoryCombo.SelectedIndex = 0;
        }

        private void ShowState1()
        {
            State1Panel.Visibility = Visibility.Visible;
            State2Panel.Visibility = Visibility.Collapsed;
            State3Panel.Visibility = Visibility.Collapsed;
            TitleText.Text = "Download File Info";
            this.Height = 440;
        }

        private void ShowState2()
        {
            State1Panel.Visibility = Visibility.Collapsed;
            State2Panel.Visibility = Visibility.Visible;
            State3Panel.Visibility = Visibility.Collapsed;
            TitleText.Text = "Downloading";
            AnimateHeight(520);
            AnimateFadeIn(State2Panel);
            StartProgressTracking();
        }

        private void ShowState3()
        {
            State1Panel.Visibility = Visibility.Collapsed;
            State2Panel.Visibility = Visibility.Collapsed;
            State3Panel.Visibility = Visibility.Visible;
            TitleText.Text = "Download Complete";
            AnimateHeight(480);
            AnimateFadeIn(State3Panel);

            CompletedFileName.Text = _task.Filename;
            CompletedFullSizeText.Text = DownloadTask.FormatSize(_task.TotalSize);
            CompletedTimeText.Text = (_task.CompletedTime - _task.StartTime).ToString(@"hh\:mm\:ss");
            CompletedAvgSpeed.Text = _task.Speed;
        }

        private void OnStateChanged()
        {
            Dispatcher.Invoke(() =>
            {
                switch (_task.State)
                {
                    case DownloadState.Downloading: ShowState2(); break;
                    case DownloadState.Completed:
                        _progressTimer?.Stop();
                        DownloadProgressBar.Value = 100;
                        PercentText.Text = "100%";
                        PercentText2.Text = "100%";
                        ShowState3();
                        break;
                    case DownloadState.Paused:
                        _progressTimer?.Stop();
                        PauseResumeBtn.Content = "RESUME";
                        break;
                    case DownloadState.Failed:
                        _progressTimer?.Stop();
                        string errMsg = string.IsNullOrWhiteSpace(_task.ErrorMessage) ? "An unknown error occurred" : _task.ErrorMessage;
                        System.Diagnostics.Debug.WriteLine($"[MAC-1] Download FAILED: {errMsg}");
                        MessageBox.Show($"Download failed: {errMsg}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error);
                        this.Close();
                        break;
                }
            });
        }

        private void StartProgressTracking()
        {
            _progressTimer = new DispatcherTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(300);
            _progressTimer.Tick += (_, _) =>
            {
                DownloadProgressBar.Value = _task.Progress;
                PercentText.Text = $"{_task.Progress:F1}%";
                PercentText2.Text = $"{_task.Progress:F1}%";
                SpeedText.Text = _task.Speed;
                SpeedStat.Text = _task.Speed;
                TimeLeftText.Text = _task.TimeRemaining;
                TimeLeftStat.Text = _task.TimeRemaining;
                SizeText.Text = DownloadTask.FormatSize(_task.DownloadedSize);
                SizeDetailRun.Text = DownloadTask.FormatSize(_task.TotalSize);
                DownloadedRun.Text = DownloadTask.FormatSize(_task.DownloadedSize);
                RemainingStat.Text = DownloadTask.FormatSize(_task.TotalSize - _task.DownloadedSize);

                int completedParts = (int)(_task.Progress / 100 * 32);
                PartsText.Text = $"{completedParts} / 32";
                ChunkStatus.Text = $"{Math.Min(8, completedParts / 4)} / 8 completed";
            };
            _progressTimer.Start();
        }

        private async void StartDownload_Click(object sender, RoutedEventArgs e)
        {
            if (CategoryCombo.SelectedItem is System.Windows.Controls.ComboBoxItem selected)
                _task.Category = selected.Content.ToString() ?? "General";

            _task.SavePath = Path.Combine(
                DataService.Instance.GetSavePathForCategory(_task.Category),
                _task.Filename);

            DownloadStarted?.Invoke(_task);
            ShowState2();

            if (_session != null)
            {
                try
                {
                    var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    var request = new { sessionId = _session.SessionId ?? "", savePath = _task.SavePath };
                    var json = System.Text.Json.JsonSerializer.Serialize(request);
                    var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                    System.Diagnostics.Debug.WriteLine($"[MAC-1] Sending start-download: sessionId={request.sessionId}, url={_session.Url}");
                    var response = await httpClient.PostAsync("http://127.0.0.1:57575/api/start-download", content);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MAC-1] Start download FAILED: {response.StatusCode} — {responseBody}");
                        _task.ErrorMessage = $"Service returned {response.StatusCode}: {responseBody}";
                        _task.State = DownloadState.Failed;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[MAC-1] Start download OK: {responseBody}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MAC-1] Start download request FAILED: {ex.Message}");
                    _task.ErrorMessage = $"Connection failed: {ex.Message}";
                    _task.State = DownloadState.Failed;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MAC-1] Start download: _session is null, cannot start engine");
                _task.ErrorMessage = "No session data — cannot start download engine";
                _task.State = DownloadState.Failed;
            }
        }

        private void DownloadLater_Click(object sender, RoutedEventArgs e)
        {
            _task.State = DownloadState.Queued;
            DataService.Instance.AddDownload(_task);
            MessageBox.Show("Download added to queue!", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        private async void PauseResume_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;

            try
            {
                var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var request = new { sessionId = _session.SessionId ?? "" };
                var json = System.Text.Json.JsonSerializer.Serialize(request);
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                if (_task.State == DownloadState.Downloading)
                {
                    await httpClient.PostAsync("http://127.0.0.1:57575/api/pause-download", content);
                    PauseResumeBtn.Content = "RESUME";
                    _task.State = DownloadState.Paused;
                }
                else if (_task.State == DownloadState.Paused)
                {
                    await httpClient.PostAsync("http://127.0.0.1:57575/api/resume-download", content);
                    PauseResumeBtn.Content = "PAUSE";
                    _task.State = DownloadState.Downloading;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pause/Resume failed: {ex.Message}");
            }
        }

        private async void Stop_Click(object sender, RoutedEventArgs e)
        {
            _progressTimer?.Stop();
            if (MessageBox.Show("Cancel this download?", "MAC-1", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                if (_session != null)
                {
                    try
                    {
                        var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                        var request = new { sessionId = _session.SessionId ?? "" };
                        var json = System.Text.Json.JsonSerializer.Serialize(request);
                        var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                        await httpClient.PostAsync("http://127.0.0.1:57575/api/cancel-download", content);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cancel failed: {ex.Message}");
                    }
                }
                this.Close();
            }
            else _progressTimer?.Start();
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

        private void AnimateFadeIn(UIElement element)
        {
            element.Opacity = 0;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(300));
            anim.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            element.BeginAnimation(OpacityProperty, anim);
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_task.SavePath))
            {
                try { Process.Start(new ProcessStartInfo(_task.SavePath) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"Could not open file: {ex.Message}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            else MessageBox.Show($"File not found:\n{_task.SavePath}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = Path.GetDirectoryName(_task.SavePath) ?? "";
            if (Directory.Exists(folderPath))
            {
                try { Process.Start("explorer.exe", folderPath); }
                catch (Exception ex) { MessageBox.Show($"Could not open folder: {ex.Message}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void OpenWith_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_task.SavePath))
            {
                try { Process.Start(new ProcessStartInfo { FileName = _task.SavePath, Verb = "openas", UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"Could not open dialog: {ex.Message}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void NewCategory_Click(object sender, RoutedEventArgs e)
        {
            var popup = new NewCategoryPopup();
            if (popup.ShowDialog() == true)
            {
                var name = popup.CategoryName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    CategoryCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = name });
                    CategoryCombo.SelectedItem = CategoryCombo.Items[CategoryCombo.Items.Count - 1];
                }
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        private void CloseBtn_Click(object sender, RoutedEventArgs e) { _progressTimer?.Stop(); this.Close(); }

        public void UpdateFileSizeFromExtension(long fileSize)
        {
            if (fileSize <= 0) return;
            _task.TotalSize = fileSize;
            if (_session != null) _session.FileSize = fileSize;
            FileSizeText.Text = FormatSize(fileSize);
            InfoFileSize.Text = FormatSize(fileSize);
        }

        public void UpdateFromEngineEvent(DownloadEngineEvent evt)
        {
            switch (evt.EventType)
            {
                case DownloadEventType.Started:
                    System.Diagnostics.Debug.WriteLine($"[MAC-1] Engine Started: state={evt.State}");
                    _task.State = DownloadState.Downloading;
                    break;

                case DownloadEventType.StateChanged:
                    System.Diagnostics.Debug.WriteLine($"[MAC-1] Engine StateChanged: state={evt.State}");
                    if (evt.State == DownloadState2.Downloading)
                    {
                        _task.State = DownloadState.Downloading;
                    }
                    else if (evt.State == DownloadState2.Paused)
                    {
                        _task.State = DownloadState.Paused;
                    }
                    else if (evt.State == DownloadState2.Failed)
                    {
                        _task.ErrorMessage = evt.ErrorMessage ?? "Unknown error";
                        _task.State = DownloadState.Failed;
                    }
                    break;

                case DownloadEventType.MetadataReceived:
                    if (evt.FileSize > 0)
                    {
                        _task.TotalSize = evt.FileSize;
                        if (_session != null) _session.FileSize = evt.FileSize;
                        FileSizeText.Text = FormatSize(evt.FileSize);
                        InfoFileSize.Text = FormatSize(evt.FileSize);
                        InfoResumeSupport.Text = evt.ResumeSupported ? "Yes" : "No";
                    }
                    break;

                case DownloadEventType.ProgressChanged:
                    _task.DownloadedSize = evt.BytesDownloaded;
                    _task.TotalSize = evt.FileSize;
                    _task.Progress = evt.Progress;
                    _task.Speed = DownloadEngine.FormatSpeed(evt.Speed);
                    _task.TimeRemaining = DownloadEngine.FormatTime(evt.ETA);
                    break;

                case DownloadEventType.Completed:
                    _task.DownloadedSize = evt.BytesDownloaded;
                    _task.TotalSize = evt.FileSize;
                    _task.Progress = 100;
                    _task.Speed = "0 B/s";
                    _task.State = DownloadState.Completed;
                    _task.CompletedTime = DateTime.Now;
                    break;

                case DownloadEventType.Failed:
                    _task.ErrorMessage = evt.ErrorMessage ?? "Unknown error";
                    _task.State = DownloadState.Failed;
                    break;

                case DownloadEventType.Cancelled:
                    _task.State = DownloadState.Idle;
                    _progressTimer?.Stop();
                    this.Close();
                    break;
            }
        }
    }
}
