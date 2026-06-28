using MAC_1.Services;

namespace MAC_1.Utils
{
    /// <summary>
    /// A simple static locator to provide service instances across the application.
    /// This helps keep ViewModels clean and decoupled.
    /// </summary>
    public static class ServiceLocator
    {
        // 1. Download Service ka aik permanent (Static) instance
        private static readonly IDownloadService _downloadService = new DownloadService();

        /// <summary>
        /// Provides access to the Global Download Service.
        /// </summary>
        public static IDownloadService DownloadService => _downloadService;
    }
}