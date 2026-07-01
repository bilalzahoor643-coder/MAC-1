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

        private Listeners.PipeServer? _pipeServerInternal;
        private Listeners.PipeServer? Pipe => _pipeServer ?? _pipeServerInternal;

        public async Task DispatchAsync(DownloadSession session)
        {
            _queue.Enqueue(session);

            var pipe = Pipe;
            bool connected = pipe?.IsClientConnected ?? false;
            Log($"Dispatch: filename={session.Filename}, pipe={pipe != null}, connected={connected}");

            if (pipe != null && connected)
            {
                try
                {
                    await pipe.SendDownloadEventAsync(session);
                    _queue.MarkDispatched();
                    Log($"Dispatched to WPF: {session.Filename}");
                }
                catch (Exception ex)
                {
                    Log($"Send FAILED: {ex.Message}");
                }
            }
            else
            {
                Log($"WPF not connected, queued: {session.Filename}");
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
