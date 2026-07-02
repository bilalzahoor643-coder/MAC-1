using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MAC_1.Service.Models;

namespace MAC_1.Service.Core
{
    public class EventDispatcher
    {
        private readonly EventQueue _queue;
        private readonly Listeners.PipeServer? _pipeServer;
        private readonly string _logFile;

        public EventDispatcher(EventQueue queue, Listeners.PipeServer? pipeServer = null)
        {
            _queue = queue;
            _pipeServer = pipeServer;
            _logFile = Path.Combine(AppContext.BaseDirectory, "service-debug.log");
            File.WriteAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] Dispatcher initialized\n");
        }

        private Listeners.PipeServer? Pipe => _pipeServer;

        public async Task DispatchAsync(DownloadSession session)
        {
            _queue.Enqueue(session);

            // Save to SQLite BEFORE dispatching — uses same SessionId throughout
            string sessionId = string.Empty;
            try
            {
                var entity = Database.DatabaseService.Instance.SaveSessionTransactional(session);
                sessionId = entity.SessionId;
                session.SessionId = sessionId;
                Log($"Saved to DB: {session.Filename} (id={sessionId})");
                Log($"DB Entity: RawHeadersJson.Length={entity.RawHeadersJson?.Length ?? 0}, RawCookiesJson.Length={entity.RawCookiesJson?.Length ?? 0}, RawBrowserHeadersJson.Length={entity.RawBrowserHeadersJson?.Length ?? 0}, RawPostDataJson.Length={entity.RawPostDataJson?.Length ?? 0}");
            }
            catch (Exception ex)
            {
                Log($"DB save FAILED (non-fatal): {ex.Message}");
            }

            var pipe = Pipe;
            bool connected = pipe?.IsClientConnected ?? false;
            Log($"Dispatch: filename={session.Filename}, pipe={pipe != null}, connected={connected}");

            if (pipe != null && connected)
            {
                try
                {
                    await pipe.SendDownloadEventAsync(session);
                    _queue.MarkDispatched();

                    // Update status — same SessionId, never changes
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        Database.DatabaseService.Instance.UpdateStatusTransactional(sessionId, "dispatched");
                    }

                    Log($"Dispatched to WPF: {session.Filename} (id={sessionId})");
                }
                catch (Exception ex)
                {
                    Log($"Send FAILED: {ex.Message}");
                }
            }
            else
            {
                Log($"WPF not connected, queued: {session.Filename} (id={sessionId})");
            }
        }

        public async Task DispatchSizeUpdateAsync(string url, long fileSize)
        {
            _queue.UpdateSize(url, fileSize);

            var pipe = Pipe;
            if (pipe != null && pipe.IsClientConnected)
            {
                await pipe.SendSizeUpdateAsync(url, fileSize);
            }
        }

        public List<DownloadSession> GetPendingSessions()
        {
            return _queue.GetAllPending();
        }

        public object GetStats()
        {
            return new
            {
                pending = _queue.PendingCount,
                dispatched = _queue.DispatchedCount
            };
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Console.WriteLine(line);
            try { File.AppendAllText(_logFile, line + "\n"); } catch { }
        }
    }
}
