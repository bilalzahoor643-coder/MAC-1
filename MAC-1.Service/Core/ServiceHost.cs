using System;
using System.Threading;
using System.Threading.Tasks;
using MAC_1.Service.Core;
using MAC_1.Service.Listeners;
using MAC_1.Service.Models;

namespace MAC_1.Service.Core
{
    public class ServiceHost
    {
        private readonly EventQueue _eventQueue;
        private readonly EventDispatcher _eventDispatcher;
        private readonly HttpListener _httpListener;
        private readonly PipeServer _pipeServer;
        private readonly DownloadService _downloadService;

        public ServiceHost()
        {
            _eventQueue = new EventQueue();
            _pipeServer = new PipeServer();
            _eventDispatcher = new EventDispatcher(_eventQueue, _pipeServer);
            _downloadService = new DownloadService();
            _httpListener = new HttpListener(_eventDispatcher, _downloadService);
        }

        public void Start()
        {
            Log("Starting MAC-1 Background Service...");

            // Initialize database
            try
            {
                Database.DatabaseService.Instance.Initialize();
                Log("Database initialized successfully");
            }
            catch (Exception ex)
            {
                Log($"Database init FAILED: {ex.Message}");
            }

            // Initialize download service
            _downloadService.Initialize();
            _downloadService.DownloadEvent += OnDownloadEvent;
            Log("Download service initialized");

            _pipeServer.ClientConnected += async () =>
            {
                Log("WPF UI connected — flushing pending events...");
                await FlushPendingEventsAsync();
            };

            _pipeServer.ClientDisconnected += () => Log("WPF UI disconnected");

            _pipeServer.Start();
            _httpListener.Start();

            Log("Service started successfully");
            Log($"  Database: {Database.DatabaseService.Instance.DbPath}");
            Log("  HTTP server: http://127.0.0.1:57575/");
            Log("  Pipe server: MAC-1-Service");
            Log("  Download engine: ready");
            Log("  Queue: ready");
        }

        public void Stop()
        {
            Log("Stopping MAC-1 Background Service...");

            _downloadService.DownloadEvent -= OnDownloadEvent;
            _httpListener.Stop();
            _pipeServer.Stop();

            Log("Service stopped");
        }

        private async void OnDownloadEvent(object? sender, DownloadEventArgs args)
        {
            try
            {
                bool connected = _pipeServer.IsClientConnected;
                Log($"OnDownloadEvent: type={args.EventType} state={args.State} progress={args.Progress:F1}% pipeConnected={connected}");

                if (connected)
                {
                    await _pipeServer.SendEngineEventAsync(args);
                    Log($"OnDownloadEvent: Sent to WPF OK");
                }
                else
                {
                    Log($"OnDownloadEvent: WPF not connected, event dropped");
                }
            }
            catch (Exception ex)
            {
                Log($"Forward engine event FAILED: {ex.Message}");
            }
        }

        private async Task FlushPendingEventsAsync()
        {
            var pending = _eventQueue.GetAllPending();
            if (pending.Count == 0)
            {
                Log("No pending events to flush");
                return;
            }

            Log($"Flushing {pending.Count} pending events to WPF...");

            foreach (var session in pending)
            {
                try
                {
                    await _pipeServer.SendDownloadEventAsync(session);
                    _eventQueue.MarkDispatched();
                    Log($"Flushed: {session.Filename}");
                }
                catch (Exception ex)
                {
                    Log($"Flush FAILED for {session.Filename}: {ex.Message}");
                }
            }

            Log($"Flush complete. Pending: {_eventQueue.PendingCount}, Dispatched: {_eventQueue.DispatchedCount}");
        }

        public EventDispatcher Dispatcher => _eventDispatcher;
        public EventQueue Queue => _eventQueue;

        private void Log(string message)
        {
            Console.WriteLine($"[ServiceHost] {message}");
        }
    }
}
