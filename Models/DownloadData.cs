namespace MAC_1.Models
{
    public class DownloadData
    {
        public string Url { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Referrer { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string SavePath { get; set; } = string.Empty;
    }
}
