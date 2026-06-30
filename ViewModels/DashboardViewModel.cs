using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MAC_1.Models;
using MAC_1.Services;

namespace MAC_1.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private static readonly Lazy<DashboardViewModel> _instance = new(() => new DashboardViewModel());
        public static DashboardViewModel Instance => _instance.Value;

        public ObservableCollection<DownloadTask> RecentDownloads { get; } = new();
        public ObservableCollection<DownloadTask> CompletedDownloads { get; } = new();

        private int _totalDownloads;
        private int _completedCount;
        private int _failedCount;
        private string _totalSize = "0 B";

        public int TotalDownloads { get => _totalDownloads; set => SetProperty(ref _totalDownloads, value); }
        public int CompletedCount { get => _completedCount; set => SetProperty(ref _completedCount, value); }
        public int FailedCount { get => _failedCount; set => SetProperty(ref _failedCount, value); }
        public string TotalSize { get => _totalSize; set => SetProperty(ref _totalSize, value); }

        private DashboardViewModel()
        {
            DataService.Instance.StatsChanged += Refresh;
            DataService.Instance.Downloads.CollectionChanged += (_, _) =>
            {
                Application.Current.Dispatcher.Invoke(Refresh);
            };
            Refresh();
        }

        public void Refresh()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var all = DataService.Instance.Downloads;

                TotalDownloads = all.Count;
                CompletedCount = all.Count(d => d.State == DownloadState.Completed);
                FailedCount = all.Count(d => d.State == DownloadState.Failed);
                TotalSize = DownloadTask.FormatSize(DataService.Instance.TotalSizeDownloaded);

                RecentDownloads.Clear();
                foreach (var task in all.Where(d => d.State != DownloadState.Completed)
                                       .OrderByDescending(d => d.StartTime)
                                       .Take(5))
                    RecentDownloads.Add(task);

                CompletedDownloads.Clear();
                foreach (var task in all.Where(d => d.State == DownloadState.Completed)
                                       .OrderByDescending(d => d.CompletedTime)
                                       .Take(5))
                    CompletedDownloads.Add(task);
            });
        }
    }
}
