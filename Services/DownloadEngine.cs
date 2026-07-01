using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class DownloadEngine
    {
        private static readonly Lazy<DownloadEngine> _instance = new(() => new DownloadEngine());
        public static DownloadEngine Instance => _instance.Value;

        private readonly HttpClientHandler _handler;
        private readonly HttpClient _httpClient;

        private DownloadEngine()
        {
            _handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = false,
                UseCookies = false,
                Proxy = WebRequest.GetSystemWebProxy(),
                UseProxy = false,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
            };

            _httpClient = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromMinutes(60)
            };
        }

        // ═══════════════════════════════════════════════════
        // REDIRECT RESOLVER MODULE
        // ═══════════════════════════════════════════════════

        public async Task<RedirectResult> ResolveRedirectsAsync(string url, DownloadSession? session = null)
        {
            var result = new RedirectResult { OriginalUrl = url };
            var chain = new List<string> { url };
            string currentUrl = url;
            int maxHops = 15;

            for (int hop = 0; hop < maxHops; hop++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, currentUrl);
                    ApplyBrowserFingerprint(request, session, hop > 0 ? chain[^1] : null);

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                    result.StatusCode = (int)response.StatusCode;
                    result.ResponseHeaders = CaptureResponseHeaders(response);

                    string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                    long contentLength = response.Content.Headers.ContentLength ?? 0;
                    string cdFileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "";

                    if (response.StatusCode is System.Net.HttpStatusCode.MovedPermanently
                        or System.Net.HttpStatusCode.Found
                        or System.Net.HttpStatusCode.SeeOther
                        or System.Net.HttpStatusCode.TemporaryRedirect
                        or System.Net.HttpStatusCode.PermanentRedirect)
                    {
                        string? location = response.Headers.Location?.ToString();
                        if (string.IsNullOrEmpty(location))
                        {
                            result.FinalUrl = currentUrl;
                            break;
                        }

                        if (!location.StartsWith("http"))
                        {
                            var baseUri = new Uri(currentUrl);
                            location = new Uri(baseUri, location).ToString();
                        }

                        chain.Add(location);
                        currentUrl = location;
                        continue;
                    }

                    result.FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? currentUrl;
                    result.FinalContentType = contentType;
                    result.ContentLength = contentLength;
                    result.IsHtml = contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);
                    result.HasContentDisposition = !string.IsNullOrEmpty(cdFileName);
                    result.ContentDispositionFileName = cdFileName;
                    result.AcceptRanges = response.Headers.Contains("Accept-Ranges");
                    result.ETag = response.Headers.ETag?.Tag ?? "";
                    result.LastModified = response.Content.Headers.LastModified?.ToString("R") ?? "";

                    break;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    result.FinalUrl = currentUrl;
                    break;
                }
            }

            result.RedirectChain = chain;
            result.HopCount = chain.Count - 1;
            return result;
        }

        private Dictionary<string, string> CaptureResponseHeaders(HttpResponseMessage response)
        {
            var headers = new Dictionary<string, string>();
            foreach (var h in response.Headers)
                headers[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);
            foreach (var h in response.Content.Headers)
                headers[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);
            return headers;
        }

        // ═══════════════════════════════════════════════════
        // REQUEST FINGERPRINT ENGINE
        // ═══════════════════════════════════════════════════

        private void ApplyBrowserFingerprint(HttpRequestMessage request, DownloadSession? session, string? refererOverride = null)
        {
            string userAgent = session?.UserAgent
                ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36";

            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

            request.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");

            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br, zstd");
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
            request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Chromium\";v=\"137\", \"Not/A)Brand\";v=\"24\", \"Google Chrome\";v=\"137\"");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Full-Version", "\"137.0.6853.60\"");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Arch", "\"x86\"");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Bitness", "\"64\"");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Form-Factors", "\"Desktop\"");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Full-Version-List",
                "\"Chromium\";v=\"137.0.6853.60\", \"Not/A)Brand\";v=\"24.0.0.0\", \"Google Chrome\";v=\"137.0.6853.60\"");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");

            string referer = refererOverride ?? session?.Referrer ?? "";
            if (!string.IsNullOrEmpty(referer))
            {
                request.Headers.TryAddWithoutValidation("Referer", referer);
                try
                {
                    var reqHost = new Uri(request.RequestUri!.ToString()).Host;
                    var refHost = new Uri(referer).Host;
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", reqHost == refHost ? "same-origin" : "cross-site");
                }
                catch { request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site"); }
            }
            else
            {
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            }

            if (!string.IsNullOrEmpty(session?.Origin))
                request.Headers.TryAddWithoutValidation("Origin", session.Origin);

            request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
            request.Headers.TryAddWithoutValidation("DNT", "1");
            request.Headers.TryAddWithoutValidation("Sec-GPC", "1");

            if (session?.ClientHints != null)
            {
                foreach (var kv in session.ClientHints)
                    if (!request.Headers.Contains(kv.Key))
                        request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }

            if (session?.Cookies != null && session.Cookies.Count > 0)
            {
                string cookieHeader = string.Join("; ", session.Cookies.Select(c => $"{c.Name}={c.Value}"));
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }

            if (!string.IsNullOrEmpty(session?.ETag))
                request.Headers.TryAddWithoutValidation("If-None-Match", session.ETag);

            if (!string.IsNullOrEmpty(session?.LastModified))
                request.Headers.TryAddWithoutValidation("If-Modified-Since", session.LastModified);

            if (session?.Headers != null)
            {
                string[] skipHeaders = {
                    "user-agent", "accept", "accept-language", "accept-encoding",
                    "cache-control", "pragma", "upgrade-insecure-requests",
                    "referer", "origin", "cookie", "connection",
                    "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site", "sec-fetch-user",
                    "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
                    "sec-ch-ua-full-version", "sec-ch-ua-arch", "sec-ch-ua-bitness",
                    "sec-ch-ua-form-factors", "sec-ch-ua-full-version-list",
                    "content-length", "content-type", "host",
                    "dnt", "sec-gpc", "if-none-match", "if-modified-since"
                };

                foreach (var kv in session.Headers)
                {
                    string key = kv.Key.ToLowerInvariant();
                    if (skipHeaders.Contains(key)) continue;
                    if (request.Headers.Contains(kv.Key)) continue;
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
        }

        // ═══════════════════════════════════════════════════
        // ANALYZE URL
        // ═══════════════════════════════════════════════════

        public async Task<DownloadAnalysis> AnalyzeUrlAsync(string url, DownloadSession? session = null)
        {
            try
            {
                var redirectResult = await ResolveRedirectsAsync(url, session);

                using var request = new HttpRequestMessage(HttpMethod.Head, redirectResult.FinalUrl);
                ApplyBrowserFingerprint(request, session);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                bool isHtml = contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);

                var result = new DownloadAnalysis
                {
                    Url = url,
                    FinalUrl = redirectResult.FinalUrl,
                    FileSize = redirectResult.ContentLength > 0 ? redirectResult.ContentLength : (response.Content.Headers.ContentLength ?? 0),
                    MimeType = contentType,
                    AcceptRanges = response.Headers.Contains("Accept-Ranges"),
                    IsValid = !isHtml,
                    RedirectHops = redirectResult.HopCount,
                    IsHtml = isHtml
                };

                string? fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                if (string.IsNullOrEmpty(fileName)) fileName = ExtractFilenameFromUrl(url);
                result.Filename = fileName ?? "download";

                return result;
            }
            catch
            {
                return new DownloadAnalysis { Url = url, IsValid = false };
            }
        }

        // ═══════════════════════════════════════════════════
        // START DOWNLOAD
        // ═══════════════════════════════════════════════════

        public async Task StartDownloadAsync(DownloadTask task, DownloadSession? session = null, IProgress<DownloadProgress>? progress = null, CancellationToken? externalToken = null)
        {
            var cts = externalToken.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(externalToken.Value)
                : new CancellationTokenSource();
            task.Cts = cts;
            task.State = DownloadState.Downloading;
            task.StartTime = DateTime.Now;

            try
            {
                string downloadUrl = task.Url;

                if (session != null)
                {
                    var redirect = await ResolveRedirectsAsync(task.Url, session);
                    if (!string.IsNullOrEmpty(redirect.FinalUrl))
                    {
                        downloadUrl = redirect.FinalUrl;
                        task.Url = redirect.FinalUrl;

                        if (!string.IsNullOrEmpty(redirect.ContentDispositionFileName))
                            task.Filename = redirect.ContentDispositionFileName;

                        if (redirect.ContentLength > 0)
                            task.TotalSize = redirect.ContentLength;
                    }
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                ApplyBrowserFingerprint(request, session);

                if (session?.AcceptRanges == "bytes" && task.DownloadedSize > 0)
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(task.DownloadedSize, null);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    task.State = DownloadState.Completed;
                    task.Speed = "0 B/s";
                    task.TimeRemaining = "00:00";
                    return;
                }

                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                bool isHtml = contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);

                if (isHtml)
                {
                    var bodyPreview = await response.Content.ReadAsStringAsync(cts.Token);
                    if (bodyPreview.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                        bodyPreview.Contains("<body", StringComparison.OrdinalIgnoreCase))
                    {
                        task.State = DownloadState.Failed;
                        task.ErrorMessage = "Server returned HTML instead of file. Possible login wall or geo-block.";
                        task.Speed = "0 B/s";
                        return;
                    }
                }

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                if (totalBytes > 0) task.TotalSize = totalBytes;

                var finalFileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                if (!string.IsNullOrEmpty(finalFileName) && finalFileName != task.Filename)
                    task.Filename = finalFileName;

                var saveDir = Path.GetDirectoryName(task.SavePath);
                if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);

                using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var fileStream = new FileStream(
                    task.SavePath,
                    task.DownloadedSize > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None, 8192);

                var buffer = new byte[8192];
                long totalRead = task.DownloadedSize;
                var lastProgressTime = DateTime.UtcNow;
                long lastBytesRead = totalRead;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
                    totalRead += bytesRead;
                    task.DownloadedSize = totalRead;

                    if (totalBytes > 0)
                        task.Progress = (double)totalRead / totalBytes * 100;

                    var now = DateTime.UtcNow;
                    var elapsed = (now - lastProgressTime).TotalSeconds;
                    if (elapsed >= 0.5)
                    {
                        double speed = (totalRead - lastBytesRead) / elapsed;
                        task.Speed = FormatSpeed(speed);

                        if (totalBytes > 0)
                        {
                            double remaining = (totalBytes - totalRead) / speed;
                            task.TimeRemaining = FormatTime(remaining);
                        }

                        lastBytesRead = totalRead;
                        lastProgressTime = now;

                        progress?.Report(new DownloadProgress
                        {
                            DownloadedBytes = totalRead,
                            TotalBytes = totalBytes,
                            Speed = speed,
                            Progress = task.Progress
                        });
                    }
                }

                task.Progress = 100;
                task.DownloadedSize = totalRead;
                task.State = DownloadState.Completed;
                task.CompletedTime = DateTime.Now;
                task.Speed = "0 B/s";
                task.TimeRemaining = "00:00";
            }
            catch (OperationCanceledException)
            {
                task.State = DownloadState.Paused;
                task.Speed = "0 B/s";
            }
            catch (Exception ex)
            {
                task.State = DownloadState.Failed;
                task.ErrorMessage = ex.Message;
                task.Speed = "0 B/s";
            }
        }

        private string ExtractFilenameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string name = Path.GetFileName(uri.LocalPath);
                return string.IsNullOrEmpty(name) ? "download" : Uri.UnescapeDataString(name);
            }
            catch { return "download"; }
        }

        public void PauseDownload(DownloadTask task)
        {
            if (task.State == DownloadState.Downloading) { task.Cts?.Cancel(); task.State = DownloadState.Paused; task.Speed = "0 B/s"; }
        }

        public void ResumeDownload(DownloadTask task, DownloadSession? session = null)
        {
            if (task.State == DownloadState.Paused) _ = StartDownloadAsync(task, session);
        }

        public void CancelDownload(DownloadTask task)
        {
            task.Cts?.Cancel(); task.State = DownloadState.Idle; task.Speed = "0 B/s"; task.Progress = 0;
            try { if (File.Exists(task.SavePath)) File.Delete(task.SavePath); } catch { }
        }

        public static string FormatSpeed(double bps)
        {
            if (bps <= 0) return "0 B/s";
            string[] s = ["B/s", "KB/s", "MB/s", "GB/s"];
            int i = 0; double v = bps;
            while (v >= 1024 && i < s.Length - 1) { v /= 1024; i++; }
            return $"{v:F1} {s[i]}";
        }

        public static string FormatTime(double sec)
        {
            if (sec <= 0 || double.IsInfinity(sec)) return "--:--";
            int m = (int)(sec / 60), s = (int)(sec % 60);
            if (m >= 60) { int h = m / 60; m %= 60; return $"{h:D2}:{m:D2}:{s:D2}"; }
            return $"{m:D2}:{s:D2}";
        }
    }

    // ═══════════════════════════════════════════════════
    // DATA MODELS
    // ═══════════════════════════════════════════════════

    public class RedirectResult
    {
        public string OriginalUrl { get; set; } = string.Empty;
        public string FinalUrl { get; set; } = string.Empty;
        public List<string> RedirectChain { get; set; } = new();
        public int HopCount { get; set; }
        public int StatusCode { get; set; }
        public string FinalContentType { get; set; } = string.Empty;
        public long ContentLength { get; set; }
        public bool IsHtml { get; set; }
        public bool HasContentDisposition { get; set; }
        public string ContentDispositionFileName { get; set; } = string.Empty;
        public bool AcceptRanges { get; set; }
        public string ETag { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
        public string? Error { get; set; }
        public Dictionary<string, string> ResponseHeaders { get; set; } = new();
    }

    public class DownloadAnalysis
    {
        public string Url { get; set; } = string.Empty;
        public string FinalUrl { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public bool AcceptRanges { get; set; }
        public bool IsValid { get; set; }
        public bool IsHtml { get; set; }
        public int RedirectHops { get; set; }
    }

    public class DownloadProgress
    {
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double Speed { get; set; }
        public double Progress { get; set; }
    }
}
