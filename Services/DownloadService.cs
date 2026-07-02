using System;
using System.Collections.ObjectModel;
using System.Linq;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class DownloadService
    {
        private static readonly DownloadService _instance = new DownloadService();
        public static DownloadService Instance => _instance;

        public ObservableCollection<DownloadTask> Downloads => DataService.Instance.Downloads;

        private DownloadService() { }

        public void AddDownload(string url, string fileName, long fileSize, string savePath)
        {
            var data = new DownloadData
            {
                Url = url,
                Filename = fileName,
                FileSize = fileSize
            };

            if (!string.IsNullOrWhiteSpace(savePath))
                data.SavePath = savePath;

            PopupService.Instance.ShowDownloadPopup(data);
        }

        public void PauseAll()
        {
            foreach (var task in Downloads.Where(d => d.State == DownloadState.Downloading))
            {
                var sessionId = task.Session?.SessionId ?? "";
                if (string.IsNullOrEmpty(sessionId)) continue;
                _ = SendServiceAction("pause-download", sessionId);
            }
        }

        public void ResumeAll()
        {
            foreach (var task in Downloads.Where(d => d.State == DownloadState.Paused))
            {
                var sessionId = task.Session?.SessionId ?? "";
                if (string.IsNullOrEmpty(sessionId)) continue;
                _ = SendServiceAction("resume-download", sessionId);
            }
        }

        private static async System.Threading.Tasks.Task SendServiceAction(string action, string sessionId)
        {
            try
            {
                var httpClient = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(5) };
                var json = System.Text.Json.JsonSerializer.Serialize(new { sessionId });
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                await httpClient.PostAsync($"http://127.0.0.1:57575/api/{action}", content);
            }
            catch { }
        }
    }
}
