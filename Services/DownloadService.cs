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
                DownloadEngine.Instance.PauseDownload(task);
            }
        }

        public void ResumeAll()
        {
            foreach (var task in Downloads.Where(d => d.State == DownloadState.Paused))
            {
                DownloadEngine.Instance.ResumeDownload(task);
            }
        }
    }
}
