using System.Windows;
using MAC_1.Services;

namespace MAC_1
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var dataService = DataService.Instance;
            var settingsService = SettingsService.Instance;
            var cardService = CardService.Instance;
            var popupService = PopupService.Instance;

            PipeServer.Instance.DownloadReceived += data =>
            {
                Dispatcher.Invoke(() => popupService.ShowDownloadPopup(data));
            };
            PipeServer.Instance.Start();

            ExtensionService.Instance.DownloadReceived += data =>
            {
                Dispatcher.Invoke(() => popupService.ShowDownloadPopup(data));
            };
            ExtensionService.Instance.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ExtensionService.Instance.Stop();
            PipeServer.Instance.Stop();
            DataService.Instance.SaveHistory();
            base.OnExit(e);
        }
    }
}
