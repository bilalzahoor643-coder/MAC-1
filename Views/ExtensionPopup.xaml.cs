using FontAwesome.WPF;
using MAC_1.Models;
using MAC_1.Services;
using Microsoft.Win32;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using Hc = HandyControl.Controls; // 'hc' ko 'Hc' kiya taake naming warning khatam ho
using System.Runtime.Versioning;

namespace MAC_1.Views
{
    public partial class ExtensionPopup : Window
    {
        private long _currentFileSize = 0;
        private DownloadData? _downloadData;

        public ExtensionPopup()
        {
            InitializeComponent();
        }

        public ExtensionPopup(string jsonData)
        {
            InitializeComponent();
            PopulateData(jsonData);
        }

        private void PopulateData(string jsonData)
        {
            if (string.IsNullOrWhiteSpace(jsonData)) return;

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                var data = JsonSerializer.Deserialize<DownloadData>(jsonData, options);

                if (data != null)
                {
                    _downloadData = data;
                    _currentFileSize = data.FileSize ?? 0;

                    // 1. URL Setting (Null safe)
                    UrlTextBox.Text = data.Url ?? string.Empty;

                    // 2. Filename Logic (Null safe + Redirect fix)
                    string finalName = data.Filename ?? string.Empty;

                    // Agar name "download" ya empty hai, toh URL se asli name nikalne ki koshish karein
                    if ((string.IsNullOrEmpty(finalName) || finalName.ToLower() == "download") && !string.IsNullOrEmpty(data.Url))
                    {
                        try
                        {
                            var uri = new Uri(data.Url);
                            finalName = Path.GetFileName(uri.LocalPath);
                        }
                        catch { finalName = string.Empty; }
                    }

                    // Agar abhi bhi empty hai, toh aik default name de dein
                    if (string.IsNullOrEmpty(finalName) || finalName.ToLower() == "download")
                    {
                        finalName = "MAC1_File_" + DateTime.Now.ToString("HHmmss");
                    }
                    FileNameText.Text = finalName;

                    // 3. Source/Website Text logic (Null safe)
                    if (!string.IsNullOrEmpty(data.Referrer))
                    {
                        try { WebsiteText.Text = "Source: " + new Uri(data.Referrer).Host; }
                        catch { WebsiteText.Text = "Source: Unknown"; }
                    }
                    else if (!string.IsNullOrEmpty(data.Url))
                    {
                        try { WebsiteText.Text = "Source: " + new Uri(data.Url).Host; }
                        catch { WebsiteText.Text = "Source: Unknown"; }
                    }

                    // 4. Icon aur Type set karna
                    string ext = Path.GetExtension(finalName).ToLower();
                    SetFileTypeAndIcon(ext, _currentFileSize);

                    // 5. Default Path Logic (C:\Users\Name\Downloads\MAC-1)
                    if (string.IsNullOrWhiteSpace(SavePathTextBox.Text))
                    {
                        string userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                        string defaultPath = Path.Combine(userDownloads, "MAC-1");
                        SavePathTextBox.Text = defaultPath;
                    }

                    // 6. Disk Space Check
                    CheckDiskSpace();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error processing data: " + ex.Message);
            }
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text;
            if (!string.IsNullOrEmpty(url))
            {
                Clipboard.SetText(url);
                Hc.MessageBox.Show("URL Copied to Clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                SavePathTextBox.Text = dialog.FolderName;
                CheckDiskSpace();
            }
        }

        private void CheckDiskSpace()
        {
            string path = SavePathTextBox.Text;
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                string? drive = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(drive)) return;

                var driveInfo = new DriveInfo(drive);
                if (driveInfo.AvailableFreeSpace < _currentFileSize)
                {
                    FileInfoText.Text = $"Size: {FormatSize(_currentFileSize)} | ⚠️ LOW DISK SPACE";
                    FileInfoText.Foreground = Brushes.Red;
                }
                else
                {
                    // Safe casting for Brush
                    var grayBrush = new BrushConverter().ConvertFrom("#60FFFFFF") as Brush;
                    FileInfoText.Foreground = grayBrush ?? Brushes.Gray;
                }
            }
            catch
            {
                // Ignore drive errors
            }
        }

        private void SetFileTypeAndIcon(string ext, long size)
        {
            FontAwesomeIcon icon = FontAwesomeIcon.FileOutline;
            string type = "File";

            switch (ext)
            {
                case ".zip":
                case ".rar":
                case ".7z":
                    type = "Compressed"; icon = FontAwesomeIcon.FileZipOutline; break;
                case ".exe":
                case ".msi":
                    type = "Application"; icon = FontAwesomeIcon.WindowMaximize; break;
                case ".mp4":
                case ".mkv":
                    type = "Video"; icon = FontAwesomeIcon.FileVideoOutline; break;
                case ".pdf":
                case ".docx":
                    type = "Document"; icon = FontAwesomeIcon.FileTextOutline; break;
            }

            FileIcon.Icon = icon;
            FileInfoText.Text = $"Size: {FormatSize(size)} | Type: {type}";
        }

        private string FormatSize(long bytes)
        {
            if (bytes <= 0) return "Unknown";
            string[] suf = { "B", "KB", "MB", "GB", "TB" };
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return num.ToString() + " " + suf[place];
        }

        private void DownloadNow_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadData != null)
            {
                DownloadService.Instance.AddDownload(
                    _downloadData.Url ?? string.Empty,
                    FileNameText.Text,
                    _downloadData.FileSize ?? 0,
                    SavePathTextBox.Text
                );
                this.Close();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}