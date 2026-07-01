using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                UseCookies = false,
                Proxy = WebRequest.GetSystemWebProxy(),
                UseProxy = false
            };

            _httpClient = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromMinutes(60)
            };
        }

        public async Task<DownloadAnalysis> AnalyzeUrlAsync(string url, DownloadSession? session = null)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);

                ApplyBrowserFingerprint(request, session);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                bool isHtml = contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);

                var result = new DownloadAnalysis
                {
                    Url = url,
                    FileSize = response.Content.Headers.ContentLength ?? 0,
                    MimeType = contentType,
                    AcceptRanges = response.Headers.Contains("Accept-Ranges"),
                    IsValid = !isHtml,
                    FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url
                };

                string? fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                if (string.IsNullOrEmpty(fileName))
                    fileName = ExtractFilenameFromUrl(url);
                result.Filename = fileName ?? "download";

                return result;
            }
            catch
            {
                return new DownloadAnalysis { Url = url, IsValid = false };
            }
        }

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
                using var request = new HttpRequestMessage(HttpMethod.Get, task.Url);

                ApplyBrowserFingerprint(request, session);

                if (session?.AcceptRanges == "bytes" && task.DownloadedSize > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(task.DownloadedSize, null);
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
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
                        task.ErrorMessage = "Server returned HTML instead of file (login/error page)";
                        task.Speed = "0 B/s";
                        return;
                    }
                }

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                if (totalBytes > 0) task.TotalSize = totalBytes;

                var finalFileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                if (!string.IsNullOrEmpty(finalFileName) && finalFileName != task.Filename)
                {
                    task.Filename = finalFileName;
                }

                var saveDir = Path.GetDirectoryName(task.SavePath);
                if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);

                using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var fileStream = new FileStream(
                    task.SavePath,
                    task.DownloadedSize > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    8192);

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

        private void ApplyBrowserFingerprint(HttpRequestMessage request, DownloadSession? session)
        {
            string userAgent = session?.UserAgent
                ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36";

            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

            request.Headers.Remove("Accept");
            request.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");

            request.Headers.Remove("Accept-Language");
            request.Headers.TryAddWithoutValidation("Accept-Language",
                "en-US,en;q=0.9");

            request.Headers.Remove("Accept-Encoding");
            request.Headers.TryAddWithoutValidation("Accept-Encoding",
                "gzip, deflate, br");

            request.Headers.Remove("Cache-Control");
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

            request.Headers.Remove("Pragma");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");

            request.Headers.Remove("Upgrade-Insecure-Requests");
            request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

            if (!string.IsNullOrEmpty(session?.Referrer))
            {
                request.Headers.Remove("Referer");
                request.Headers.TryAddWithoutValidation("Referer", session.Referrer);
            }

            if (!string.IsNullOrEmpty(session?.Origin))
            {
                request.Headers.Remove("Origin");
                request.Headers.TryAddWithoutValidation("Origin", session.Origin);
            }

            request.Headers.Remove("Sec-Fetch-Dest");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");

            request.Headers.Remove("Sec-Fetch-Mode");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");

            request.Headers.Remove("Sec-Fetch-Site");
            string fetchSite = "none";
            if (!string.IsNullOrEmpty(session?.Referrer))
            {
                try
                {
                    var reqHost = new Uri(request.RequestUri!.ToString()).Host;
                    var refHost = new Uri(session.Referrer).Host;
                    fetchSite = reqHost == refHost ? "same-origin" : "cross-site";
                }
                catch { fetchSite = "cross-site"; }
            }
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", fetchSite);

            request.Headers.Remove("Sec-Fetch-User");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");

            if (session?.ClientHints != null)
            {
                foreach (var kv in session.ClientHints)
                {
                    if (!request.Headers.Contains(kv.Key))
                        request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }

            if (session?.Cookies != null && session.Cookies.Count > 0)
            {
                string cookieHeader = string.Join("; ", session.Cookies.Select(c => $"{c.Name}={c.Value}"));
                request.Headers.Remove("Cookie");
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }

            if (session?.Headers != null)
            {
                string[] skipHeaders = {
                    "user-agent", "accept", "accept-language", "accept-encoding",
                    "cache-control", "pragma", "upgrade-insecure-requests",
                    "referer", "origin", "cookie",
                    "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site", "sec-fetch-user",
                    "connection", "content-length", "content-type", "host"
                };

                foreach (var kv in session.Headers)
                {
                    string key = kv.Key.ToLowerInvariant();
                    if (skipHeaders.Contains(key)) continue;
                    if (request.Headers.Contains(kv.Key)) continue;
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }

            if (!string.IsNullOrEmpty(session?.ETag))
            {
                request.Headers.Remove("If-None-Match");
                request.Headers.TryAddWithoutValidation("If-None-Match", session.ETag);
            }

            if (!string.IsNullOrEmpty(session?.LastModified))
            {
                request.Headers.Remove("If-Modified-Since");
                request.Headers.TryAddWithoutValidation("If-Modified-Since", session.LastModified);
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
            if (task.State == DownloadState.Downloading)
            {
                task.Cts?.Cancel();
                task.State = DownloadState.Paused;
                task.Speed = "0 B/s";
            }
        }

        public void ResumeDownload(DownloadTask task, DownloadSession? session = null)
        {
            if (task.State == DownloadState.Paused)
            {
                _ = StartDownloadAsync(task, session);
            }
        }

        public void CancelDownload(DownloadTask task)
        {
            task.Cts?.Cancel();
            task.State = DownloadState.Idle;
            task.Speed = "0 B/s";
            task.Progress = 0;
            try { if (File.Exists(task.SavePath)) File.Delete(task.SavePath); } catch { }
        }

        public static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "0 B/s";
            string[] sizes = ["B/s", "KB/s", "MB/s", "GB/s"];
            int i = 0;
            double size = bytesPerSecond;
            while (size >= 1024 && i < sizes.Length - 1) { size /= 1024; i++; }
            return $"{size:F1} {sizes[i]}";
        }

        public static string FormatTime(double seconds)
        {
            if (seconds <= 0 || double.IsInfinity(seconds)) return "--:--";
            int mins = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            if (mins >= 60) { int hrs = mins / 60; mins %= 60; return $"{hrs:D2}:{mins:D2}:{secs:D2}"; }
            return $"{mins:D2}:{secs:D2}";
        }
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
    }

    public class DownloadProgress
    {
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double Speed { get; set; }
        public double Progress { get; set; }
    }
}
