using System;
using System.Collections.Generic;
using System.Linq;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class CardService
    {
        private static readonly Lazy<CardService> _instance = new(() => new CardService());
        public static CardService Instance => _instance.Value;

        private readonly Dictionary<string, DownloadCardInfo> _cards = new();

        public event Action<DownloadCardInfo>? CardCreated;
        public event Action<DownloadCardInfo>? CardUpdated;
        public event Action<DownloadCardInfo>? CardRemoved;

        public IReadOnlyList<DownloadCardInfo> Cards => _cards.Values.ToList().AsReadOnly();

        private CardService() { }

        public void CreateCard(DownloadTask task)
        {
            var card = new DownloadCardInfo
            {
                TaskId = task.Id,
                Filename = task.Filename,
                Url = task.Url,
                SavePath = task.SavePath,
                TotalSize = task.TotalSize,
                Category = task.Category,
                CreatedAt = DateTime.Now
            };

            _cards[task.Id] = card;
            CardCreated?.Invoke(card);

            task.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DownloadTask.Progress) ||
                    e.PropertyName == nameof(DownloadTask.State) ||
                    e.PropertyName == nameof(DownloadTask.Speed) ||
                    e.PropertyName == nameof(DownloadTask.DownloadedSize))
                {
                    UpdateCard(task);
                }
            };
        }

        private void UpdateCard(DownloadTask task)
        {
            if (_cards.TryGetValue(task.Id, out var card))
            {
                card.Progress = task.Progress;
                card.State = task.State;
                card.Speed = task.Speed;
                card.DownloadedSize = task.DownloadedSize;
                card.TimeRemaining = task.TimeRemaining;
                CardUpdated?.Invoke(card);
            }
        }

        public void RemoveCard(string taskId)
        {
            if (_cards.TryGetValue(taskId, out var card))
            {
                _cards.Remove(taskId);
                CardRemoved?.Invoke(card);
            }
        }

        public DownloadCardInfo? GetCard(string taskId)
            => _cards.TryGetValue(taskId, out var card) ? card : null;
    }

    public class DownloadCardInfo
    {
        public string TaskId { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string SavePath { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long DownloadedSize { get; set; }
        public double Progress { get; set; }
        public DownloadState State { get; set; }
        public string Speed { get; set; } = "0 B/s";
        public string TimeRemaining { get; set; } = "--:--";
        public string Category { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public string SizeDisplay
        {
            get
            {
                if (TotalSize <= 0) return "Unknown";
                return $"{DownloadTask.FormatSize(DownloadedSize)} / {DownloadTask.FormatSize(TotalSize)}";
            }
        }
    }
}
