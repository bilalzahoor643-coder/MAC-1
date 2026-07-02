using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MAC_1.Service.Models
{
    public class DownloadSession
    {
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("finalUrl")] public string FinalUrl { get; set; } = string.Empty;
        [JsonPropertyName("filename")] public string Filename { get; set; } = string.Empty;
        [JsonPropertyName("suggestedFilename")] public string SuggestedFilename { get; set; } = string.Empty;
        [JsonPropertyName("fileExtension")] public string FileExtension { get; set; } = string.Empty;
        [JsonPropertyName("fileSize")] public long FileSize { get; set; }
        [JsonPropertyName("mimeType")] public string MimeType { get; set; } = string.Empty;
        [JsonPropertyName("referrer")] public string Referrer { get; set; } = string.Empty;
        [JsonPropertyName("origin")] public string Origin { get; set; } = string.Empty;
        [JsonPropertyName("method")] public string RequestMethod { get; set; } = "GET";
        [JsonPropertyName("userAgent")] public string UserAgent { get; set; } = string.Empty;
        [JsonPropertyName("platform")] public string Platform { get; set; } = string.Empty;
        [JsonPropertyName("initiator")] public string Initiator { get; set; } = string.Empty;
        [JsonPropertyName("host")] public string Host { get; set; } = string.Empty;
        [JsonPropertyName("protocol")] public string Protocol { get; set; } = string.Empty;
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
        [JsonPropertyName("downloadSource")] public string DownloadSource { get; set; } = "browser";
        [JsonPropertyName("resumeSupported")] public bool ResumeSupported { get; set; } = true;
        [JsonPropertyName("savePath")] public string SavePath { get; set; } = string.Empty;
        [JsonPropertyName("category")] public string Category { get; set; } = "General";
        [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("website")] public string Website { get; set; } = string.Empty;
        [JsonPropertyName("websiteTitle")] public string WebsiteTitle { get; set; } = string.Empty;
        [JsonPropertyName("contentDisposition")] public string ContentDisposition { get; set; } = string.Empty;
        [JsonPropertyName("statusCode")] public int StatusCode { get; set; }
        [JsonPropertyName("contentLength")] public long ContentLength { get; set; }
        [JsonPropertyName("acceptRanges")] public string AcceptRanges { get; set; } = string.Empty;
        [JsonPropertyName("contentEncoding")] public string ContentEncoding { get; set; } = string.Empty;
        [JsonPropertyName("etag")] public string ETag { get; set; } = string.Empty;
        [JsonPropertyName("lastModified")] public string LastModified { get; set; } = string.Empty;
        [JsonPropertyName("headers")] public Dictionary<string, string> Headers { get; set; } = new();
        [JsonPropertyName("responseHeaders")] public Dictionary<string, string>? ResponseHeaders { get; set; }
        [JsonPropertyName("cookies")] public List<CookieData> Cookies { get; set; } = new();
        [JsonPropertyName("clientHints")] public Dictionary<string, string>? ClientHints { get; set; }
        [JsonPropertyName("postData")] public object? PostData { get; set; }
        [JsonPropertyName("tab")] public TabData? Tab { get; set; }
        [JsonPropertyName("redirectChain")] public List<string> RedirectChain { get; set; } = new();
        [JsonPropertyName("sessionId")] public string SessionId { get; set; } = string.Empty;
        [JsonPropertyName("browserRawHeaders")] public List<RawHeader>? BrowserRawHeaders { get; set; }
        [JsonPropertyName("browserResponseRawHeaders")] public List<RawHeader>? BrowserResponseRawHeaders { get; set; }
        [JsonPropertyName("browserRequestType")] public string BrowserRequestType { get; set; } = string.Empty;
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public class CookieData
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
        [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;
        [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
        [JsonPropertyName("expires")] [JsonConverter(typeof(FlexibleLongConverter))] public long? Expires { get; set; }
        [JsonPropertyName("httpOnly")] public bool HttpOnly { get; set; }
        [JsonPropertyName("secure")] public bool Secure { get; set; }
        [JsonPropertyName("sameSite")] public string SameSite { get; set; } = string.Empty;
        [JsonPropertyName("hostOnly")] public bool HostOnly { get; set; }
        [JsonPropertyName("session")] public bool Session { get; set; }
    }

    public class TabData
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("favIconUrl")] public string FavIconUrl { get; set; } = string.Empty;
        [JsonPropertyName("windowId")] public int WindowId { get; set; }
        [JsonPropertyName("frameId")] public int FrameId { get; set; }
        [JsonPropertyName("active")] public bool Active { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    }

    public class RawHeader
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
    }

    public class SizeUpdateRequest
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("fileSize")] public long FileSize { get; set; }
    }

    public class StartDownloadRequest
    {
        [JsonPropertyName("sessionId")] public string SessionId { get; set; } = string.Empty;
        [JsonPropertyName("savePath")] public string? SavePath { get; set; }
    }

    public class PipeMessage
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("data")] public string Data { get; set; } = string.Empty;
        [JsonPropertyName("requestId")] public string RequestId { get; set; } = string.Empty;
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
}
