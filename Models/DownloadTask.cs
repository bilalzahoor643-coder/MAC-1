using System;
using System.IO;
using MAC_1.ViewModels;

namespace MAC_1.Models
{
    public class DownloadTask : ViewModelBase
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Url { get; set; } = string.Empty;
        public string SavePath { get; set; } = string.Empty;

        // XAML Binding: Task.Filename
        private string _filename = string.Empty;
        public string Filename
        {
            get => _filename;
            set
            {
                _filename = value;
                OnPropertyChanged(nameof(Filename));
                CheckIfArchive();
            }
        }

        // XAML Binding: Task.TotalSize
        private long _totalSize;
        public long TotalSize
        {
            get => _totalSize;
            set { _totalSize = value; OnPropertyChanged(nameof(TotalSize)); }
        }

        // --- YEH PROPERTY MISSING THI (FIXED) ---
        private long _downloadedSize;
        public long DownloadedSize
        {
            get => _downloadedSize;
            set { _downloadedSize = value; OnPropertyChanged(nameof(DownloadedSize)); }
        }

        // XAML Binding: Task.Progress
        private double _progress;
        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }

        // XAML Binding: Task.State
        private string _state = "Idle";
        public string State
        {
            get => _state;
            set { _state = value; OnPropertyChanged(nameof(State)); }
        }

        // Archive check for Extract button logic
        private bool _isArchive;
        public bool IsArchive
        {
            get => _isArchive;
            set { _isArchive = value; OnPropertyChanged(nameof(IsArchive)); }
        }

        public string Speed { get; set; } = "0 KB/s";
        public string TimeRemaining { get; set; } = "Calculating...";
        public string ErrorMessage { get; set; } = string.Empty;

        public DownloadTask(string url, string filename, long totalSize, string savePath)
        {
            Url = url;
            Filename = filename;
            TotalSize = totalSize;
            SavePath = savePath;
            State = "Idle";
            CheckIfArchive();
        }

        private void CheckIfArchive()
        {
            if (string.IsNullOrEmpty(Filename)) return;
            string ext = Path.GetExtension(Filename).ToLower();
            IsArchive = (ext == ".zip" || ext == ".rar" || ext == ".7z" || ext == ".tar");
        }
    }
}