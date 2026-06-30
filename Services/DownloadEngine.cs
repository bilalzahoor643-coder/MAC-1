using System;
using System.IO;
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

        private readonly HttpClient _httpClient;

        private DownloadEngine()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MAC-1 Download Manager/1.0");
        }

        public async Task<DownloadAnalysis> AnalyzeUrlAsync(string url)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                request.Headers.Add("User-Agent", "MAC-1 Download Manager/1.0");

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var result = new DownloadAnalysis
                {
                    Url = url,
                    FileSize = response.Content.Headers.ContentLength ?? 0,
                    MimeType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                    AcceptRanges = response.Headers.Contains("Accept-Ranges"),
                    IsValid = true
                };

                // Extract filename from URL or Content-Disposition
                string? fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                if (string.IsNullOrEmpty(fileName))
                {
                    var uri = new Uri(url);
                    fileName = Path.GetFileName(uri.LocalPath);
                }
                if (string.IsNullOrEmpty(fileName)) fileName = "download";
                result.Filename = fileName;

                return result;
            }
            catch
            {
                return new DownloadAnalysis { Url = url, IsValid = false };
            }
        }

        public async Task StartDownloadAsync(DownloadTask task, IProgress<DownloadProgress>? progress = null, CancellationToken? externalToken = null)
        {
            var cts = externalToken.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(externalToken.Value)
                : new CancellationTokenSource();
            task.Cts = cts;
            task.State = DownloadState.Downloading;
            task.StartTime = DateTime.Now;

            try
            {
                using var response = await _httpClient.GetAsync(task.Url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                if (totalBytes > 0) task.TotalSize = totalBytes;

                var saveDir = Path.GetDirectoryName(task.SavePath);
                if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);

                using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var fileStream = new FileStream(task.SavePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);

                var buffer = new byte[8192];
                long totalRead = 0;
                var lastProgressTime = DateTime.UtcNow;
                long lastBytesRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
                    totalRead += bytesRead;
                    task.DownloadedSize = totalRead;

                    // Update progress
                    if (totalBytes > 0)
                        task.Progress = (double)totalRead / totalBytes * 100;

                    // Calculate speed every 500ms
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

        public void PauseDownload(DownloadTask task)
        {
            if (task.State == DownloadState.Downloading)
            {
                task.Cts?.Cancel();
                task.State = DownloadState.Paused;
                task.Speed = "0 B/s";
            }
        }

        public void ResumeDownload(DownloadTask task)
        {
            if (task.State == DownloadState.Paused)
            {
                _ = StartDownloadAsync(task);
            }
        }

        public void CancelDownload(DownloadTask task)
        {
            task.Cts?.Cancel();
            task.State = DownloadState.Idle;
            task.Speed = "0 B/s";
            task.Progress = 0;

            // Delete partial file
            try
            {
                if (File.Exists(task.SavePath))
                    File.Delete(task.SavePath);
            }
            catch { }
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
            if (mins >= 60)
            {
                int hrs = mins / 60;
                mins %= 60;
                return $"{hrs:D2}:{mins:D2}:{secs:D2}";
            }
            return $"{mins:D2}:{secs:D2}";
        }
    }

    public class DownloadAnalysis
    {
        public string Url { get; set; } = string.Empty;
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
