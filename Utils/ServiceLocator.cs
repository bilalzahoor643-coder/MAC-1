using MAC_1.Services;

namespace MAC_1.Utils
{
    public static class ServiceLocator
    {
        public static DownloadService DownloadService => DownloadService.Instance;
        public static DataService DataService => DataService.Instance;
        public static PopupService PopupService => PopupService.Instance;
    }
}
