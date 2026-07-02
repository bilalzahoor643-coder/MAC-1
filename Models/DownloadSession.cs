using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MAC_1.Models
{
    public class DownloadSession
    {
        public string SessionId { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FinalUrl { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public string SuggestedFilename { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string Referrer { get; set; } = string.Empty;
        public string Origin { get; set; } = string.Empty;
        public string RequestMethod { get; set; } = "GET";
        public string UserAgent { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string Initiator { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Protocol { get; set; } = string.Empty;
        public int Port { get; set; }
        public long Timestamp { get; set; }
        public string DownloadSource { get; set; } = "browser";
        public bool ResumeSupported { get; set; } = true;
        public string SavePath { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string Description { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string WebsiteTitle { get; set; } = string.Empty;
        public string ContentDisposition { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public long ContentLength { get; set; }
        public string AcceptRanges { get; set; } = string.Empty;
        public string ContentEncoding { get; set; } = string.Empty;
        public string ETag { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;

        public Dictionary<string, string> Headers { get; set; } = new();
        public Dictionary<string, string>? ResponseHeaders { get; set; }
        public List<CookieData> Cookies { get; set; } = new();
        public Dictionary<string, string>? ClientHints { get; set; }
        public object? PostData { get; set; }
        public TabData? Tab { get; set; }
        public List<string> RedirectChain { get; set; } = new();
        public List<RawHeader>? BrowserRawHeaders { get; set; }
        public List<RawHeader>? BrowserResponseRawHeaders { get; set; }
        public string BrowserRequestType { get; set; } = string.Empty;
    }

    public class CookieData
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        [JsonConverter(typeof(FlexibleLongConverter))] public long? Expires { get; set; }
        public bool HttpOnly { get; set; }
        public bool Secure { get; set; }
        public string SameSite { get; set; } = string.Empty;
        public bool HostOnly { get; set; }
        public bool Session { get; set; }
    }

    public class FlexibleLongConverter : JsonConverter<long?>
    {
        public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                case JsonTokenType.None:
                    return null;
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long lval)) return lval;
                    if (reader.TryGetDouble(out double dval)) return (long)dval;
                    return null;
                case JsonTokenType.String:
                    var s = reader.GetString();
                    if (string.IsNullOrEmpty(s)) return null;
                    if (long.TryParse(s, out long val)) return val;
                    if (double.TryParse(s, out double dval2)) return (long)dval2;
                    return null;
                case JsonTokenType.True: return 1;
                case JsonTokenType.False: return 0;
                default: return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteNumberValue(value.Value);
            else writer.WriteNullValue();
        }
    }

    public class TabData
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string FavIconUrl { get; set; } = string.Empty;
        public int WindowId { get; set; }
        public int FrameId { get; set; }
        public bool Active { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class RawHeader
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
