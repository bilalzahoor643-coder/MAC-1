using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAC_1.Service.Models;

namespace MAC_1.Service.Listeners
{
    public class PipeServer
    {
        private const string PipeName = "MAC-1-Service";
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private NamedPipeServerStream? _connectedPipe;
        private readonly object _pipeLock = new();

        public event Action? ClientConnected;
        public event Action? ClientDisconnected;
        public bool IsClientConnected
        {
            get
            {
                lock (_pipeLock) return _connectedPipe?.IsConnected == true;
            }
        }

        public void Start()
        {
            if (_isRunning) return;
            _cts = new CancellationTokenSource();
            _isRunning = true;
            _ = AcceptConnectionsAsync(_cts.Token);
            Log("Pipe server started on: " + PipeName);
        }

        public void Stop()
        {
            _isRunning = false;
            _cts?.Cancel();
            lock (_pipeLock)
            {
                try { _connectedPipe?.Dispose(); } catch { }
                _connectedPipe = null;
            }
            Log("Pipe server stopped");
        }

        private async Task AcceptConnectionsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);
                    Log("WPF client connected");

                    lock (_pipeLock)
                    {
                        try { _connectedPipe?.Dispose(); } catch { }
                        _connectedPipe = server;
                    }

                    ClientConnected?.Invoke();
                    _ = MonitorClientAsync(server, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { break; }
                catch (Exception ex)
                {
                    Log($"Accept error: {ex.Message}");
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task MonitorClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
        {
            try
            {
                using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);

                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    try
                    {
                        var msg = await ReadMessageAsync(reader);
                        if (msg == null) break;

                        if (msg.Type == "heartbeat")
                        {
                            await WriteMessageAsync(writer, new PipeMessage { Type = "heartbeat_ack" });
                        }
                    }
                    catch (EndOfStreamException) { break; }
                    catch (IOException) { break; }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                lock (_pipeLock)
                {
                    if (_connectedPipe == pipe)
                    {
                        try { pipe.Dispose(); } catch { }
                        _connectedPipe = null;
                    }
                }
                ClientDisconnected?.Invoke();
                Log("WPF client disconnected");
            }
        }

        public async Task SendToClientAsync(PipeMessage msg)
        {
            NamedPipeServerStream? pipe;
            lock (_pipeLock) { pipe = _connectedPipe; }

            if (pipe == null || !pipe.IsConnected) return;

            try
            {
                using var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);
                await WriteMessageAsync(writer, msg);
            }
            catch { }
        }

        public async Task SendDownloadEventAsync(DownloadSession session)
        {
            await SendToClientAsync(new PipeMessage
            {
                Type = "download_event",
                Data = JsonSerializer.Serialize(session)
            });
        }

        public async Task SendSizeUpdateAsync(string url, long fileSize)
        {
            await SendToClientAsync(new PipeMessage
            {
                Type = "size_update",
                Data = JsonSerializer.Serialize(new SizeUpdateRequest { Url = url, FileSize = fileSize })
            });
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
            Console.WriteLine($"[PipeServer] {message}");
        }
    }
}
