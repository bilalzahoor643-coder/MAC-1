using System.Windows;
using MAC_1.Services;

namespace MAC_1
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize services
            var dataService = DataService.Instance;
            var settingsService = SettingsService.Instance;
            var cardService = CardService.Instance;
            var popupService = PopupService.Instance;

            // Start pipe server for extension communication
            PipeServer.Instance.DownloadReceived += data =>
            {
                Dispatcher.Invoke(() => popupService.ShowDownloadPopup(data));
            };
            PipeServer.Instance.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            PipeServer.Instance.Stop();
            DataService.Instance.SaveHistory();
            base.OnExit(e);
        }
    }
}
