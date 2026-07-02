using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MAC_1.Models;
using MAC_1.Services;

namespace MAC_1.ViewModels
{
    public class DownloadViewModel : ViewModelBase
    {
        public DownloadTask Task { get; }

        private bool _isExpanded;
        public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

        public string ProgressDisplay => $"{Task.Progress:F1}%";
        public string SpeedDisplay => Task.Speed;
        public string SizeDisplay => Task.SizeDisplay;
        public string TimeDisplay => Task.TimeRemaining;
        public string StateDisplay => Task.StateText;

        public bool IsDownloading => Task.State == DownloadState.Downloading;
        public bool IsPaused => Task.State == DownloadState.Paused;
        public bool IsCompleted => Task.State == DownloadState.Completed;
        public bool IsFailed => Task.State == DownloadState.Failed;
        public bool IsIdle => Task.State == DownloadState.Idle || Task.State == DownloadState.Queued;

        public ObservableCollection<ChunkInfo> Chunks { get; } = new();

        public DownloadViewModel(DownloadTask task)
        {
            Task = task;
            task.PropertyChanged += (_, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(ProgressDisplay));
                    OnPropertyChanged(nameof(SpeedDisplay));
                    OnPropertyChanged(nameof(SizeDisplay));
                    OnPropertyChanged(nameof(TimeDisplay));
                    OnPropertyChanged(nameof(StateDisplay));
                    OnPropertyChanged(nameof(IsDownloading));
                    OnPropertyChanged(nameof(IsPaused));
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(IsFailed));
                    OnPropertyChanged(nameof(IsIdle));
                });
            };
        }

        public void ToggleExpand() => IsExpanded = !IsExpanded;
    }

    public class ChunkInfo
    {
        public string ChunkNum { get; set; } = "";
        public string Range { get; set; } = "";
        public string Info { get; set; } = "";
        public double ProgressPercent { get; set; }
        public string StartPos { get; set; } = "";
        public string Size { get; set; } = "";
        public string Speed { get; set; } = "";
    }
}
