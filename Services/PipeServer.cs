using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class PipeServer
    {
        private static readonly Lazy<PipeServer> _instance = new(() => new PipeServer());
        public static PipeServer Instance => _instance.Value;

        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public event Action<DownloadData>? DownloadReceived;
        public event Action<string>? ClientConnected;
        public event Action<string>? ClientDisconnected;

        public bool IsRunning => _isRunning;

        private PipeServer() { }

        public void Start()
        {
            if (_isRunning) return;
            _cts = new CancellationTokenSource();
            _isRunning = true;
            _ = AcceptConnectionsAsync(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _isRunning = false;
        }

        private async Task AcceptConnectionsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var server = new NamedPipeServerStream(
                        DataService.Instance.Settings.PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);

                    _ = HandleClientAsync(server, ct);
                }
                catch (OperationCanceledException) { break; }
                catch { /* continue accepting */ }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
        {
            string clientId = Guid.NewGuid().ToString("N")[..8];
            ClientConnected?.Invoke(clientId);

            try
            {
                using var reader = new StreamReader(server);
                using var writer = new StreamWriter(server) { AutoFlush = true };

                while (!ct.IsCancellationRequested && server.IsConnected)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrEmpty(line)) break;

                    try
                    {
                        var data = JsonSerializer.Deserialize<DownloadData>(line);
                        if (data != null && !string.IsNullOrEmpty(data.Url))
                        {
                            DownloadReceived?.Invoke(data);
                            await writer.WriteLineAsync("OK");
                        }
                        else
                        {
                            await writer.WriteLineAsync("ERROR:InvalidData");
                        }
                    }
                    catch (JsonException)
                    {
                        await writer.WriteLineAsync("ERROR:InvalidJSON");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                ClientDisconnected?.Invoke(clientId);
                try { server.Dispose(); } catch { }
            }
        }
    }
}
