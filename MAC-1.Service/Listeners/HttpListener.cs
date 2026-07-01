using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAC_1.Service.Core;
using MAC_1.Service.Models;

namespace MAC_1.Service.Listeners
{
    public class HttpListener
    {
        private readonly int _port = 57575;
        private System.Net.HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private readonly EventDispatcher _dispatcher;

        public bool IsRunning => _isRunning;

        public HttpListener(EventDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new System.Net.HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Start();
                _isRunning = true;

                _ = AcceptConnectionsAsync(_cts.Token);
                Log($"HTTP server started on port {_port}");
            }
            catch (Exception ex)
            {
                Log($"Failed to start HTTP server: {ex.Message}");
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            Log("HTTP server stopped");
        }

        private async Task AcceptConnectionsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var context = await _listener!.GetContextAsync();
                    _ = HandleRequestAsync(context);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Log($"Accept error: {ex.Message}");
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

                string path = request.Url!.AbsolutePath.ToLower();

                if (path == "/api/health" && request.HttpMethod == "GET")
                    await HandleHealthCheck(response);
                else if (path == "/api/ping" && request.HttpMethod == "GET")
                    await HandlePing(response);
                else if (path == "/api/status" && request.HttpMethod == "GET")
                    await HandleStatus(response);
                else if (path == "/api/session" && request.HttpMethod == "POST")
                    await HandleSession(request, response);
                else if (path == "/api/download" && request.HttpMethod == "POST")
                    await HandleDownload(request, response);
                else if (path == "/api/size-update" && request.HttpMethod == "POST")
                    await HandleSizeUpdate(request, response);
                else
                {
                    response.StatusCode = 404;
                    await WriteJson(response, new { error = "Not found" });
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteJson(response, new { error = ex.Message });
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        private async Task HandleHealthCheck(HttpListenerResponse response)
        {
            await WriteJson(response, new
            {
                status = "ok",
                service = "MAC-1 Background Service",
                version = "1.0.0",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        private async Task HandlePing(HttpListenerResponse response)
        {
            await WriteJson(response, new
            {
                ping = "pong",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        private async Task HandleStatus(HttpListenerResponse response)
        {
            var stats = _dispatcher.GetStats();
            await WriteJson(response, stats);
        }

        private async Task HandleSession(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var session = JsonSerializer.Deserialize<DownloadSession>(body, options);

                if (session == null || string.IsNullOrEmpty(session.Url))
                {
                    response.StatusCode = 400;
                    await WriteJson(response, new { error = "Invalid session data" });
                    return;
                }

                await _dispatcher.DispatchAsync(session);

                await WriteJson(response, new
                {
                    success = true,
                    message = "Session received",
                    sessionId = Guid.NewGuid().ToString("N")[..8]
                });
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteJson(response, new { error = "Failed to process session: " + ex.Message });
            }
        }

        private async Task HandleDownload(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                DownloadSession? session = null;
                try { session = JsonSerializer.Deserialize<DownloadSession>(body, options); } catch { }

                if (session != null && !string.IsNullOrEmpty(session.Url))
                {
                    await _dispatcher.DispatchAsync(session);
                    await WriteJson(response, new
                    {
                        success = true,
                        message = "Download received",
                        downloadId = Guid.NewGuid().ToString("N")[..8]
                    });
                    return;
                }

                response.StatusCode = 400;
                await WriteJson(response, new { error = "Invalid download data" });
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteJson(response, new { error = "Failed to process download: " + ex.Message });
            }
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
                    await _dispatcher.DispatchSizeUpdateAsync(update.Url, update.FileSize);
                    await WriteJson(response, new { success = true });
                    return;
                }

                response.StatusCode = 400;
                await WriteJson(response, new { error = "Invalid size update data" });
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteJson(response, new { error = "Failed to process size update: " + ex.Message });
            }
        }

        private async Task WriteJson(HttpListenerResponse response, object data)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private void Log(string message)
        {
            Console.WriteLine($"[HttpListener] {message}");
        }
    }
}
