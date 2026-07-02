using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAC_1.Service.Core;
using MAC_1.Service.Models;
using System.Collections.Generic;

namespace MAC_1.Service.Listeners
{
    public class HttpListener
    {
        private readonly int _port = 57575;
        private System.Net.HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private readonly EventDispatcher _dispatcher;
        private readonly DownloadService? _downloadService;

        public bool IsRunning => _isRunning;

        public HttpListener(EventDispatcher dispatcher, DownloadService? downloadService = null)
        {
            _dispatcher = dispatcher;
            _downloadService = downloadService;
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
                Log($"HTTP {request.HttpMethod} {request.Url!.AbsolutePath}");

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
                else if (path == "/api/start-download" && request.HttpMethod == "POST")
                    await HandleStartDownload(request, response);
                else if (path == "/api/pause-download" && request.HttpMethod == "POST")
                    await HandlePauseDownload(request, response);
                else if (path == "/api/resume-download" && request.HttpMethod == "POST")
                    await HandleResumeDownload(request, response);
                else if (path == "/api/cancel-download" && request.HttpMethod == "POST")
                    await HandleCancelDownload(request, response);
                else if (path == "/api/sessions" && request.HttpMethod == "GET")
                    await HandleGetSessions(response);
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
                protocol = 1,
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
                Log("HandleSession: Reading body...");
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                Log($"HandleSession: Body length={body.Length}, preview={body[..Math.Min(200, body.Length)]}");
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var session = JsonSerializer.Deserialize<DownloadSession>(body, options);

                if (session == null || string.IsNullOrEmpty(session.Url))
                {
                    Log("[FAIL] Stage 4: Invalid session data - session is null or URL empty");
                    response.StatusCode = 400;
                    await WriteJson(response, new { error = "Invalid session data" });
                    return;
                }

                string uaInfo = string.IsNullOrEmpty(session.UserAgent) ? "EMPTY" : "present";
                string pdInfo = session.PostData != null ? "present" : "null";
                Log($"[PASS] Stage 4: Session received — filename={session.Filename}, url={session.Url}, headers={session.Headers?.Count ?? 0}, cookies={session.Cookies?.Count ?? 0}, browserRawHeaders={session.BrowserRawHeaders?.Count ?? 0}, userAgent={uaInfo}, postData={pdInfo}");
                await _dispatcher.DispatchAsync(session);

                await WriteJson(response, new
                {
                    success = true,
                    message = "Session received",
                    sessionId = Guid.NewGuid().ToString("N")[..8]
                });
                Log("HandleSession: Response sent");
            }
            catch (Exception ex)
            {
                Log($"[FAIL] Stage 4: EXCEPTION - {ex.Message}");
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

        private async Task HandleGetSessions(HttpListenerResponse response)
        {
            try
            {
                var sessions = Database.DatabaseService.Instance.Sessions.GetAll();
                await WriteJson(response, new
                {
                    success = true,
                    count = sessions.Count,
                    sessions = sessions.Select(s => new
                    {
                        sessionId = s.SessionId,
                        url = s.Url,
                        filename = s.Filename,
                        fileSize = s.FileSize,
                        bytesDownloaded = s.BytesDownloaded,
                        progress = s.Progress,
                        speed = s.Speed,
                        averageSpeed = s.AverageSpeed,
                        eta = s.ETA,
                        mimeType = s.MimeType,
                        status = s.Status,
                        savePath = s.SavePath,
                        category = s.Category,
                        resumeSupported = s.ResumeSupported,
                        errorMessage = s.ErrorMessage,
                        createdAt = s.CreatedAt.ToString("o"),
                        updatedAt = s.UpdatedAt.ToString("o")
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteJson(response, new { error = "Failed to get sessions: " + ex.Message });
            }
        }

        private async Task HandleStartDownload(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                Log($"HandleStartDownload: body={body}");
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var startReq = JsonSerializer.Deserialize<StartDownloadRequest>(body, options);

                if (startReq == null || string.IsNullOrEmpty(startReq.SessionId))
                {
                    Log("HandleStartDownload: Missing sessionId");
                    response.StatusCode = 400;
                    await WriteJson(response, new { error = "Missing sessionId" });
                    return;
                }

                if (_downloadService == null)
                {
                    Log("HandleStartDownload: DownloadService is null");
                    response.StatusCode = 503;
                    await WriteJson(response, new { error = "Download service not available" });
                    return;
                }

                // Load session from DB to get headers/cookies
                Log($"HandleStartDownload: Looking up session {startReq.SessionId} in DB");
                var session = Database.DatabaseService.Instance.Sessions.GetById(startReq.SessionId);
                if (session == null)
                {
                    Log($"HandleStartDownload: Session {startReq.SessionId} NOT FOUND in DB");
                    response.StatusCode = 404;
                    await WriteJson(response, new { error = $"Session not found: {startReq.SessionId}" });
                    return;
                }

                Log($"[PASS] Stage 8: Start download command received — sessionId={startReq.SessionId}, filename={session.Filename}, url={session.Url}");

                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(session.RawHeadersJson ?? "{}") ?? new();
                var cookies = JsonSerializer.Deserialize<List<CookieData>>(session.RawCookiesJson ?? "[]") ?? new();
                var browserRawHeaders = JsonSerializer.Deserialize<List<RawHeader>>(session.RawBrowserHeadersJson ?? "[]") ?? new();

                string uaShort = string.IsNullOrEmpty(session.UserAgent) ? "EMPTY" : session.UserAgent[..Math.Min(50, session.UserAgent.Length)];
                Log($"Stage 8 DB loaded: headers.Count={headers.Count}, cookies.Count={cookies.Count}, browserRawHeaders.Count={browserRawHeaders.Count}, userAgent={uaShort}");

                object? postData = null;
                if (!string.IsNullOrEmpty(session.RawPostDataJson) && session.RawPostDataJson != "{}")
                {
                    try { postData = JsonSerializer.Deserialize<object>(session.RawPostDataJson); }
                    catch { }
                }

                string savePath = startReq.SavePath ?? session.SavePath;
                if (string.IsNullOrEmpty(savePath))
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    Directory.CreateDirectory(dir);
                    savePath = Path.Combine(dir, session.Filename);
                }

                // Use FinalUrl (redirect-resolved CDN URL) when available — skip engine's own redirect resolution
                string downloadUrl = !string.IsNullOrEmpty(session.FinalUrl) ? session.FinalUrl : session.Url;
                string downloadMethod = session.RequestMethod ?? "GET";

                // If redirected to a different domain (CDN), force GET — POST form data was already consumed
                try
                {
                    var origHost = new Uri(session.Url).Host;
                    var finalHost = new Uri(downloadUrl).Host;
                    if (origHost != finalHost)
                    {
                        Log($"HandleStartDownload: Redirect to different domain ({origHost} → {finalHost}), forcing GET");
                        downloadMethod = "GET";
                    }
                }
                catch { }

                Log($"HandleStartDownload: url={downloadUrl} method={downloadMethod} (original={session.Url})");

                _ = _downloadService.StartDownloadAsync(
                    startReq.SessionId,
                    downloadUrl,
                    savePath,
                    headers,
                    cookies,
                    session.UserAgent,
                    session.Referrer,
                    browserRawHeaders,
                    downloadMethod,
                    postData,
                    session.FileSize);

                await WriteJson(response, new
                {
                    success = true,
                    message = "Download started",
                    sessionId = startReq.SessionId
                });
                Log($"HandleStartDownload: Started {startReq.SessionId}");
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteJson(response, new { error = "Failed to start download: " + ex.Message });
            }
        }

        private async Task HandlePauseDownload(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var req = JsonSerializer.Deserialize<StartDownloadRequest>(body, options);

                if (req == null || string.IsNullOrEmpty(req.SessionId))
                {
                    response.StatusCode = 400;
                    await WriteJson(response, new { error = "Missing sessionId" });
                    return;
                }

                if (_downloadService == null)
                {
                    response.StatusCode = 503;
                    await WriteJson(response, new { error = "Download service not available" });
                    return;
                }

                _downloadService.PauseDownload(req.SessionId);
                await WriteJson(response, new { success = true, message = "Download paused", sessionId = req.SessionId });
                Log($"HandlePauseDownload: Paused {req.SessionId}");
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteJson(response, new { error = "Failed to pause: " + ex.Message });
            }
        }

        private async Task HandleResumeDownload(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var req = JsonSerializer.Deserialize<StartDownloadRequest>(body, options);

                if (req == null || string.IsNullOrEmpty(req.SessionId))
                {
                    response.StatusCode = 400;
                    await WriteJson(response, new { error = "Missing sessionId" });
                    return;
                }

                if (_downloadService == null)
                {
                    response.StatusCode = 503;
                    await WriteJson(response, new { error = "Download service not available" });
                    return;
                }

                var session = Database.DatabaseService.Instance.Sessions.GetById(req.SessionId);
                if (session == null)
                {
                    response.StatusCode = 404;
                    await WriteJson(response, new { error = "Session not found" });
                    return;
                }

                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(session.RawHeadersJson) ?? new();
                var cookies = JsonSerializer.Deserialize<List<CookieData>>(session.RawCookiesJson) ?? new();
                var browserRawHeaders = JsonSerializer.Deserialize<List<RawHeader>>(session.RawBrowserHeadersJson) ?? new();
                object? postData = null;
                if (!string.IsNullOrEmpty(session.RawPostDataJson) && session.RawPostDataJson != "{}")
                {
                    try { postData = JsonSerializer.Deserialize<object>(session.RawPostDataJson); }
                    catch { }
                }

                string resumeUrl = !string.IsNullOrEmpty(session.FinalUrl) ? session.FinalUrl : session.Url;
                string resumeMethod = session.RequestMethod ?? "GET";
                try
                {
                    var origHost = new Uri(session.Url).Host;
                    var finalHost = new Uri(resumeUrl).Host;
                    if (origHost != finalHost) resumeMethod = "GET";
                } catch { }

                _downloadService.ResumeDownload(req.SessionId, resumeUrl, session.SavePath, headers, cookies, session.UserAgent, session.Referrer, browserRawHeaders,
                    resumeMethod, postData);
                await WriteJson(response, new { success = true, message = "Download resumed", sessionId = req.SessionId });
                Log($"HandleResumeDownload: Resumed {req.SessionId}");
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteJson(response, new { error = "Failed to resume: " + ex.Message });
            }
        }

        private async Task HandleCancelDownload(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var req = JsonSerializer.Deserialize<StartDownloadRequest>(body, options);

                if (req == null || string.IsNullOrEmpty(req.SessionId))
                {
                    response.StatusCode = 400;
                    await WriteJson(response, new { error = "Missing sessionId" });
                    return;
                }

                if (_downloadService == null)
                {
                    response.StatusCode = 503;
                    await WriteJson(response, new { error = "Download service not available" });
                    return;
                }

                _downloadService.CancelDownload(req.SessionId);
                await WriteJson(response, new { success = true, message = "Download cancelled", sessionId = req.SessionId });
                Log($"HandleCancelDownload: Cancelled {req.SessionId}");
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteJson(response, new { error = "Failed to cancel: " + ex.Message });
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
