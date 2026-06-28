using System.Threading.Tasks;
using MAC_1.Models;

namespace MAC_1.Services
{
    /// <summary>
    /// Interface for the download service. 
    /// Defines the contract for downloading files.
    /// </summary>
    public interface IDownloadService
    {
        /// <summary>
        /// Starts a single file download for the given task.
        /// </summary>
        /// <param name="task">The task object containing URL and file details.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        Task StartDownloadAsync(DownloadTask task);
    }
}