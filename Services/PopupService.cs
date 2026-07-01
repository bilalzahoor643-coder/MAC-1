using System;
using System.IO;
using System.Windows;
using MAC_1.Models;
using MAC_1.Views;

namespace MAC_1.Services
{
    public class PopupService
    {
        private static readonly Lazy<PopupService> _instance = new(() => new PopupService());
        public static PopupService Instance => _instance.Value;

        private DownloadPopup? _activePopup;

        public event Action<DownloadTask>? DownloadStarted;
        public event Action? PopupClosed;

        private PopupService() { }

        public void ShowDownloadPopup(DownloadSession session)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CloseActivePopup();

                string category = GuessCategoryFromExtension(session.FileExtension);
                string saveFolder = DataService.Instance.GetSavePathForCategory(category);

                var task = new DownloadTask
                {
                    Url = session.Url,
                    Filename = string.IsNullOrEmpty(session.Filename) ? ExtractFilename(session.Url) : session.Filename,
                    TotalSize = session.FileSize > 0 ? session.FileSize : 0,
                    Category = category,
                    ResumeSupported = session.ResumeSupported,
                    SaveFolder = saveFolder
                };

                task.CheckIfArchive();

                _activePopup = new DownloadPopup(task, session);
                _activePopup.DownloadStarted += OnDownloadStarted;
                _activePopup.Closed += (_, _) =>
                {
                    _activePopup = null;
                    PopupClosed?.Invoke();
                };
                _activePopup.Show();
            });
        }

        public void ShowDownloadPopup(DownloadData data)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CloseActivePopup();

                var task = new DownloadTask
                {
                    Url = data.Url,
                    Filename = data.Filename,
                    TotalSize = data.FileSize,
                    Category = "Compressed"
                };

                _activePopup = new DownloadPopup(task);
                _activePopup.DownloadStarted += OnDownloadStarted;
                _activePopup.Closed += (_, _) =>
                {
                    _activePopup = null;
                    PopupClosed?.Invoke();
                };
                _activePopup.Show();
            });
        }

        public void ShowDownloadPopup(DownloadTask task)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CloseActivePopup();

                _activePopup = new DownloadPopup(task);
                _activePopup.DownloadStarted += OnDownloadStarted;
                _activePopup.Closed += (_, _) =>
                {
                    _activePopup = null;
                    PopupClosed?.Invoke();
                };
                _activePopup.Show();
            });
        }

        private string GuessCategoryFromExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return "General";

            ext = ext.ToLowerInvariant().TrimStart('.');

            return ext switch
            {
                "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz" or "iso" or "bin" or "cue" or "img" => "Compressed",
                "exe" or "msi" or "dmg" or "apk" => "Software",
                "pdf" or "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx" or "txt" or "rtf" => "Documents",
                "mp3" or "flac" or "wav" or "aac" or "ogg" or "wma" => "Music",
                "mp4" or "mkv" or "avi" or "mov" or "wmv" or "flv" or "webm" or "m4v" => "Video",
                "jpg" or "jpeg" or "png" or "gif" or "bmp" or "svg" or "webp" or "ico" => "Images",
                _ => "General"
            };
        }

        private string ExtractFilename(string url)
        {
            try
            {
                var uri = new Uri(url);
                string path = uri.AbsolutePath;
                string name = Path.GetFileName(path);
                return string.IsNullOrEmpty(name) ? "download" : Uri.UnescapeDataString(name);
            }
            catch
            {
                return "download";
            }
        }

        private void OnDownloadStarted(DownloadTask task)
        {
            DataService.Instance.AddDownload(task);
            CardService.Instance.CreateCard(task);
            DownloadStarted?.Invoke(task);
        }

        public void CloseActivePopup()
        {
            _activePopup?.Close();
            _activePopup = null;
        }

        public bool HasActivePopup => _activePopup != null;
    }
}
