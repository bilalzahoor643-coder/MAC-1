using System.Text.Json.Serialization;

namespace MAC_1.Service.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DownloadState
    {
        Pending = 0,
        Starting = 1,
        ReceivingMetadata = 2,
        Downloading = 3,
        Paused = 4,
        Resumed = 5,
        Completed = 6,
        Failed = 7,
        Cancelled = 8
    }
}
