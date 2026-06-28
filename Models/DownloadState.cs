namespace MAC_1.Models
{
    /// <summary>
    /// Represents the current state of a download task.
    /// </summary>
    public enum DownloadState
    {
        Idle,
        Downloading,
        Completed,
        Failed
    }
}