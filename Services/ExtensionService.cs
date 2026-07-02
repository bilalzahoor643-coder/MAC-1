using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class ExtensionService
    {
        private static readonly Lazy<ExtensionService> _instance = new(() => new ExtensionService());
        public static ExtensionService Instance => _instance.Value;

        private readonly PipeClient _pipeClient;

        public event Action<DownloadSession>? DownloadSessionReceived;
        public event Action<string>? StatusChanged;
        public event Action<string, long>? SizeUpdateReceived;
        public event Action<DownloadEngineEvent>? EngineEventReceived;

        public bool IsRunning => _pipeClient.IsConnected;
        public int Port => 57575;

        private ExtensionService()
        {
            _pipeClient = new PipeClient();

            _pipeClient.Connected += () =>
                StatusChanged?.Invoke("Connected to MAC-1 Background Service");

            _pipeClient.Disconnected += () =>
                StatusChanged?.Invoke("Disconnected from MAC-1 Background Service");

            _pipeClient.DownloadSessionReceived += session =>
                DownloadSessionReceived?.Invoke(session);

            _pipeClient.SizeUpdateReceived += (url, size) =>
                SizeUpdateReceived?.Invoke(url, size);

            _pipeClient.EngineEventReceived += evt =>
                EngineEventReceived?.Invoke(evt);
        }

        public void Start()
        {
            _pipeClient.Start();
            StatusChanged?.Invoke("Connecting to background service...");
        }

        public void Stop()
        {
            _pipeClient.Stop();
            StatusChanged?.Invoke("Disconnected from background service");
        }
    }

    public class ExtensionDownloadRequest
    {
        public string url { get; set; } = string.Empty;
        public string? filename { get; set; }
        public long? fileSize { get; set; }
        public string? referrer { get; set; }
        public string? mimeType { get; set; }
        public string? savePath { get; set; }
        public string? userAgent { get; set; }
        public object? cookies { get; set; }
        public object? headers { get; set; }
    }

    public class SizeUpdateRequest
    {
        public string? Url { get; set; }
        public long FileSize { get; set; }
    }
}
