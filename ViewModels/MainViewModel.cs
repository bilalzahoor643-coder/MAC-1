using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MAC_1.Models;
using MAC_1.Services;

namespace MAC_1.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private static readonly Lazy<MainViewModel> _instance = new(() => new MainViewModel());
        public static MainViewModel Instance => _instance.Value;

        public ObservableCollection<DownloadTask> Downloads => DataService.Instance.Downloads;

        private int _totalDownloads;
        private int _completedDownloads;
        private int _activeDownloads;
        private int _failedDownloads;
        private string _totalSizeDisplay = "0 B";
        private string _diskSpeed = "0 B/s";
        private string _activeCount = "0";

        public int TotalDownloads { get => _totalDownloads; set => SetProperty(ref _totalDownloads, value); }
        public int CompletedDownloads { get => _completedDownloads; set => SetProperty(ref _completedDownloads, value); }
        public int ActiveDownloads { get => _activeDownloads; set => SetProperty(ref _activeDownloads, value); }
        public int FailedDownloads { get => _failedDownloads; set => SetProperty(ref _failedDownloads, value); }
        public string TotalSizeDisplay { get => _totalSizeDisplay; set => SetProperty(ref _totalSizeDisplay, value); }
        public string DiskSpeed { get => _diskSpeed; set => SetProperty(ref _diskSpeed, value); }
        public string ActiveCount { get => _activeCount; set => SetProperty(ref _activeCount, value); }

        private MainViewModel()
        {
            DataService.Instance.StatsChanged += UpdateStats;
            DataService.Instance.Downloads.CollectionChanged += (_, _) =>
            {
                Application.Current.Dispatcher.Invoke(UpdateStats);
            };
            UpdateStats();
        }

        private void UpdateStats()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TotalDownloads = DataService.Instance.TotalDownloads;
                CompletedDownloads = DataService.Instance.CompletedDownloads;
                ActiveDownloads = DataService.Instance.ActiveDownloads;
                FailedDownloads = DataService.Instance.FailedDownloads;
                TotalSizeDisplay = DownloadTask.FormatSize(DataService.Instance.TotalSizeDownloaded);
                ActiveCount = DataService.Instance.ActiveDownloads.ToString();
            });
        }

        public ObservableCollection<DownloadTask> GetActiveDownloads()
        {
            return new ObservableCollection<DownloadTask>(
                Downloads.Where(d => d.State == DownloadState.Downloading || d.State == DownloadState.Paused));
        }

        public ObservableCollection<DownloadTask> GetCompletedDownloads()
        {
            return new ObservableCollection<DownloadTask>(
                Downloads.Where(d => d.State == DownloadState.Completed));
        }

        public ObservableCollection<DownloadTask> GetQueuedDownloads()
        {
            return new ObservableCollection<DownloadTask>(
                Downloads.Where(d => d.State == DownloadState.Queued || d.State == DownloadState.Waiting));
        }

        public ObservableCollection<DownloadTask> GetFailedDownloads()
        {
            return new ObservableCollection<DownloadTask>(
                Downloads.Where(d => d.State == DownloadState.Failed));
        }
    }
}
