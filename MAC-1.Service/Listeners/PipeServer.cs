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
        private NamedPipeServerStream? _server;
        private BinaryReader? _reader;
        private BinaryWriter? _writer;
        private readonly object _lock = new();
        private readonly string _logFile;

        public event Action? ClientConnected;
        public event Action? ClientDisconnected;

        public bool IsClientConnected
        {
            get { lock (_lock) return _server?.IsConnected == true; }
        }

        public PipeServer()
        {
            _logFile = Path.Combine(AppContext.BaseDirectory, "service-debug.log");
        }

        public void Start()
        {
            if (_isRunning) return;
            _cts = new CancellationTokenSource();
            _isRunning = true;
            _ = RunAsync(_cts.Token);
            Log("Pipe server started on: " + PipeName);
        }

        public void Stop()
        {
            _isRunning = false;
            _cts?.Cancel();
            lock (_lock)
            {
                try { _reader?.Dispose(); } catch { }
                try { _writer?.Dispose(); } catch { }
                try { _server?.Dispose(); } catch { }
                _reader = null;
                _writer = null;
                _server = null;
            }
            Log("Pipe server stopped");
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isRunning)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(ct);
                    Log("WPF client connected");

                    var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
                    var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);

                    lock (_lock)
                    {
                        try { _server?.Dispose(); } catch { }
                        try { _reader?.Dispose(); } catch { }
                        try { _writer?.Dispose(); } catch { }
                        _server = pipe;
                        _reader = reader;
                        _writer = writer;
                    }

                    ClientConnected?.Invoke();
                    await MonitorClientAsync(pipe, reader, writer, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { break; }
                catch (Exception ex)
                {
                    Log($"Error: {ex.Message}");
                    try { pipe?.Dispose(); } catch { }
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task MonitorClientAsync(NamedPipeServerStream pipe, BinaryReader reader, BinaryWriter writer, CancellationToken ct)
        {
            try
            {
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
                lock (_lock)
                {
                    if (_server == pipe)
                    {
                        try { _reader?.Dispose(); } catch { }
                        try { _writer?.Dispose(); } catch { }
                        try { pipe.Dispose(); } catch { }
                        _server = null;
                        _reader = null;
                        _writer = null;
                    }
                }
                ClientDisconnected?.Invoke();
                Log("WPF client disconnected");
            }
        }

        public async Task SendToClientAsync(PipeMessage msg)
        {
            BinaryWriter? writer;
            NamedPipeServerStream? pipe;
            lock (_lock)
            {
                writer = _writer;
                pipe = _server;
            }

            if (writer == null || pipe == null || !pipe.IsConnected)
            {
                Console.WriteLine($"[PipeServer] SendToClient SKIP: writer={writer != null}, pipe={pipe != null}, connected={pipe?.IsConnected}");
                return;
            }

            try
            {
                await WriteMessageAsync(writer, msg);
                Console.WriteLine($"[PipeServer] SendToClient OK: {msg.Type}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PipeServer] SendToClient FAIL: {ex.Message}");
            }
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
                    lock (_lock)
                    {
                        if (_writer == null || _server == null || !_server.IsConnected) return;
                        writer.Write(data.Length);
                        writer.Write(data);
                        writer.Flush();
                    }
                }
                catch { }
            });
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [PipeServer] {message}";
            Console.WriteLine(line);
            try { File.AppendAllText(_logFile, line + "\n"); } catch { }
        }
    }
}
