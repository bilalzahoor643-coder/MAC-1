using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using MAC_1.Service.Models;

namespace MAC_1.Service.Core
{
    public class EventQueue
    {
        private readonly ConcurrentQueue<DownloadSession> _queue = new();
        private readonly List<DownloadSession> _dispatched = new();
        private readonly object _lock = new();

        public int PendingCount => _queue.Count;
        public int DispatchedCount
        {
            get { lock (_lock) return _dispatched.Count; }
        }

        public void Enqueue(DownloadSession session)
        {
            _queue.Enqueue(session);
            Log($"Queued: {session.Filename} ({session.FileSize} bytes)");
        }

        public DownloadSession? Dequeue()
        {
            if (_queue.TryDequeue(out var session))
            {
                lock (_lock) { _dispatched.Add(session); }
                return session;
            }
            return null;
        }

        public List<DownloadSession> GetAllPending()
        {
            var list = new List<DownloadSession>();
            foreach (var item in _queue) list.Add(item);
            return list;
        }

        public void UpdateSize(string url, long fileSize)
        {
            lock (_lock)
            {
                foreach (var item in _dispatched)
                {
                    if (item.Url == url && item.FileSize == 0)
                    {
                        item.FileSize = fileSize;
                        Log($"Size updated: {item.Filename} → {fileSize} bytes");
                        break;
                    }
                }
            }
        }

        private void Log(string message)
        {
            Console.WriteLine($"[EventQueue] {message}");
        }
    }
}
