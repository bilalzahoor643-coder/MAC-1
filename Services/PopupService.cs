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

                string filename = BestFilename(session);
                string category = GuessCategory(filename, session.MimeType);
                string saveFolder = DataService.Instance.GetSavePathForCategory(category);

                var task = new DownloadTask
                {
                    Url = session.Url,
                    Filename = filename,
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
                _activePopup.Closed += (_, _) => { _activePopup = null; PopupClosed?.Invoke(); };
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
                _activePopup.Closed += (_, _) => { _activePopup = null; PopupClosed?.Invoke(); };
                _activePopup.Show();
            });
        }

        private string BestFilename(DownloadSession s)
        {
            if (!string.IsNullOrEmpty(s.Filename) && s.Filename != "download" && s.Filename != "unknown")
                return s.Filename;

            if (!string.IsNullOrEmpty(s.SuggestedFilename))
                return s.SuggestedFilename;

            if (!string.IsNullOrEmpty(s.ContentDisposition))
                return s.ContentDisposition;

            return ExtractFilenameFromUrl(s.Url);
        }

        private string GuessCategory(string filename, string mimeType)
        {
            string ext = Path.GetExtension(filename).ToLowerInvariant().TrimStart('.');

            if (!string.IsNullOrEmpty(ext))
            {
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

            if (!string.IsNullOrEmpty(mimeType))
            {
                string m = mimeType.ToLowerInvariant();
                if (m.Contains("zip") || m.Contains("rar") || m.Contains("7z") || m.Contains("tar")) return "Compressed";
                if (m.Contains("pdf") || m.Contains("msword") || m.Contains("vnd.ms-excel") || m.Contains("vnd.ms-powerpoint")) return "Documents";
                if (m.Contains("audio/")) return "Music";
                if (m.Contains("video/")) return "Video";
                if (m.Contains("image/")) return "Images";
                if (m.Contains("octet-stream") || m.Contains("x-msdownload")) return "Software";
            }

            return "General";
        }

        private string ExtractFilenameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string name = Path.GetFileName(uri.AbsolutePath);
                return string.IsNullOrEmpty(name) ? "download" : Uri.UnescapeDataString(name);
            }
            catch { return "download"; }
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
