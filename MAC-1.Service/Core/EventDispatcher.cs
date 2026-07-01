using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using MAC_1.Service.Models;

namespace MAC_1.Service.Core
{
    public class EventDispatcher
    {
        private readonly EventQueue _queue;
        private readonly Listeners.PipeServer? _pipeServer;

        public EventDispatcher(EventQueue queue, Listeners.PipeServer? pipeServer = null)
        {
            _queue = queue;
            _pipeServer = pipeServer;
        }

        public void SetPipeServer(Listeners.PipeServer pipeServer)
        {
            _pipeServerInternal = pipeServer;
        }

        private Listeners.PipeServer? _pipeServerInternal;

        private Listeners.PipeServer? Pipe => _pipeServer ?? _pipeServerInternal;

        public async Task DispatchAsync(DownloadSession session)
        {
            _queue.Enqueue(session);

            var pipe = Pipe;
            if (pipe != null && pipe.IsClientConnected)
            {
                await pipe.SendDownloadEventAsync(session);
                Log($"Dispatched to WPF: {session.Filename}");
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
            Console.WriteLine($"[Dispatcher] {message}");
        }
    }
}
