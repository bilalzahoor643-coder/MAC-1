using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using MAC_1.Services;
using MAC_1.Models;

namespace MAC_1.ViewModels
{
    /// <summary>
    /// The main controller for the application dashboard.
    /// Manages the collection of download cards.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        // UI (XAML) is collection ko bind karega cards dikhane ke liye
        public ObservableCollection<DownloadCardViewModel> DownloadCards { get; set; }

        public MainViewModel()
        {
            DownloadCards = new ObservableCollection<DownloadCardViewModel>();

            // IMPORTANT: Hum "DownloadService" mein maujood tasks ko monitor kar rahe hain.
            // Note: Maine yahan 'DownloadService.Instance' rakha hai kyunke aapki app isi logic par chal rahi hai.
            DownloadService.Instance.Downloads.CollectionChanged += OnDownloadsChanged;

            // Agar app shuru hote hi pehle se kuch downloads majood hain, unhein cards mein badlein
            foreach (var task in DownloadService.Instance.Downloads)
            {
                AddCardForTask(task);
            }
        }

        /// <summary>
        /// Jab bhi Service mein naya Download add ya remove hoga, ye method trigger hoga.
        /// </summary>
        private void OnDownloadsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (DownloadTask newTask in e.NewItems)
                {
                    AddCardForTask(newTask);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (DownloadTask oldTask in e.OldItems)
                {
                    RemoveCardForTask(oldTask);
                }
            }
        }

        // Helper: Naya UI Card (ViewModel) banane ke liye
        private void AddCardForTask(DownloadTask task)
        {
            // Ensure duplicate cards na banein
            if (!DownloadCards.Any(c => c.Task.Id == task.Id))
            {
                DownloadCards.Add(new DownloadCardViewModel(task));
            }
        }

        // Helper: Card ko UI se hatane ke liye
        private void RemoveCardForTask(DownloadTask task)
        {
            var card = DownloadCards.FirstOrDefault(c => c.Task.Id == task.Id);
            if (card != null)
            {
                DownloadCards.Remove(card);
            }
        }
    }
}