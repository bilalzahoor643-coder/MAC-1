using System.Text.Json.Serialization;

namespace MAC_1.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DownloadEventType
    {
        Started,
        MetadataReceived,
        ProgressChanged,
        SpeedChanged,
        ETAChanged,
        StateChanged,
        Completed,
        Failed,
        Cancelled
    }

    public class DownloadEngineEvent
    {
        [JsonPropertyName("eventType")] public DownloadEventType EventType { get; set; }
        [JsonPropertyName("sessionId")] public string SessionId { get; set; } = string.Empty;
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("filename")] public string Filename { get; set; } = string.Empty;
        [JsonPropertyName("savePath")] public string SavePath { get; set; } = string.Empty;
        [JsonPropertyName("fileSize")] public long FileSize { get; set; }
        [JsonPropertyName("bytesDownloaded")] public long BytesDownloaded { get; set; }
        [JsonPropertyName("progress")] public double Progress { get; set; }
        [JsonPropertyName("speed")] public double Speed { get; set; }
        [JsonPropertyName("averageSpeed")] public double AverageSpeed { get; set; }
        [JsonPropertyName("eta")] public double ETA { get; set; }
        [JsonPropertyName("state")] public DownloadState2 State { get; set; }
        [JsonPropertyName("resumeSupported")] public bool ResumeSupported { get; set; }
        [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
        [JsonPropertyName("elapsedSeconds")] public double ElapsedSeconds { get; set; }
        [JsonPropertyName("httpStatusCode")] public int HttpStatusCode { get; set; }
        [JsonPropertyName("finalUrl")] public string? FinalUrl { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DownloadState2
    {
        Pending,
        Starting,
        ReceivingMetadata,
        Downloading,
        Paused,
        Resumed,
        Completed,
        Failed,
        Cancelled
    }
}
