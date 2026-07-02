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
        private string? _activePopupUrl;
        private string? _activePopupSessionId;
        private readonly Dictionary<string, long> _pendingSizeUpdates = new();

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
                    SaveFolder = saveFolder,
                    Session = session
                };

                task.CheckIfArchive();

                _activePopup = new DownloadPopup(task, session);
                _activePopupUrl = session.Url;
                _activePopupSessionId = session.SessionId;
                _activePopup.DownloadStarted += OnDownloadStarted;
                _activePopup.Closed += (_, _) =>
                {
                    _activePopup = null;
                    _activePopupUrl = null;
                    _activePopupSessionId = null;
                    PopupClosed?.Invoke();
                };
                _activePopup.Show();

                System.Diagnostics.Debug.WriteLine($"[MAC-1] Popup shown: filename={filename}, sessionId={session.SessionId}, url={session.Url}");

                // Apply any pending size updates that arrived before popup was created
                if (_pendingSizeUpdates.TryGetValue(session.Url, out long pendingSize) && pendingSize > 0)
                {
                    _activePopup.UpdateFileSizeFromExtension(pendingSize);
                    _pendingSizeUpdates.Remove(session.Url);
                }
                if (_pendingSizeUpdates.TryGetValue(session.FinalUrl, out long pendingSize2) && pendingSize2 > 0)
                {
                    _activePopup.UpdateFileSizeFromExtension(pendingSize2);
                    _pendingSizeUpdates.Remove(session.FinalUrl);
                }
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
            _activePopupUrl = null;
            _activePopupSessionId = null;
        }

        public void UpdateFileSize(string url, long fileSize)
        {
            if (fileSize <= 0) return;

            if (_activePopup != null && _activePopupUrl == url)
            {
                _activePopup.UpdateFileSizeFromExtension(fileSize);
            }
            else
            {
                // Store for later — popup may not be open yet
                _pendingSizeUpdates[url] = fileSize;
            }
        }

        public bool HasActivePopup => _activePopup != null;

        public void HandleEngineEvent(DownloadEngineEvent evt)
        {
            if (_activePopup == null)
            {
                System.Diagnostics.Debug.WriteLine($"[MAC-1] EngineEvent DROPPED: no active popup | type={evt.EventType} sessionId={evt.SessionId} url={evt.Url}");
                return;
            }

            // Primary match: use SessionId (stable across redirect resolution)
            bool matchesSessionId = !string.IsNullOrEmpty(_activePopupSessionId) &&
                                    _activePopupSessionId == evt.SessionId;

            // Fallback match: URL (for events without SessionId)
            bool matchesUrl = !string.IsNullOrEmpty(_activePopupUrl) &&
                              _activePopupUrl == evt.Url;

            if (!matchesSessionId && !matchesUrl)
            {
                System.Diagnostics.Debug.WriteLine($"[MAC-1] EngineEvent DROPPED: no match | activeSessionId={_activePopupSessionId}, evtSessionId={evt.SessionId}, activeUrl={_activePopupUrl}, evtUrl={evt.Url}");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[MAC-1] EngineEvent OK: type={evt.EventType} state={evt.State} progress={evt.Progress:F1}% sessionId={evt.SessionId}");
            _activePopup.Dispatcher.Invoke(() =>
            {
                _activePopup.UpdateFromEngineEvent(evt);
            });
        }
    }
}
