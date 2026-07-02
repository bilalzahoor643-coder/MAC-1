using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAC_1.Service.Database;
using MAC_1.Service.Models;

namespace MAC_1.Service.Core
{
    public class DownloadService
    {
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDownloads = new();
        private readonly ConcurrentDictionary<string, DownloadEngine> _engines = new();
        private readonly string _logFile;
        private readonly object _pipeLock = new();

        public event EventHandler<DownloadEventArgs>? DownloadEvent;

        public DownloadService()
        {
            _logFile = System.IO.Path.Combine(AppContext.BaseDirectory, "service-debug.log");
        }

        public void Initialize()
        {
            Log("[DownloadService] Initialized");
        }

        public bool IsDownloadActive(string sessionId)
        {
            return _activeDownloads.ContainsKey(sessionId);
        }

        // ═══════════════════════════════════════════════════
        // START DOWNLOAD
        // ═══════════════════════════════════════════════════

        public async Task StartDownloadAsync(
            string sessionId,
            string url,
            string savePath,
            Dictionary<string, string>? headers = null,
            List<CookieData>? cookies = null,
            string? userAgent = null,
            string? referer = null,
            List<RawHeader>? browserRawHeaders = null,
            string sessionMethod = "GET",
            object? postData = null,
            long knownFileSize = 0)
        {
            if (_activeDownloads.ContainsKey(sessionId))
            {
                Log($"[DownloadService] Download already active: {sessionId}");
                return;
            }

            var cts = new CancellationTokenSource();
            _activeDownloads[sessionId] = cts;

            var engine = new DownloadEngine();
            _engines[sessionId] = engine;

            engine.DownloadEvent += (sender, args) => HandleEngineEvent(sessionId, args);

            Log($"[PASS] Stage 9: Engine started — sessionId={sessionId}, url={url}");

            // Run on background thread
            _ = Task.Run(async () =>
            {
                try
                {
                    await engine.StartDownloadAsync(sessionId, url, savePath,
                        headers, cookies, userAgent, referer, cts.Token, browserRawHeaders,
                        sessionMethod, postData, knownFileSize);
                }
                catch (Exception ex)
                {
                    Log($"[DownloadService] Engine crashed: {ex.Message}");
                    HandleEngineEvent(sessionId, new DownloadEventArgs
                    {
                        EventType = DownloadEventType.Failed,
                        SessionId = sessionId,
                        Url = url,
                        State = DownloadState.Failed,
                        ErrorMessage = ex.Message,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });
                }
                finally
                {
                    _activeDownloads.TryRemove(sessionId, out _);
                    _engines.TryRemove(sessionId, out _);
                    cts.Dispose();
                }
            });
        }

        // ═══════════════════════════════════════════════════
        // PAUSE / RESUME / CANCEL
        // ═══════════════════════════════════════════════════

        public void PauseDownload(string sessionId)
        {
            if (_activeDownloads.TryRemove(sessionId, out var cts))
            {
                cts.Cancel();
                Log($"[DownloadService] Paused: {sessionId}");
                _engines.TryRemove(sessionId, out _);
            }
        }

        public void ResumeDownload(string sessionId, string url, string savePath,
            Dictionary<string, string>? headers = null,
            List<CookieData>? cookies = null,
            string? userAgent = null,
            string? referer = null,
            List<RawHeader>? browserRawHeaders = null,
            string sessionMethod = "GET",
            object? postData = null)
        {
            if (!_activeDownloads.ContainsKey(sessionId))
            {
                Log($"[DownloadService] Resuming: {sessionId}");
                _ = StartDownloadAsync(sessionId, url, savePath, headers, cookies, userAgent, referer, browserRawHeaders,
                    sessionMethod, postData);
            }
        }

        public void CancelDownload(string sessionId)
        {
            if (_activeDownloads.TryRemove(sessionId, out var cts))
            {
                cts.Cancel();
                Log($"[DownloadService] Cancelled: {sessionId}");
                _engines.TryRemove(sessionId, out _);

                // Delete partial file
                try
                {
                    var entity = DatabaseService.Instance.Sessions.GetById(sessionId);
                    if (entity != null && !string.IsNullOrEmpty(entity.SavePath) && System.IO.File.Exists(entity.SavePath))
                        System.IO.File.Delete(entity.SavePath);
                }
                catch { }
            }
        }

        public int ActiveDownloadCount => _activeDownloads.Count;

        // ═══════════════════════════════════════════════════
        // ENGINE EVENT HANDLER — DB + PIPE + External Event
        // ═══════════════════════════════════════════════════

        private void HandleEngineEvent(string sessionId, DownloadEventArgs args)
        {
            args.SessionId = sessionId;

            Log($"[DownloadService] Engine event: {args.EventType} | state={args.State} | progress={args.Progress:F1}% | url={args.Url} | sessionId={sessionId}");

            // Update SQLite
            UpdateDatabase(sessionId, args);

            // Forward to pipe/WPF
            DownloadEvent?.Invoke(this, args);
        }

        private void UpdateDatabase(string sessionId, DownloadEventArgs args)
        {
            try
            {
                switch (args.EventType)
                {
                    case DownloadEventType.Started:
                        DatabaseService.Instance.UpdateStatusTransactional(sessionId, "starting");
                        break;

                    case DownloadEventType.MetadataReceived:
                        DatabaseService.Instance.UpdateMetadataTransactional(sessionId,
                            args.FileSize, args.ResumeSupported, args.HttpStatusCode, args.FinalUrl);
                        break;

                    case DownloadEventType.ProgressChanged:
                        DatabaseService.Instance.UpdateProgressTransactional(sessionId,
                            args.BytesDownloaded, args.FileSize, args.Progress,
                            args.Speed, args.AverageSpeed, args.ETA,
                            args.SavePath);
                        break;

                    case DownloadEventType.Completed:
                        DatabaseService.Instance.UpdateStatusTransactional(sessionId, "completed",
                            args.SavePath, args.BytesDownloaded, args.FileSize, 100);
                        break;

                    case DownloadEventType.Failed:
                        DatabaseService.Instance.UpdateStatusTransactional(sessionId, "failed",
                            errorMessage: args.ErrorMessage);
                        break;

                    case DownloadEventType.Cancelled:
                        DatabaseService.Instance.UpdateStatusTransactional(sessionId, "cancelled");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"[DownloadService] DB update failed: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Console.WriteLine(line);
            try { System.IO.File.AppendAllText(_logFile, line + "\n"); } catch { }
        }
    }
}
