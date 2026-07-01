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

        public event Action<DownloadSession>? DownloadSessionReceived;
        public event Action<string, long>? SizeUpdateReceived;
        public event Action? Connected;
        public event Action? Disconnected;

        public bool IsConnected => _isConnected;

        public void Start()
        {
            if (_isRunning) return;
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
            while (!ct.IsCancellationRequested && _isRunning)
            {
                try
                {
                    _client = new NamedPipeClientStream(
                        ".",
                        PipeName,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous);

                    await _client.ConnectAsync(3000, ct);

                    _isConnected = true;
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
                    try { await Task.Delay(3000, ct); } catch { break; }
                }
            }
        }

        private async Task MonitorAsync(NamedPipeClientStream pipe, CancellationToken ct)
        {
            try
            {
                using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);

                _ = SendHeartbeatsAsync(writer, ct);

                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    try
                    {
                        var msg = await ReadMessageAsync(reader);
                        if (msg == null) break;

                        await ProcessMessageAsync(msg);
                    }
                    catch (EndOfStreamException) { break; }
                    catch (IOException) { break; }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
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
                        var session = JsonSerializer.Deserialize<DownloadSession>(msg.Data,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (session != null && !string.IsNullOrEmpty(session.Url))
                        {
                            DownloadSessionReceived?.Invoke(session);
                            Log($"Download event: {session.Filename}");
                        }
                    }
                    catch (Exception ex) { Log($"Deserialize error: {ex.Message}"); }
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
                    return JsonSerializer.Deserialize<PipeMessage>(Encoding.UTF8.GetString(data));
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
            Console.WriteLine($"[PipeClient] {message}");
        }

        private class SizeUpdateRpc
        {
            public string? Url { get; set; }
            public long FileSize { get; set; }
        }
    }

    public class PipeMessage
    {
        public string Type { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
    }
}
