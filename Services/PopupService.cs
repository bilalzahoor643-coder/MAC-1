using System;
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
                _activePopup.Closed += (_, _) =>
                {
                    _activePopup = null;
                    PopupClosed?.Invoke();
                };
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
                _activePopup.Closed += (_, _) =>
                {
                    _activePopup = null;
                    PopupClosed?.Invoke();
                };
                _activePopup.Show();
            });
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
