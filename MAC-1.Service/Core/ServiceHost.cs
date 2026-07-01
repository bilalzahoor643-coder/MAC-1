using System;
using System.Threading;
using System.Threading.Tasks;
using MAC_1.Service.Core;
using MAC_1.Service.Listeners;

namespace MAC_1.Service.Core
{
    public class ServiceHost
    {
        private readonly EventQueue _eventQueue;
        private readonly EventDispatcher _eventDispatcher;
        private readonly HttpListener _httpListener;
        private readonly PipeServer _pipeServer;

        public ServiceHost()
        {
            _eventQueue = new EventQueue();
            _pipeServer = new PipeServer();
            _eventDispatcher = new EventDispatcher(_eventQueue, _pipeServer);
            _httpListener = new HttpListener(_eventDispatcher);
        }

        public void Start()
        {
            Log("Starting MAC-1 Background Service...");

            _pipeServer.ClientConnected += () => Log("WPF UI connected");
            _pipeServer.ClientDisconnected += () => Log("WPF UI disconnected");

            _pipeServer.Start();
            _httpListener.Start();

            Log("Service started successfully");
            Log("  HTTP server: http://127.0.0.1:57575/");
            Log("  Pipe server: MAC-1-Service");
            Log("  Queue: ready");
        }

        public void Stop()
        {
            Log("Stopping MAC-1 Background Service...");

            _httpListener.Stop();
            _pipeServer.Stop();

            Log("Service stopped");
        }

        public EventDispatcher Dispatcher => _eventDispatcher;
        public EventQueue Queue => _eventQueue;

        private void Log(string message)
        {
            Console.WriteLine($"[ServiceHost] {message}");
        }
    }
}
