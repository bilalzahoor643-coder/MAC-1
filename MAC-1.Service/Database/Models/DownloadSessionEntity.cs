using System;
using System.Collections.Generic;

namespace MAC_1.Service.Database.Models
{
    public class DownloadSessionEntity
    {
        public string SessionId { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FinalUrl { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string Referrer { get; set; } = string.Empty;
        public string Origin { get; set; } = string.Empty;
        public string RequestMethod { get; set; } = "GET";
        public string UserAgent { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Status { get; set; } = "pending";
        public string SavePath { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public long BytesDownloaded { get; set; }
        public int Connections { get; set; } = 1;
        public bool ResumeSupported { get; set; } = true;
        public string? ETag { get; set; }
        public string? LastModified { get; set; }
        public string? AcceptRanges { get; set; }
        public string RawHeadersJson { get; set; } = "{}";
        public string RawCookiesJson { get; set; } = "[]";
        public string RawClientHintsJson { get; set; } = "{}";
        public string RawTabJson { get; set; } = "{}";
        public string RawRedirectChainJson { get; set; } = "[]";
        public string RawBrowserHeadersJson { get; set; } = "[]";
        public string RawPostDataJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        // Progress columns (v2)
        public double Progress { get; set; }
        public double Speed { get; set; }
        public double AverageSpeed { get; set; }
        public double ETA { get; set; }
        public string? ErrorMessage { get; set; }
        public int HttpStatusCode { get; set; }
        public double ElapsedSeconds { get; set; }
    }

    public class SettingsEntity
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class HistoryEntity
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Result { get; set; } = string.Empty;
        public string FinalPath { get; set; } = string.Empty;
        public long FinalSize { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
