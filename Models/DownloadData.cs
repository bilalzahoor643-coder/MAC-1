using System.Text.Json.Serialization;

namespace MAC_1.Models
{
    public class DownloadData
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("fileSize")]
        public long? FileSize { get; set; }

        [JsonPropertyName("referrer")]
        public string? Referrer { get; set; }
    }
}