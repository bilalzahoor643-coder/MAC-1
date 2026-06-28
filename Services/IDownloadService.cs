using System.Threading;
using System.Threading.Tasks;
using MAC_1.Models;

namespace MAC_1.Services
{
    public interface IDownloadService
    {
        Task StartDownloadAsync(DownloadTask task, CancellationToken token = default);
        void PauseAll();
        void ResumeAll();
    }
}
