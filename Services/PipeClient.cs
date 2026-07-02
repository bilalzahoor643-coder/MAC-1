using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class PipeClient
    {
        private const string PipeName = "MAC-1-Service";
        private NamedPipeClientStream? _client;
        private CancellationTokenSource? _cts;
        private bool _isConnected;
        private bool _isRunning;
        private static readonly string _logFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MAC-1", "wpf-pipe.log");

        public event Action<DownloadSession>? DownloadSessionReceived;
        public event Action<string, long>? SizeUpdateReceived;
        public event Action<DownloadEngineEvent>? EngineEventReceived;
        public event Action? Connected;
        public event Action? Disconnected;

        public bool IsConnected => _isConnected;

        public void Start()
        {
            if (_isRunning) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                File.WriteAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] PipeClient starting\n");
            }
            catch { }
            _cts = new CancellationTokenSource();
            _isRunning = true;
            _ = ConnectAsync(_cts.Token);
        }

        public void Stop()
        {
            _isRunning = false;
            _cts?.Cancel();
            try { _client?.Dispose(); } catch { }
            _client = null;
            _isConnected = false;
        }

        private async Task ConnectAsync(CancellationToken ct)
        {
            int retryMs = 300;
            const int maxRetryMs = 5000;

            while (!ct.IsCancellationRequested && _isRunning)
            {
                try
                {
                    _client = new NamedPipeClientStream(
                        ".",
                        PipeName,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous);

                    await _client.ConnectAsync(2000, ct);

                    _isConnected = true;
                    retryMs = 300;
                    Connected?.Invoke();
                    Log("Connected to MAC-1 Service");

                    await MonitorAsync(_client, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (TimeoutException) { }
                catch (IOException) { }
                catch (Exception ex)
                {
                    Log($"Connection error: {ex.Message}");
                }

                _isConnected = false;
                Disconnected?.Invoke();

                if (!ct.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        await Task.Delay(retryMs, ct);
                        retryMs = Math.Min(retryMs * 2, maxRetryMs);
                    }
                    catch { break; }
                }
            }
        }

        private async Task MonitorAsync(NamedPipeClientStream pipe, CancellationToken ct)
        {
            try
            {
                using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);

                await WriteMessageAsync(writer, new PipeMessage { Type = "ready" });
                Log("Sent 'ready' to service");

                _ = SendHeartbeatsAsync(writer, ct);

                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    try
                    {
                        var msg = await ReadMessageAsync(reader);
                        if (msg == null)
                        {
                            Log("ReadMessage returned null — stream closed");
                            break;
                        }

                        Log($"Received message: type={msg.Type}, dataLen={msg.Data?.Length ?? 0}");
                        await ProcessMessageAsync(msg);
                    }
                    catch (EndOfStreamException) { Log("EndOfStream"); break; }
                    catch (IOException ex) { Log($"IOException: {ex.Message}"); break; }
                }
            }
            catch (OperationCanceledException) { Log("Monitor cancelled"); }
            catch (Exception ex) { Log($"Monitor error: {ex.Message}"); }
        }

        private async Task SendHeartbeatsAsync(BinaryWriter writer, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(15000, ct);
                    await WriteMessageAsync(writer, new PipeMessage { Type = "heartbeat" });
                }
                catch { break; }
            }
        }

        private async Task ProcessMessageAsync(PipeMessage msg)
        {
            switch (msg.Type)
            {
                case "download_event":
                    try
                    {
                        Log($"Deserializing download_event, data preview: {(msg.Data?.Length > 200 ? msg.Data[..200] + "..." : msg.Data)}");
                        var session = JsonSerializer.Deserialize<DownloadSession>(msg.Data,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (session != null && !string.IsNullOrEmpty(session.Url))
                        {
                            Log($"[PASS] Stage 5: Session received via pipe: filename={session.Filename}, sessionId={session.SessionId}, url={session.Url}");
                            DownloadSessionReceived?.Invoke(session);
                            Log("DownloadSessionReceived event fired");
                        }
                        else
                        {
                            Log($"[FAIL] Stage 5: Deserialize returned null or empty URL. session={session != null}, url={session?.Url}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[FAIL] Stage 5: Deserialize ERROR: {ex.Message}");
                        Log($"Stack: {ex.StackTrace}");
                    }
                    break;

                case "size_update":
                    try
                    {
                        var update = JsonSerializer.Deserialize<SizeUpdateRpc>(msg.Data,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (update != null && !string.IsNullOrEmpty(update.Url) && update.FileSize > 0)
                        {
                            SizeUpdateReceived?.Invoke(update.Url, update.FileSize);
                        }
                    }
                    catch { }
                    break;

                case "heartbeat_ack":
                    break;

                case "engine_event":
                    try
                    {
                        var engineEvent = JsonSerializer.Deserialize<DownloadEngineEvent>(msg.Data,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (engineEvent != null)
                        {
                            Log($"[PASS] Stage 11: Engine event received: type={engineEvent.EventType} state={engineEvent.State} progress={engineEvent.Progress:F1}% sessionId={engineEvent.SessionId}");
                            EngineEventReceived?.Invoke(engineEvent);
                        }
                        else
                        {
                            Log($"[FAIL] Stage 11: Engine event deserialize returned null");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[FAIL] Stage 11: Engine event deserialize ERROR: {ex.Message}");
                    }
                    break;

                default:
                    Log($"Unknown message type: {msg.Type}");
                    break;
            }
        }

        public async Task SendAsync(PipeMessage msg)
        {
            if (_client == null || !_client.IsConnected) return;

            try
            {
                using var writer = new BinaryWriter(_client, Encoding.UTF8, leaveOpen: true);
                await WriteMessageAsync(writer, msg);
            }
            catch { }
        }

        private async Task<PipeMessage?> ReadMessageAsync(BinaryReader reader)
        {
            return await Task.Run(() =>
            {
                try
                {
                    int length = reader.ReadInt32();
                    if (length <= 0 || length > 10 * 1024 * 1024) return null;
                    byte[] data = reader.ReadBytes(length);
                    return JsonSerializer.Deserialize<PipeMessage>(Encoding.UTF8.GetString(data),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { return null; }
            });
        }

        private async Task WriteMessageAsync(BinaryWriter writer, PipeMessage msg)
        {
            await Task.Run(() =>
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));
                    writer.Write(data.Length);
                    writer.Write(data);
                    writer.Flush();
                }
                catch { }
            });
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            try { File.AppendAllText(_logFile, line + "\n"); } catch { }
        }

        private class SizeUpdateRpc
        {
            public string? Url { get; set; }
            public long FileSize { get; set; }
        }
    }

    public class PipeMessage
    {
        public int Version { get; set; } = 1;
        public string Type { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
    }
}
