using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MAC_1.Models
{
    public class DownloadTask : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString("N")[..12];
        private string _url = string.Empty;
        private string _savePath = string.Empty;
        private string _filename = string.Empty;
        private long _totalSize;
        private long _downloadedSize;
        private double _progress;
        private DownloadState _state = DownloadState.Idle;
        private string _speed = "0 B/s";
        private string _timeRemaining = "--:--";
        private string _errorMessage = string.Empty;
        private string _category = "Compressed";
        private int _connections = 16;
        private DateTime _startTime;
        private DateTime _completedTime;
        private string _saveFolder = string.Empty;

        public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public string Url { get => _url; set { _url = value; OnPropertyChanged(); } }
        public string SavePath { get => _savePath; set { _savePath = value; OnPropertyChanged(); } }
        public string Filename { get => _filename; set { _filename = value; OnPropertyChanged(); } }
        public long TotalSize { get => _totalSize; set { _totalSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); } }
        public long DownloadedSize { get => _downloadedSize; set { _downloadedSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); } }
        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }
        public DownloadState State { get => _state; set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(StateText)); } }
        public string Speed { get => _speed; set { _speed = value; OnPropertyChanged(); } }
        public string TimeRemaining { get => _timeRemaining; set { _timeRemaining = value; OnPropertyChanged(); } }
        public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }
        public string Category { get => _category; set { _category = value; OnPropertyChanged(); } }
        public int Connections { get => _connections; set { _connections = value; OnPropertyChanged(); } }
        public DateTime StartTime { get => _startTime; set { _startTime = value; OnPropertyChanged(); } }
        public DateTime CompletedTime { get => _completedTime; set { _completedTime = value; OnPropertyChanged(); } }
        public string SaveFolder { get => _saveFolder; set { _saveFolder = value; OnPropertyChanged(); } }
        public bool IsArchive { get; set; }
        public bool ResumeSupported { get; set; } = true;
        public CancellationTokenSource? Cts { get; set; }

        public string SizeDisplay
        {
            get
            {
                if (TotalSize <= 0) return "Unknown";
                string downloaded = FormatSize(DownloadedSize);
                string total = FormatSize(TotalSize);
                return $"{downloaded} / {total}";
            }
        }

        public string StateText => State switch
        {
            DownloadState.Idle => "Ready",
            DownloadState.Waiting => "Waiting",
            DownloadState.Downloading => "Downloading",
            DownloadState.Paused => "Paused",
            DownloadState.Completed => "Completed",
            DownloadState.Failed => "Failed",
            DownloadState.Queued => "Queued",
            _ => State.ToString()
        };

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] sizes = ["B", "KB", "MB", "GB", "TB"];
            int i = 0;
            double size = bytes;
            while (size >= 1024 && i < sizes.Length - 1) { size /= 1024; i++; }
            return $"{size:F2} {sizes[i]}";
        }

        public void CheckIfArchive()
        {
            if (string.IsNullOrEmpty(Filename)) return;
            string ext = System.IO.Path.GetExtension(Filename).ToLowerInvariant();
            IsArchive = ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
