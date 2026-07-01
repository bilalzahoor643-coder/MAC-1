using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class ExtensionService
    {
        private static readonly Lazy<ExtensionService> _instance = new(() => new ExtensionService());
        public static ExtensionService Instance => _instance.Value;

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private readonly int _port = 57575;

        public event Action<DownloadSession>? DownloadSessionReceived;
        public event Action<DownloadData>? DownloadReceived;
        public event Action<string>? StatusChanged;
        public event Action<string, long>? SizeUpdateReceived;

        public bool IsRunning => _isRunning;
        public int Port => _port;

        private ExtensionService() { }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Start();
                _isRunning = true;

                StatusChanged?.Invoke("Extension server started on port " + _port);
                _ = AcceptConnectionsAsync(_cts.Token);

                RegisterNativeMessagingHost();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Failed to start extension server: " + ex.Message);
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
            StatusChanged?.Invoke("Extension server stopped");
        }

        private async Task AcceptConnectionsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = HandleRequestAsync(context);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke("Error: " + ex.Message);
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                string path = request.Url.AbsolutePath.ToLower();

                if (path == "/api/health" && request.HttpMethod == "GET")
                    await HandleHealthCheck(response);
                else if (path == "/api/download" && request.HttpMethod == "POST")
                    await HandleDownloadRequest(request, response);
                else if (path == "/api/session" && request.HttpMethod == "POST")
                    await HandleSessionRequest(request, response);
                else if (path == "/api/status" && request.HttpMethod == "GET")
                    await HandleStatusRequest(response);
                else if (path == "/api/ping" && request.HttpMethod == "GET")
                    await HandlePingResponse(response);
                else if (path == "/api/size-update" && request.HttpMethod == "POST")
                    await HandleSizeUpdate(request, response);
                else
                {
                    response.StatusCode = 404;
                    await WriteResponse(response, new { error = "Not found" });
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteResponse(response, new { error = ex.Message });
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        private async Task HandleHealthCheck(HttpListenerResponse response)
        {
            await WriteResponse(response, new
            {
                status = "ok",
                service = "MAC-1 Download Manager",
                version = "1.0.0",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        private async Task HandlePingResponse(HttpListenerResponse response)
        {
            await WriteResponse(response, new { ping = "pong", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        }

        private async Task HandleSizeUpdate(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var update = JsonSerializer.Deserialize<SizeUpdateRequest>(body, options);

                if (update != null && !string.IsNullOrEmpty(update.Url) && update.FileSize > 0)
                {
                    SizeUpdateReceived?.Invoke(update.Url, update.FileSize);
                    await WriteResponse(response, new { success = true });
                    return;
                }

                response.StatusCode = 400;
                await WriteResponse(response, new { error = "Invalid size update data" });
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteResponse(response, new { error = "Failed to process size update: " + ex.Message });
            }
        }

        private async Task HandleStatusRequest(HttpListenerResponse response)
        {
            await WriteResponse(response, new
            {
                downloads = DataService.Instance.TotalDownloads,
                active = DataService.Instance.ActiveDownloads,
                completed = DataService.Instance.CompletedDownloads,
                failed = DataService.Instance.FailedDownloads
            });
        }

        private async Task HandleSessionRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var session = JsonSerializer.Deserialize<DownloadSession>(body, options);

                if (session == null || string.IsNullOrEmpty(session.Url))
                {
                    response.StatusCode = 400;
                    await WriteResponse(response, new { error = "Invalid session data" });
                    return;
                }

                DownloadSessionReceived?.Invoke(session);

                await WriteResponse(response, new
                {
                    success = true,
                    message = "Session received",
                    sessionId = Guid.NewGuid().ToString("N")[..8]
                });
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteResponse(response, new { error = "Failed to process session: " + ex.Message });
            }
        }

        private async Task HandleDownloadRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                DownloadSession? session = null;
                ExtensionDownloadRequest? legacy = null;

                try
                {
                    session = JsonSerializer.Deserialize<DownloadSession>(body, options);
                }
                catch { }

                if (session != null && !string.IsNullOrEmpty(session.Url))
                {
                    DownloadSessionReceived?.Invoke(session);
                    await WriteResponse(response, new
                    {
                        success = true,
                        message = "Download received",
                        downloadId = Guid.NewGuid().ToString("N")[..8]
                    });
                    return;
                }

                legacy = JsonSerializer.Deserialize<ExtensionDownloadRequest>(body, options);

                if (legacy == null || string.IsNullOrEmpty(legacy.url))
                {
                    response.StatusCode = 400;
                    await WriteResponse(response, new { error = "Invalid download data" });
                    return;
                }

                var data = new DownloadData
                {
                    Url = legacy.url,
                    Filename = legacy.filename ?? "",
                    FileSize = legacy.fileSize ?? 0,
                    Referrer = legacy.referrer ?? "",
                    MimeType = legacy.mimeType ?? "",
                    SavePath = legacy.savePath ?? ""
                };

                DownloadReceived?.Invoke(data);

                await WriteResponse(response, new
                {
                    success = true,
                    message = "Download received",
                    downloadId = Guid.NewGuid().ToString("N")[..8]
                });
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteResponse(response, new { error = "Failed to process download: " + ex.Message });
            }
        }

        private async Task WriteResponse(HttpListenerResponse response, object data)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private void RegisterNativeMessagingHost()
        {
            try
            {
                string hostName = "com.mac1.downloader";
                string manifestDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "User Data", "Default", "NativeMessagingHosts", hostName);

                Directory.CreateDirectory(manifestDir);

                var manifest = new
                {
                    name = hostName,
                    description = "MAC-1 Download Manager",
                    path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "",
                    type = "stdio",
                    allowed_origins = new[] { "chrome-extension://XXXXXXXXXX/" }
                };

                string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(manifestDir, "native-messaging-host.json"), manifestJson);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Note: Native messaging registration skipped: " + ex.Message);
            }
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
