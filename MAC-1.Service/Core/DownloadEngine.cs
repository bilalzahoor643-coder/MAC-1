using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;
using MAC_1.Service.Models;

namespace MAC_1.Service.Core
{
    public class DownloadEngine
    {
        private readonly HttpClientHandler _handler;
        private readonly HttpClient _httpClient;
        private readonly string _logFile;
        private readonly string _debugDir;
        private string _currentDebugLog = string.Empty;

        private const int BUFFER_SIZE = 65536;
        private const int PROGRESS_INTERVAL_MS = 200;
        private const int FLUSH_INTERVAL_MS = 5000;

        public event EventHandler<DownloadEventArgs>? DownloadEvent;

        public DownloadEngine()
        {
            _debugDir = Path.Combine(AppContext.BaseDirectory, "download-debug");
            Directory.CreateDirectory(_debugDir);
            _logFile = Path.Combine(AppContext.BaseDirectory, "service-debug.log");

            _handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = false,
                UseCookies = false,
                Proxy = WebRequest.GetSystemWebProxy(),
                UseProxy = false,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
            };

            _httpClient = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromMinutes(60)
            };
        }

        // ═══════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════

        public async Task StartDownloadAsync(string sessionId, string url, string savePath,
            Dictionary<string, string>? headers = null,
            List<CookieData>? cookies = null,
            string? userAgent = null,
            string? referer = null,
            CancellationToken cancellationToken = default,
            List<RawHeader>? browserRawHeaders = null,
            string sessionMethod = "GET",
            object? postData = null,
            long knownFileSize = 0)
        {
            var debugLog = Path.Combine(_debugDir, $"{sessionId}_debug.log");
            _currentDebugLog = debugLog;
            DebugLog(debugLog, $"═══════════════════════════════════════════════════════════");
            DebugLog(debugLog, $"DOWNLOAD START: sessionId={sessionId}");
            DebugLog(debugLog, $"Original URL: {url}");
            DebugLog(debugLog, $"Save Path: {savePath}");
            DebugLog(debugLog, $"User Agent: {userAgent ?? "(default)"}");
            DebugLog(debugLog, $"Referer: {referer ?? "(none)"}");
            DebugLog(debugLog, $"Cookies: {cookies?.Count ?? 0} items");
            DebugLog(debugLog, $"Extension Headers: {headers?.Count ?? 0} items");
            DebugLog(debugLog, $"Browser Raw Headers: {browserRawHeaders?.Count ?? 0} items");
            DebugLog(debugLog, $"HTTP Method: {sessionMethod}");
            string postDataStr = postData != null ? "YES" : "none";
            DebugLog(debugLog, $"Post Data: {postDataStr}");
            if (postData != null)
                DebugLog(debugLog, $"Post Data Content: {postData}");

            if (headers != null)
            {
                foreach (var kv in headers)
                    DebugLog(debugLog, $"  Header: {kv.Key}: {kv.Value}");
            }
            if (cookies != null)
            {
                foreach (var c in cookies)
                    DebugLog(debugLog, $"  Cookie: {c.Name}={c.Value[..Math.Min(50, c.Value.Length)]}... (domain={c.Domain}, path={c.Path}, secure={c.Secure}, httpOnly={c.HttpOnly})");
            }

            Log($"[Engine] Starting download: sessionId={sessionId}, url={url}");
            Emit(DownloadEventType.Started, sessionId, url, state: DownloadState.Starting);

            long bytesDownloaded = 0;
            DateTime startTime = DateTime.UtcNow;

            try
            {
                // ── Step 1: Resolve redirects + get metadata ──
                Emit(DownloadEventType.StateChanged, sessionId, url, state: DownloadState.ReceivingMetadata);

                string finalUrl = url;
                long fileSize = 0;
                bool resumeSupported = false;
                int httpStatusCode = 0;
                var redirectResult = new RedirectResult { OriginalUrl = url, RedirectChain = new List<string> { url } };

                try
                {
                    redirectResult = await ResolveRedirectsAsync(url, headers, cookies, userAgent, referer, debugLog, browserRawHeaders);
                    finalUrl = redirectResult.FinalUrl;
                    fileSize = redirectResult.ContentLength;
                    resumeSupported = redirectResult.AcceptRanges;
                    httpStatusCode = redirectResult.StatusCode;

                    DebugLog(debugLog, $"METADATA RESULT:");
                    DebugLog(debugLog, $"  Final URL: {finalUrl}");
                    DebugLog(debugLog, $"  Content-Length: {fileSize} ({FormatSize(fileSize)})");
                    DebugLog(debugLog, $"  Resume Support: {resumeSupported}");
                    DebugLog(debugLog, $"  HTTP Status: {httpStatusCode}");
                    DebugLog(debugLog, $"  Redirect Hops: {redirectResult.HopCount}");
                    DebugLog(debugLog, $"  Redirect Chain: {string.Join(" → ", redirectResult.RedirectChain)}");
                    DebugLog(debugLog, $"  ETag: {redirectResult.ETag}");
                    DebugLog(debugLog, $"  Last-Modified: {redirectResult.LastModified}");
                    foreach (var kv in redirectResult.ResponseHeaders)
                        DebugLog(debugLog, $"  Response Header: {kv.Key}: {kv.Value}");

                    Log($"[Engine] Metadata: finalUrl={finalUrl}, size={fileSize}, resume={resumeSupported}, status={httpStatusCode}");

                    Emit(DownloadEventType.MetadataReceived, sessionId, url,
                        fileSize: fileSize, resumeSupported: resumeSupported,
                        state: DownloadState.ReceivingMetadata, httpStatusCode: httpStatusCode,
                        finalUrl: finalUrl);
                }
                catch (Exception ex)
                {
                    DebugLog(debugLog, $"METADATA FAILED: {ex}");
                    Log($"[Engine] Metadata FAILED: {ex.Message}");
                    Emit(DownloadEventType.Failed, sessionId, url,
                        state: DownloadState.Failed, errorMessage: $"Metadata failed: {ex.Message}");
                    return;
                }

                // ── Step 2: Check for existing file (resume) ──
                long existingBytes = 0;
                if (resumeSupported && File.Exists(savePath))
                {
                    var fileInfo = new FileInfo(savePath);
                    existingBytes = fileInfo.Length;

                    if (fileSize > 0 && existingBytes == fileSize)
                    {
                        DebugLog(debugLog, $"File already complete ({existingBytes} bytes)");
                        Log($"[Engine] File already complete ({existingBytes} bytes)");
                        Emit(DownloadEventType.Completed, sessionId, url,
                            fileSize: fileSize, bytesDownloaded: fileSize,
                            progress: 100, state: DownloadState.Completed,
                            savePath: savePath);
                        return;
                    }

                    if (fileSize > 0 && existingBytes > fileSize)
                    {
                        DebugLog(debugLog, $"Stale file: existing={existingBytes} > expected={fileSize} — deleting");
                        Log($"[Engine] Existing file ({existingBytes} bytes) larger than expected ({fileSize} bytes) — deleting stale file");
                        try { File.Delete(savePath); } catch { }
                        existingBytes = 0;
                    }
                    else if (existingBytes > 0)
                    {
                        DebugLog(debugLog, $"Resuming from byte {existingBytes}");
                        Log($"[Engine] Resuming from byte {existingBytes}");
                    }
                }

                // ── Step 3: Start downloading ──
                Emit(DownloadEventType.StateChanged, sessionId, url,
                    state: DownloadState.Downloading, fileSize: fileSize);

                using var request = new HttpRequestMessage(new HttpMethod(sessionMethod), finalUrl);
                ApplyHeaders(request, headers, cookies, userAgent, referer, null, browserRawHeaders);

                if (existingBytes > 0 && sessionMethod == "GET")
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);

                // Send POST body if present (form data from browser)
                if (sessionMethod == "POST" && postData != null)
                {
                    DebugLog(debugLog, $"SENDING POST DATA: {postData}");
                    string postBody = "";
                    if (postData is System.Text.Json.JsonElement je)
                    {
                        if (je.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            var pairs = new List<string>();
                            foreach (var prop in je.EnumerateObject())
                            {
                                string val = prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array
                                    ? string.Join(",", prop.Value.EnumerateArray().Select(a => a.GetString() ?? ""))
                                    : prop.Value.ToString();
                                pairs.Add($"{Uri.EscapeDataString(prop.Name)}={Uri.EscapeDataString(val)}");
                            }
                            postBody = string.Join("&", pairs);
                        }
                        else
                        {
                            postBody = je.ToString();
                        }
                    }
                    else if (postData is Dictionary<string, string> dict)
                    {
                        postBody = string.Join("&", dict.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
                    }
                    else if (postData is string s)
                    {
                        postBody = s;
                    }

                    if (!string.IsNullOrEmpty(postBody))
                    {
                        request.Content = new StringContent(postBody, Encoding.UTF8, "application/x-www-form-urlencoded");
                    }
                }

                DebugLog(debugLog, $"SENDING REQUEST:");
                DebugLog(debugLog, $"  Method: {sessionMethod}");
                DebugLog(debugLog, $"  URL: {finalUrl}");
                string rangeStr = existingBytes > 0 ? $"bytes={existingBytes}-" : "(none)";
                DebugLog(debugLog, $"  Range: {rangeStr}");
                DebugLog(debugLog, $"ENGINE HEADERS SENT:");
                foreach (var h in request.Headers)
                    DebugLog(debugLog, $"  {h.Key}: {string.Join(", ", h.Value)}");
                foreach (var h in request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                    DebugLog(debugLog, $"  Content-{h.Key}: {string.Join(", ", h.Value)}");
                DebugLog(debugLog, $"TOTAL ENGINE HEADERS: {request.Headers.Count()}");

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                Log($"[PASS] Stage 10: HTTP request sent — {sessionMethod} {finalUrl}");
                Log($"[PASS] Stage 11: Response received — status={(int)response.StatusCode} {response.StatusCode}, content-type={response.Content.Headers.ContentType}, content-length={response.Content.Headers.ContentLength}");

                DebugLog(debugLog, $"RESPONSE RECEIVED:");
                DebugLog(debugLog, $"  Status: {(int)response.StatusCode} {response.StatusCode}");
                DebugLog(debugLog, $"  Final URL: {response.RequestMessage?.RequestUri}");
                DebugLog(debugLog, $"  Content-Type: {response.Content.Headers.ContentType}");
                DebugLog(debugLog, $"  Content-Length: {response.Content.Headers.ContentLength}");
                DebugLog(debugLog, $"  Content-Encoding: {string.Join(", ", response.Content.Headers.ContentEncoding)}");
                DebugLog(debugLog, $"  Content-Disposition: {response.Content.Headers.ContentDisposition}");
                foreach (var h in response.Headers)
                    DebugLog(debugLog, $"  Response Header: {h.Key}: {string.Join(", ", h.Value)}");
                foreach (var h in response.Content.Headers)
                    DebugLog(debugLog, $"  Content Header: {h.Key}: {string.Join(", ", h.Value)}");

                if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    DebugLog(debugLog, $"304 Not Modified — file complete");
                    Emit(DownloadEventType.Completed, sessionId, url,
                        fileSize: fileSize, bytesDownloaded: existingBytes,
                        progress: 100, state: DownloadState.Completed, savePath: savePath);
                    request.Dispose();
                    response.Dispose();
                    return;
                }

                // ── 416 Range Not Satisfiable → retry fresh download (Chrome behavior) ──
                if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable && existingBytes > 0)
                {
                    DebugLog(debugLog, $"416 Range Not Satisfiable — server rejected resume from byte {existingBytes}");
                    Log($"[Engine] 416: Server rejected Range header — retrying fresh download from byte 0");

                    try { File.Delete(savePath); } catch { }
                    existingBytes = 0;

                    // Retry WITHOUT Range header (fresh download from beginning)
                    using var retryRequest = new HttpRequestMessage(new HttpMethod(sessionMethod), finalUrl);
                    ApplyHeaders(retryRequest, headers, cookies, userAgent, referer, null, browserRawHeaders);

                    if (sessionMethod == "POST" && postData != null)
                    {
                        string postBody = "";
                        if (postData is System.Text.Json.JsonElement je2)
                        {
                            if (je2.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                var pairs = new List<string>();
                                foreach (var prop in je2.EnumerateObject())
                                {
                                    string val = prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array
                                        ? string.Join(",", prop.Value.EnumerateArray().Select(a => a.GetString() ?? ""))
                                        : prop.Value.ToString();
                                    pairs.Add($"{Uri.EscapeDataString(prop.Name)}={Uri.EscapeDataString(val)}");
                                }
                                postBody = string.Join("&", pairs);
                            }
                            else postBody = je2.ToString();
                        }
                        else if (postData is Dictionary<string, string> dict2)
                            postBody = string.Join("&", dict2.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
                        else if (postData is string s2)
                            postBody = s2;

                        if (!string.IsNullOrEmpty(postBody))
                            retryRequest.Content = new StringContent(postBody, Encoding.UTF8, "application/x-www-form-urlencoded");
                    }

                    response.Dispose();

                    using var retryResponse = await _httpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    Log($"[PASS] Stage 10 (retry): HTTP request sent — {sessionMethod} {finalUrl}");
                    Log($"[PASS] Stage 11 (retry): Response received — status={(int)retryResponse.StatusCode} {retryResponse.StatusCode}");

                    retryResponse.EnsureSuccessStatusCode();

                    // Use retry response from here on — replace response
                    // Copy retryResponse into response variable for rest of method
                    // We can't re-assign 'using var response', so we work with retryResponse directly below

                    var retryResponseContentType = retryResponse.Content.Headers.ContentType?.MediaType ?? "";
                    long retryTotalBytes = retryResponse.Content.Headers.ContentLength ?? 0;

                    if (retryResponseContentType.Contains("text/html") || retryResponseContentType.Contains("text/plain"))
                    {
                        Emit(DownloadEventType.Failed, sessionId, url,
                            state: DownloadState.Failed,
                            errorMessage: $"Server returned {retryResponseContentType} (status {(int)retryResponse.StatusCode}) after retry.");
                        return;
                    }

                    if (retryTotalBytes > 0) fileSize = retryTotalBytes;

                    var retryFileName = retryResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                    string retrySavePath = savePath;
                    if (!string.IsNullOrEmpty(retryFileName))
                    {
                        var dir = Path.GetDirectoryName(savePath);
                        if (!string.IsNullOrEmpty(dir))
                            retrySavePath = Path.Combine(dir, retryFileName);
                    }

                    var retrySaveDir = Path.GetDirectoryName(retrySavePath);
                    if (!string.IsNullOrEmpty(retrySaveDir) && !Directory.Exists(retrySaveDir))
                        Directory.CreateDirectory(retrySaveDir);

                    // Stream download from retry response
                    using var retryContentStream = await retryResponse.Content.ReadAsStreamAsync(cancellationToken);
                    Log($"[PASS] Stage 12 (retry): Response stream opened — starting download to {retrySavePath}");

                    long retryBytesDownloaded = 0;
                    var retryPeekBuffer = new byte[8192];
                    int retryPeeked = await retryContentStream.ReadAsync(retryPeekBuffer.AsMemory(0, 8192), cancellationToken);

                    Emit(DownloadEventType.MetadataReceived, sessionId, url,
                        fileSize: fileSize, resumeSupported: false, state: DownloadState.Downloading, savePath: retrySavePath);

                    await using (var retryFileStream = new FileStream(retrySavePath, FileMode.Create, FileAccess.Write, FileShare.Read, BUFFER_SIZE, FileOptions.Asynchronous))
                    {
                        var retryStartTime = DateTime.UtcNow;
                        var retryLastProgressTime = DateTime.UtcNow;
                        double retryLastEmittedProgress = -1;
                        long retryLastBytesForSpeed = 0;
                        double retryAvgSpeed = 0;

                        if (retryPeeked > 0)
                        {
                            await retryFileStream.WriteAsync(retryPeekBuffer.AsMemory(0, retryPeeked), cancellationToken);
                            retryBytesDownloaded = retryPeeked;
                        }

                        var retryBuf = new byte[BUFFER_SIZE];
                        int retryRead;
                        while ((retryRead = await retryContentStream.ReadAsync(retryBuf, cancellationToken)) > 0)
                        {
                            await retryFileStream.WriteAsync(retryBuf.AsMemory(0, retryRead), cancellationToken);
                            retryBytesDownloaded += retryRead;

                            var retryNow = DateTime.UtcNow;
                            var retryElapsed = (retryNow - retryStartTime).TotalSeconds;
                            if (retryElapsed > 0.5)
                                retryAvgSpeed = retryBytesDownloaded / retryElapsed;

                            double retryProgress = fileSize > 0 ? Math.Clamp((double)retryBytesDownloaded / fileSize * 100, 0, 100) : 0;
                            double retryEta = retryAvgSpeed > 0 && fileSize > 0 ? Math.Max(0, (fileSize - retryBytesDownloaded) / retryAvgSpeed) : 0;

                            if (Math.Abs(retryProgress - retryLastEmittedProgress) > 0.01 || retryBytesDownloaded != retryLastBytesForSpeed)
                            {
                                Emit(DownloadEventType.ProgressChanged, sessionId, url,
                                    fileSize: fileSize, bytesDownloaded: retryBytesDownloaded,
                                    progress: retryProgress, speed: retryAvgSpeed, averageSpeed: retryAvgSpeed,
                                    eta: retryEta, state: DownloadState.Downloading,
                                    elapsedSeconds: retryElapsed, savePath: retrySavePath);
                                retryLastEmittedProgress = retryProgress;
                            }
                            retryLastBytesForSpeed = retryBytesDownloaded;
                        }

                        await retryFileStream.FlushAsync(cancellationToken);
                    }

                    Log($"[PASS] Stage 14 (retry): Download completed — {retryBytesDownloaded} bytes");
                    Emit(DownloadEventType.Completed, sessionId, url,
                        fileSize: fileSize, bytesDownloaded: retryBytesDownloaded,
                        progress: 100, state: DownloadState.Completed, savePath: retrySavePath);
                    return;
                }

                response.EnsureSuccessStatusCode();

                // ── EARLY HTML DETECTION from Content-Type header ──
                var responseContentType = response.Content.Headers.ContentType?.MediaType ?? "";
                var responseContentEncoding = string.Join(", ", response.Content.Headers.ContentEncoding);
                var totalBytes = response.Content.Headers.ContentLength ?? 0;

                DebugLog(debugLog, $"RESPONSE CONTENT-VALIDATION:");
                DebugLog(debugLog, $"  Content-Type: {responseContentType}");
                DebugLog(debugLog, $"  Content-Encoding: {responseContentEncoding}");
                DebugLog(debugLog, $"  Content-Length: {totalBytes}");
                DebugLog(debugLog, $"  Transfer-Encoding: {response.Headers.TransferEncoding}");

                if (responseContentType.Contains("text/html") || responseContentType.Contains("text/plain"))
                {
                    DebugLog(debugLog, $"❌ CONTENT-TYPE IS {responseContentType} — server returned error/download page, NOT file");

                    // Read first 2KB of HTML for diagnostics
                    var htmlBuf = new byte[4096];
                    using var htmlStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    int htmlRead = await htmlStream.ReadAsync(htmlBuf, cancellationToken);
                    string htmlPreview = Encoding.UTF8.GetString(htmlBuf[..htmlRead]);
                    DebugLog(debugLog, $"HTML preview ({htmlRead} bytes): {htmlPreview}");

                    // Capture all engine response headers
                    var engineRespHeaders = new Dictionary<string, string>();
                    foreach (var h in response.Headers)
                        engineRespHeaders[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);
                    foreach (var h in response.Content.Headers)
                        engineRespHeaders[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);

                    // Generate COMPARISON REPORT — browser vs engine
                    await GenerateDiagnosticsReport(
                        sessionId: sessionId,
                        originalUrl: url,
                        engineFinalUrl: finalUrl,
                        engineStatusCode: (int)response.StatusCode,
                        engineContentType: responseContentType,
                        engineContentLength: totalBytes,
                        redirectChain: redirectResult.RedirectChain,
                        engineResponseHeaders: engineRespHeaders,
                        browserRawHeaders: browserRawHeaders,
                        cookies: cookies,
                        extensionHeaders: headers,
                        userAgent: userAgent,
                        referer: referer,
                        htmlContent: htmlPreview,
                        debugLog: debugLog);

                    Emit(DownloadEventType.Failed, sessionId, url,
                        state: DownloadState.Failed,
                        errorMessage: $"Server returned {responseContentType} (status {(int)response.StatusCode}). " +
                            $"HTML type: {ClassifyHtml(htmlPreview)}. " +
                            $"Diagnostics report saved for analysis.");
                    return;
                }

                // ── Get response content length ──
                if (totalBytes > 0) fileSize = totalBytes;
                if (fileSize <= 0 && existingBytes > 0) fileSize = existingBytes;
                if (fileSize <= 0 && knownFileSize > 0) fileSize = knownFileSize;

                DebugLog(debugLog, $"Expected file size: {fileSize} ({FormatSize(fileSize)})");

                // ── Get filename from Content-Disposition ──
                var finalFileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                string actualSavePath = savePath;
                if (!string.IsNullOrEmpty(finalFileName))
                {
                    var dir = Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(dir))
                        actualSavePath = Path.Combine(dir, finalFileName);
                }

                var saveDir = Path.GetDirectoryName(actualSavePath);
                if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                Log($"[PASS] Stage 12: Response stream opened — starting download to {actualSavePath}");

                // ── Peek first bytes for validation ──
                var peekBuffer = new byte[8192];
                int peeked = await contentStream.ReadAsync(peekBuffer.AsMemory(0, 8192), cancellationToken);
                DebugLog(debugLog, $"First {peeked} bytes (hex): {BitConverter.ToString(peekBuffer[..peeked])}");

                // ── Check if response is zstd compressed (raw zstd not handled by handler) ──
                bool isZstd = peeked >= 4 && peekBuffer[0] == 0x28 && peekBuffer[1] == 0xB5 && peekBuffer[2] == 0x2F && peekBuffer[3] == 0xFD;
                if (isZstd)
                {
                    DebugLog(debugLog, $"ZSTD DETECTED — server sent zstd but handler can't auto-decompress");
                    DebugLog(debugLog, $"Decompressing with ZstdSharp...");

                    // Read ALL remaining content
                    using var ms = new MemoryStream();
                    await ms.WriteAsync(peekBuffer.AsMemory(0, peeked), cancellationToken);
                    var remainingBuf = new byte[BUFFER_SIZE];
                    int remaining;
                    while ((remaining = await contentStream.ReadAsync(remainingBuf, cancellationToken)) > 0)
                        await ms.WriteAsync(remainingBuf.AsMemory(0, remaining), cancellationToken);

                    var compressed = ms.ToArray();
                    var totalDownloaded = compressed.Length;
                    DebugLog(debugLog, $"Total compressed bytes received: {totalDownloaded} ({FormatSize(totalDownloaded)})");

                    try
                    {
                        using var decompressor = new Decompressor();

                        // Get decompressed size
                        long decompressedSizeLong = (long)Decompressor.GetDecompressedSize(compressed.AsSpan(0, Math.Min(compressed.Length, 1024)));
                        DebugLog(debugLog, $"Expected decompressed size: {decompressedSizeLong} ({FormatSize(decompressedSizeLong)})");

                        // Decompress in one shot
                        int decompressedSize = decompressedSizeLong > 0 ? (int)decompressedSizeLong : compressed.Length * 4;
                        var decompressedBytes = new byte[decompressedSize];
                        int written = decompressor.Unwrap(compressed, decompressedBytes, 0);

                        DebugLog(debugLog, $"Decompressed size: {written} ({FormatSize(written)})");
                        DebugLog(debugLog, $"First 512 bytes of decompressed (hex): {BitConverter.ToString(decompressedBytes[..Math.Min(512, written)])}");

                        // Show first bytes as text for inspection
                        string preview = Encoding.UTF8.GetString(decompressedBytes[..Math.Min(1024, written)]);
                        DebugLog(debugLog, $"First 1024 chars as text: {preview}");

                        // Check if decompressed content is HTML
                        bool isHtml = written > 0 &&
                            (preview.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                             preview.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase));
                        if (isHtml)
                        {
                            DebugLog(debugLog, $"DECOMPRESSED CONTENT IS HTML — server rejected our request");
                            DebugLog(debugLog, $"Full HTML preview: {Encoding.UTF8.GetString(decompressedBytes[..Math.Min(4096, written)])}");
                            Emit(DownloadEventType.Failed, sessionId, url,
                                state: DownloadState.Failed,
                                errorMessage: "Server returned HTML error page (decompressed from zstd). The server may be blocking automated downloads.");
                            return;
                        }

                        // Write decompressed content to file
                        DebugLog(debugLog, $"Writing decompressed content to: {actualSavePath}");
                        await File.WriteAllBytesAsync(actualSavePath, decompressedBytes[..written], cancellationToken);
                        bytesDownloaded = written;

                        if (fileSize <= 0) fileSize = bytesDownloaded;

                        double finalElapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                        double finalSpeed = finalElapsed > 0 ? bytesDownloaded / finalElapsed : 0;
                        DebugLog(debugLog, $"✅ Download complete: {bytesDownloaded} ({FormatSize(bytesDownloaded)}) in {finalElapsed:F1}s");
                        Log($"[Engine] Completed (zstd): {bytesDownloaded} bytes in {finalElapsed:F1}s");

                        Emit(DownloadEventType.Completed, sessionId, url,
                            fileSize: fileSize, bytesDownloaded: bytesDownloaded,
                            progress: 100, speed: 0, averageSpeed: finalSpeed,
                            eta: 0, state: DownloadState.Completed,
                            elapsedSeconds: finalElapsed, savePath: actualSavePath);
                    }
                    catch (Exception zstdEx)
                    {
                        DebugLog(debugLog, $"❌ ZSTD DECOMPRESSION FAILED: {zstdEx.Message}");
                        Log($"[Engine] ZSTD decompression failed: {zstdEx.Message}");

                        // Try to detect if content is actually HTML (server sent error page with zstd-like header)
                        bool rawIsHtml = false;
                        try
                        {
                            // Try reading raw bytes as text to check for HTML
                            string rawText = Encoding.UTF8.GetString(compressed.AsSpan(0, Math.Min(compressed.Length, 4096)));
                            DebugLog(debugLog, $"Raw content as text: {rawText[..Math.Min(500, rawText.Length)]}");
                            rawIsHtml = rawText.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                                        rawText.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
                                        rawText.Contains("<head", StringComparison.OrdinalIgnoreCase) ||
                                        rawText.Contains("<title>", StringComparison.OrdinalIgnoreCase);
                        }
                        catch { }

                        if (rawIsHtml)
                        {
                            DebugLog(debugLog, $"Raw content is HTML — server returned compressed error page");
                            Emit(DownloadEventType.Failed, sessionId, url,
                                state: DownloadState.Failed,
                                errorMessage: "Server returned compressed HTML error page. The site requires browser-based download. Try: open the download page in browser, right-click the actual download link → Copy Link → paste in app.");
                        }
                        else
                        {
                            var rawPath = actualSavePath + ".bin";
                            await File.WriteAllBytesAsync(rawPath, compressed, cancellationToken);
                            DebugLog(debugLog, $"Raw bytes saved to: {rawPath}");
                            DebugLog(debugLog, $"First 64 bytes hex: {BitConverter.ToString(compressed[..Math.Min(64, compressed.Length)])}");

                            Emit(DownloadEventType.Failed, sessionId, url,
                                state: DownloadState.Failed,
                                errorMessage: $"Server returned unexpected compressed data ({FormatSize(compressed.Length)}). Decompression failed: {zstdEx.Message}. Raw file saved to: {rawPath}");
                        }
                    }
                    return;
                }

                // ── Check if peeked content is HTML ──
                if (peeked > 0)
                {
                    string preview = Encoding.UTF8.GetString(peekBuffer, 0, peeked);
                    if (preview.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                        preview.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase))
                    {
                        string htmlContent = preview;
                        DebugLog(debugLog, $"❌ RESPONSE IS HTML — server returned download page instead of file");
                        DebugLog(debugLog, $"HTML preview: {htmlContent}");

                        // Try to extract actual download URL from HTML
                        string? extractedUrl = ExtractDownloadUrlFromHtml(htmlContent);
                        if (!string.IsNullOrEmpty(extractedUrl))
                        {
                            DebugLog(debugLog, $"EXTRACTED DOWNLOAD URL: {extractedUrl}");
                            Log($"[Engine] Extracted download URL from HTML: {extractedUrl}");

                            // Retry with extracted URL
                            Emit(DownloadEventType.StateChanged, sessionId, url,
                                state: DownloadState.ReceivingMetadata,
                                errorMessage: $"Followed redirect from download page to: {extractedUrl}");

                            await StartDownloadAsync(sessionId, extractedUrl, savePath, headers, cookies, userAgent, referer, cancellationToken, browserRawHeaders: browserRawHeaders, sessionMethod: sessionMethod, postData: postData);
                            return;
                        }

                        string errorMsg = "Server returned HTML download page instead of file. " +
                            "This site requires browser-based download. " +
                            "Try: right-click the download link → Copy Link → paste in app.";
                        Emit(DownloadEventType.Failed, sessionId, url,
                            state: DownloadState.Failed,
                            errorMessage: errorMsg);
                        return;
                    }
                }

                // ── CONTENT VALIDATION ──
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                var contentEncoding = string.Join(", ", response.Content.Headers.ContentEncoding);
                DebugLog(debugLog, $"CONTENT VALIDATION:");
                DebugLog(debugLog, $"  Content-Type: {contentType}");
                DebugLog(debugLog, $"  Content-Encoding: {contentEncoding}");
                DebugLog(debugLog, $"  Content-Length: {totalBytes}");
                DebugLog(debugLog, $"  First {peeked} bytes: {Encoding.UTF8.GetString(peekBuffer[..peeked])}");

                // ── Validate: if Content-Length is suspiciously small for a large file ──
                if (fileSize > 0 && totalBytes > 0 && totalBytes < 1024 * 100 && fileSize > 1024 * 1024)
                {
                    DebugLog(debugLog, $"⚠️ SIZE MISMATCH: Expected {FormatSize(fileSize)} but Content-Length is only {FormatSize(totalBytes)}");
                    DebugLog(debugLog, $"Server may be returning error page instead of file");

                    // Read the small response to inspect
                    var smallBuf = new byte[Math.Min(totalBytes, 4096)];
                    int smallRead = await contentStream.ReadAsync(smallBuf, cancellationToken);
                    string smallPreview = Encoding.UTF8.GetString(smallBuf[..smallRead]);
                    DebugLog(debugLog, $"Small response content: {smallPreview}");

                    // Write it anyway but warn
                    using var fileStream = new FileStream(actualSavePath, FileMode.Create, FileAccess.Write);
                    await fileStream.WriteAsync(smallBuf.AsMemory(0, smallRead), cancellationToken);
                    bytesDownloaded = smallRead;

                    Emit(DownloadEventType.Failed, sessionId, url,
                        state: DownloadState.Failed,
                        errorMessage: $"Server returned {FormatSize(totalBytes)} but expected {FormatSize(fileSize)}. Response: {smallPreview[..Math.Min(200, smallPreview.Length)]}");
                    return;
                }

                // ── Normal download ──
                using var normalFileStream = new FileStream(
                    actualSavePath,
                    existingBytes > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None, BUFFER_SIZE, FileOptions.Asynchronous);

                var buffer = new byte[BUFFER_SIZE];
                bytesDownloaded = existingBytes;

                // ── Write peeked bytes first ──
                if (peeked > 0)
                {
                    await normalFileStream.WriteAsync(peekBuffer.AsMemory(0, peeked), cancellationToken);
                    bytesDownloaded += peeked;
                }

                var lastProgressTime = DateTime.UtcNow;
                long lastBytesForSpeed = bytesDownloaded;
                var speedWindow = new Queue<(long bytes, DateTime time)>();
                double lastEmittedProgress = -1;
                DateTime lastFlushTime = DateTime.UtcNow;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await normalFileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    bytesDownloaded += bytesRead;

                    var now = DateTime.UtcNow;
                    double elapsedSec = (now - lastProgressTime).TotalSeconds;

                    if (elapsedSec * 1000 >= PROGRESS_INTERVAL_MS)
                    {
                        speedWindow.Enqueue((bytesDownloaded, now));
                        while (speedWindow.Count > 0 && (now - speedWindow.Peek().time).TotalSeconds > 3.0)
                            speedWindow.Dequeue();

                        double avgSpeed = 0;
                        if (speedWindow.Count >= 2)
                        {
                            var oldest = speedWindow.Peek();
                            double windowSec = (now - oldest.time).TotalSeconds;
                            if (windowSec > 0)
                                avgSpeed = (bytesDownloaded - oldest.bytes) / windowSec;
                        }

                        double progress = fileSize > 0 ? Math.Clamp((double)bytesDownloaded / fileSize * 100, 0, 100) : 0;
                        double eta = avgSpeed > 0 && fileSize > 0 ? Math.Max(0, (fileSize - bytesDownloaded) / avgSpeed) : 0;
                        double elapsedTotal = (now - startTime).TotalSeconds;

                        bool progressChanged = Math.Abs(progress - lastEmittedProgress) > 0.01;
                        bool bytesChanged = bytesDownloaded != lastBytesForSpeed;
                        if (progressChanged || bytesChanged || elapsedTotal < 2)
                        {
                            Emit(DownloadEventType.ProgressChanged, sessionId, url,
                                fileSize: fileSize, bytesDownloaded: bytesDownloaded,
                                progress: progress, speed: avgSpeed, averageSpeed: avgSpeed,
                                eta: eta, state: DownloadState.Downloading,
                                elapsedSeconds: elapsedTotal, savePath: actualSavePath);
                            lastEmittedProgress = progress;

                            if (bytesDownloaded == existingBytes + peeked + bytesRead)
                                Log($"[PASS] Stage 13: First progress event — {progress:F1}% {FormatSize(bytesDownloaded)}/{FormatSize(fileSize)}");
                        }

                        lastBytesForSpeed = bytesDownloaded;
                        lastProgressTime = now;
                    }

                    if ((now - lastFlushTime).TotalMilliseconds >= FLUSH_INTERVAL_MS)
                    {
                        await normalFileStream.FlushAsync(cancellationToken);
                        lastFlushTime = now;
                    }
                }

                await normalFileStream.FlushAsync(cancellationToken);

                if (fileSize <= 0) fileSize = bytesDownloaded;

                double finalElapsed2 = (DateTime.UtcNow - startTime).TotalSeconds;
                double finalSpeed2 = finalElapsed2 > 0 ? bytesDownloaded / finalElapsed2 : 0;

                DebugLog(debugLog, $"═══════════════════════════════════════════════════════════");
                DebugLog(debugLog, $"DOWNLOAD COMPLETE:");
                DebugLog(debugLog, $"  File: {actualSavePath}");
                DebugLog(debugLog, $"  Size: {bytesDownloaded} ({FormatSize(bytesDownloaded)})");
                DebugLog(debugLog, $"  Expected: {fileSize} ({FormatSize(fileSize)})");
                string matchStr = (bytesDownloaded == fileSize || fileSize <= 0) ? "YES" : "NO";
                DebugLog(debugLog, $"  Match: {matchStr}");
                DebugLog(debugLog, $"  Duration: {finalElapsed2:F1}s");
                DebugLog(debugLog, $"  Avg Speed: {FormatSpeed(finalSpeed2)}");
                DebugLog(debugLog, $"═══════════════════════════════════════════════════════════");

                Log($"[Engine] Completed: {bytesDownloaded} bytes in {finalElapsed2:F1}s, avg={FormatSpeed(finalSpeed2)}");
                Log($"[PASS] Stage 14: Download complete — {FormatSize(bytesDownloaded)} in {finalElapsed2:F1}s");

                Emit(DownloadEventType.Completed, sessionId, url,
                    fileSize: fileSize, bytesDownloaded: bytesDownloaded,
                    progress: 100, speed: 0, averageSpeed: finalSpeed2,
                    eta: 0, state: DownloadState.Completed,
                    elapsedSeconds: finalElapsed2, savePath: actualSavePath);
            }
            catch (OperationCanceledException)
            {
                DebugLog(debugLog, $"CANCELLED after {bytesDownloaded} bytes");
                Log($"[Engine] Cancelled: sessionId={sessionId}");
                Emit(DownloadEventType.Cancelled, sessionId, url,
                    bytesDownloaded: bytesDownloaded, state: DownloadState.Cancelled);
            }
            catch (Exception ex)
            {
                DebugLog(debugLog, $"FAILED: {ex}");
                Log($"[Engine] FAILED: {ex.Message}");
                Emit(DownloadEventType.Failed, sessionId, url,
                    bytesDownloaded: bytesDownloaded, state: DownloadState.Failed,
                    errorMessage: ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════
        // REDIRECT RESOLVER
        // ═══════════════════════════════════════════════════

        private async Task<RedirectResult> ResolveRedirectsAsync(string url,
            Dictionary<string, string>? headers = null,
            List<CookieData>? cookies = null,
            string? userAgent = null,
            string? referer = null,
            string? debugLog = null,
            List<RawHeader>? browserRawHeaders = null)
        {
            var result = new RedirectResult { OriginalUrl = url };
            var chain = new List<string> { url };
            string currentUrl = url;

            for (int hop = 0; hop < 15; hop++)
            {
                DebugLog(debugLog, $"REDIRECT RESOLVE hop={hop}: {currentUrl}");

                using var request = new HttpRequestMessage(HttpMethod.Head, currentUrl);
                ApplyHeaders(request, headers, cookies, userAgent, referer, hop > 0 ? chain[^1] : null, browserRawHeaders);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                DebugLog(debugLog, $"  → Status: {(int)response.StatusCode} {response.StatusCode}");
                DebugLog(debugLog, $"  → Content-Type: {response.Content.Headers.ContentType}");
                DebugLog(debugLog, $"  → Content-Length: {response.Content.Headers.ContentLength}");

                result.StatusCode = (int)response.StatusCode;
                result.ResponseHeaders = new Dictionary<string, string>();
                foreach (var h in response.Headers)
                    result.ResponseHeaders[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);
                foreach (var h in response.Content.Headers)
                    result.ResponseHeaders[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);

                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                    or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect
                    or HttpStatusCode.PermanentRedirect)
                {
                    string? location = response.Headers.Location?.ToString();
                    DebugLog(debugLog, $"  → Redirect to: {location}");
                    if (string.IsNullOrEmpty(location)) { result.FinalUrl = currentUrl; break; }
                    if (!location.StartsWith("http"))
                        location = new Uri(new Uri(currentUrl), location).ToString();
                    chain.Add(location);
                    currentUrl = location;
                    continue;
                }

                result.FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? currentUrl;
                result.ContentLength = response.Content.Headers.ContentLength ?? 0;
                result.AcceptRanges = response.Headers.Contains("Accept-Ranges");
                result.ETag = response.Headers.ETag?.Tag ?? "";
                result.LastModified = response.Content.Headers.LastModified?.ToString("R") ?? "";

                // Check if response is HTML (download page, not actual file)
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (contentType.Contains("text/html"))
                {
                    DebugLog(debugLog, $"⚠️ HTML response detected at URL: {currentUrl}");
                    DebugLog(debugLog, $"Content-Type: {contentType}");
                    result.IsHtml = true;
                }

                break;
            }

            result.RedirectChain = chain;
            result.HopCount = chain.Count - 1;
            return result;
        }

        // ═══════════════════════════════════════════════════
        // HEADER APPLICATION — USE EXACT BROWSER HEADERS
        // ═══════════════════════════════════════════════════

        private void ApplyHeaders(HttpRequestMessage request,
            Dictionary<string, string>? extensionHeaders = null,
            List<CookieData>? cookies = null,
            string? userAgent = null,
            string? referer = null,
            string? refererOverride = null,
            List<RawHeader>? browserRawHeaders = null)
        {
            // ═══ STRATEGY: Use EXACT browser headers if available ═══
            // This is the KEY to bypassing server-side bot detection.
            // The browser's request is "trusted" — we replicate it exactly.

            if (browserRawHeaders != null && browserRawHeaders.Count > 0)
            {
                Log($"[Engine] Using EXACT browser headers ({browserRawHeaders.Count} headers) — PASS");
                DebugLog(_currentDebugLog, $"Using EXACT browser headers ({browserRawHeaders.Count} headers)");
                foreach (var h in browserRawHeaders)
                {
                    string key = h.Name;
                    string val = h.Value;

                    // Skip headers that HttpClient manages automatically
                    string lk = key.ToLowerInvariant();
                    if (lk == "host" || lk == "content-length" || lk == "content-type")
                        continue;

                    // Handle Referer override (for redirect chains)
                    if (lk == "referer" && !string.IsNullOrEmpty(refererOverride))
                        val = refererOverride;

                    request.Headers.TryAddWithoutValidation(key, val);
                    DebugLog(_currentDebugLog, $"  Browser header: {key}: {val}");
                }

                // Add cookies from cookie list if Cookie header wasn't in browser headers
                bool hasCookieHeader = browserRawHeaders.Any(h =>
                    string.Equals(h.Name, "Cookie", StringComparison.OrdinalIgnoreCase));

                if (!hasCookieHeader && cookies != null && cookies.Count > 0)
                {
                    string cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                    request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                    DebugLog(_currentDebugLog, $"  Cookie from list: {cookieHeader[..Math.Min(200, cookieHeader.Length)]}...");
                }

                return;
            }

            // ═══ FALLBACK: Build headers manually (no browser data available) ═══
            Log($"[Engine] FALLBACK: No browser headers — using hardcoded fake headers (browserRawHeaders={browserRawHeaders?.Count ?? 0})");
            DebugLog(_currentDebugLog, $"No browser headers available — using fallback headers");

            string ua = userAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36";
            request.Headers.TryAddWithoutValidation("User-Agent", ua);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
            request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Chromium\";v=\"137\", \"Not/A)Brand\";v=\"24\", \"Google Chrome\";v=\"137\"");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");

            string referrerValue = refererOverride ?? referer ?? "";
            if (!string.IsNullOrEmpty(referrerValue))
            {
                request.Headers.TryAddWithoutValidation("Referer", referrerValue);
                try
                {
                    var reqHost = new Uri(request.RequestUri!.ToString()).Host;
                    var refHost = new Uri(referrerValue).Host;
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", reqHost == refHost ? "same-origin" : "cross-site");
                }
                catch { request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site"); }
            }

            if (cookies != null && cookies.Count > 0)
            {
                string cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }

            if (extensionHeaders != null)
            {
                string[] skip = { "user-agent", "accept", "accept-language", "accept-encoding",
                    "cookie", "connection", "host", "content-length", "content-type",
                    "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site", "sec-fetch-user",
                    "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
                    "upgrade-insecure-requests", "referer", "origin" };

                foreach (var kv in extensionHeaders)
                {
                    string key = kv.Key.ToLowerInvariant();
                    if (skip.Contains(key)) continue;
                    if (request.Headers.Contains(kv.Key)) continue;
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
        }

        // ═══════════════════════════════════════════════════
        // EVENT EMISSION
        // ═══════════════════════════════════════════════════

        private void Emit(DownloadEventType eventType, string sessionId, string url,
            long fileSize = 0, long bytesDownloaded = 0, double progress = 0,
            double speed = 0, double averageSpeed = 0, double eta = 0,
            DownloadState state = DownloadState.Downloading, bool resumeSupported = false,
            string? errorMessage = null, double elapsedSeconds = 0,
            int httpStatusCode = 0, string? finalUrl = null, string? savePath = null)
        {
            var args = new DownloadEventArgs
            {
                EventType = eventType,
                SessionId = sessionId,
                Url = url,
                Filename = Path.GetFileName(savePath ?? url),
                SavePath = savePath ?? string.Empty,
                FileSize = fileSize,
                BytesDownloaded = bytesDownloaded,
                Progress = progress,
                Speed = speed,
                AverageSpeed = averageSpeed,
                ETA = eta,
                State = state,
                ResumeSupported = resumeSupported,
                ErrorMessage = errorMessage,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ElapsedSeconds = elapsedSeconds,
                HttpStatusCode = httpStatusCode,
                FinalUrl = finalUrl
            };

            Log($"[Engine] Event: {eventType} | state={state} | progress={progress:F1}% | speed={FormatSpeed(speed)} | eta={FormatTime(eta)}");
            DownloadEvent?.Invoke(this, args);
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Console.WriteLine(line);
            try { File.AppendAllText(_logFile, line + "\n"); } catch { }
        }

        private void DebugLog(string? path, string message)
        {
            if (string.IsNullOrEmpty(path)) return;
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            try { File.AppendAllText(path, line + "\n"); } catch { }
        }

        public static string FormatSpeed(double bps)
        {
            if (bps <= 0) return "0 B/s";
            string[] s = ["B/s", "KB/s", "MB/s", "GB/s"];
            int i = 0; double v = bps;
            while (v >= 1024 && i < s.Length - 1) { v /= 1024; i++; }
            return $"{v:F1} {s[i]}";
        }

        public static string FormatTime(double sec)
        {
            if (sec <= 0 || double.IsInfinity(sec)) return "--:--";
            int m = (int)(sec / 60), s = (int)(sec % 60);
            if (m >= 60) { int h = m / 60; m %= 60; return $"{h:D2}:{m:D2}:{s:D2}"; }
            return $"{m:D2}:{s:D2}";
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] s = ["B", "KB", "MB", "GB"];
            int i = 0; double v = bytes;
            while (v >= 1024 && i < s.Length - 1) { v /= 1024; i++; }
            return $"{v:F1} {s[i]}";
        }

        private string? ExtractDownloadUrlFromHtml(string html)
        {
            try
            {
                // Look for common download link patterns in HTML
                // Pattern 1: <a href="..." download>
                var downloadLinkMatch = System.Text.RegularExpressions.Regex.Match(html,
                    @"href\s*=\s*[""']([^""']+)[""'][^>]*\s+download",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (downloadLinkMatch.Success)
                {
                    string url = downloadLinkMatch.Groups[1].Value;
                    if (url.StartsWith("http")) return url;
                }

                // Pattern 2: JavaScript redirect with file URL
                var jsRedirectMatch = System.Text.RegularExpressions.Regex.Match(html,
                    @"(?:window\.location(?:\.href)?|location\.replace|location\.assign)\s*=\s*[""']([^""']+\.(?:zip|rar|7z|exe|mp4|mkv|avi|pdf|iso|img))[""']",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (jsRedirectMatch.Success)
                {
                    string url = jsRedirectMatch.Groups[1].Value;
                    if (url.StartsWith("http")) return url;
                }

                // Pattern 3: Direct file URL in href (with file extension)
                var fileLinkMatch = System.Text.RegularExpressions.Regex.Match(html,
                    @"href\s*=\s*[""'](https?://[^""']+\.(?:zip|rar|7z|exe|mp4|mkv|avi|pdf|iso|img))[""']",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (fileLinkMatch.Success)
                {
                    return fileLinkMatch.Groups[1].Value;
                }

                // Pattern 4: data-url or download attribute with URL
                var dataUrlMatch = System.Text.RegularExpressions.Regex.Match(html,
                    @"data-url\s*=\s*[""'](https?://[^""']+)[""']",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (dataUrlMatch.Success)
                {
                    return dataUrlMatch.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                Log($"[Engine] HTML URL extraction error: {ex.Message}");
            }

            return null;
        }

        private static string ClassifyHtml(string html)
        {
            string lower = html.ToLowerInvariant();
            if (lower.Contains("captcha") || lower.Contains("recaptcha")) return "CAPTCHA";
            if (lower.Contains("cloudflare") && lower.Contains("challenge")) return "CLOUDFLARE CHALLENGE";
            if (lower.Contains("login") || lower.Contains("sign in")) return "LOGIN PAGE";
            if (lower.Contains("access denied") || lower.Contains("forbidden")) return "ACCESS DENIED";
            if (lower.Contains("download") && lower.Contains("button")) return "DOWNLOAD PAGE";
            if (lower.Contains("expired") || lower.Contains("invalid token")) return "EXPIRED TOKEN";
            if (lower.Contains("hotlink") || lower.Contains("direct link protection")) return "ANTI-HOTLINK";
            if (lower.Contains("<!doctype html")) return "HTML PAGE (unclassified)";
            return "HTML RESPONSE";
        }

        // ═══════════════════════════════════════════════════════════════
        // DEVELOPER DIAGNOSTICS — AUTOMATIC REQUEST COMPARISON
        // ═══════════════════════════════════════════════════════════════
        // When server returns HTML instead of file, this generates a
        // complete side-by-side comparison of what browser sent vs
        // what engine sent. This is the KEY to root cause analysis.

        private async Task GenerateDiagnosticsReport(
            string sessionId,
            string originalUrl,
            string engineFinalUrl,
            int engineStatusCode,
            string engineContentType,
            long engineContentLength,
            List<string> redirectChain,
            Dictionary<string, string> engineResponseHeaders,
            List<RawHeader>? browserRawHeaders,
            List<CookieData>? cookies,
            Dictionary<string, string>? extensionHeaders,
            string? userAgent,
            string? referer,
            string htmlContent,
            string? debugLog)
        {
            try
            {
                var diagDir = Path.Combine(AppContext.BaseDirectory, "diagnostics");
                Directory.CreateDirectory(diagDir);
                var diagFile = Path.Combine(diagDir, $"{sessionId}_comparison.txt");

                var sb = new StringBuilder();
                sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                sb.AppendLine("║              DEVELOPER DIAGNOSTICS — REQUEST COMPARISON REPORT              ║");
                sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                sb.AppendLine();
                sb.AppendLine($"Session ID:     {sessionId}");
                sb.AppendLine($"Timestamp:      {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"Original URL:   {originalUrl}");
                sb.AppendLine($"Engine Final URL: {engineFinalUrl}");
                sb.AppendLine();

                // ═══ SECTION 1: RESPONSE ANALYSIS ═══
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine(" SECTION 1: ENGINE RESPONSE ANALYSIS");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine($"  HTTP Status:        {engineStatusCode}");
                sb.AppendLine($"  Content-Type:       {engineContentType}");
                sb.AppendLine($"  Content-Length:     {engineContentLength} ({FormatSize(engineContentLength)})");
                sb.AppendLine($"  Redirect Hops:      {redirectChain.Count - 1}");
                if (redirectChain.Count > 1)
                {
                    sb.AppendLine($"  Redirect Chain:");
                    for (int i = 0; i < redirectChain.Count; i++)
                        sb.AppendLine($"    [{i}] {redirectChain[i]}");
                }
                sb.AppendLine();

                // HTML Analysis
                if (!string.IsNullOrEmpty(htmlContent))
                {
                    sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                    sb.AppendLine(" HTML RESPONSE ANALYSIS");
                    sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                    string htmlType = "Unknown";
                    string htmlLower = htmlContent.ToLowerInvariant();
                    if (htmlLower.Contains("captcha") || htmlLower.Contains("recaptcha")) htmlType = "CAPTCHA PAGE";
                    else if (htmlLower.Contains("login") || htmlLower.Contains("sign in") || htmlLower.Contains("signin")) htmlType = "LOGIN PAGE";
                    else if (htmlLower.Contains("access denied") || htmlLower.Contains("forbidden") || htmlLower.Contains("403")) htmlType = "ACCESS DENIED";
                    else if (htmlLower.Contains("cloudflare") && htmlLower.Contains("challenge")) htmlType = "CLOUDFLARE CHALLENGE";
                    else if (htmlLower.Contains("download") && htmlLower.Contains("button")) htmlType = "DOWNLOAD PAGE (with download button)";
                    else if (htmlLower.Contains("expired") || htmlLower.Contains("invalid") || htmlLower.Contains("error")) htmlType = "ERROR/EXPIRED PAGE";
                    else if (htmlLower.Contains("hotlink") || htmlLower.Contains("direct link")) htmlType = "ANTI-HOTLINK PAGE";

                    sb.AppendLine($"  HTML Type Detected: {htmlType}");
                    sb.AppendLine($"  HTML Size:          {htmlContent.Length} chars");
                    sb.AppendLine();

                    // Extract title
                    var titleMatch = System.Text.RegularExpressions.Regex.Match(htmlContent, @"<title[^>]*>(.*?)</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (titleMatch.Success)
                        sb.AppendLine($"  Page Title:         {titleMatch.Groups[1].Value.Trim()}");
                    sb.AppendLine();

                    // Extract meta description
                    var metaMatch = System.Text.RegularExpressions.Regex.Match(htmlContent, @"<meta[^>]*name=[""']description[""'][^>]*content=[""']([^""]*)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (metaMatch.Success)
                        sb.AppendLine($"  Meta Description:   {metaMatch.Groups[1].Value.Trim()}");
                    sb.AppendLine();

                    // Extract all form actions
                    sb.AppendLine("  Form Actions Found:");
                    var formMatches = System.Text.RegularExpressions.Regex.Matches(htmlContent, @"<form[^>]*action=[""']([^""]*)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    foreach (System.Text.RegularExpressions.Match m in formMatches)
                        sb.AppendLine($"    → {m.Groups[1].Value}");
                    sb.AppendLine();

                    // Extract all links with file extensions
                    sb.AppendLine("  Download Links Found:");
                    var linkMatches = System.Text.RegularExpressions.Regex.Matches(htmlContent, @"href=[""'](https?://[^""']*\.(?:zip|rar|7z|exe|mp4|mkv|avi|pdf|iso|img|mp3|flac|tar\.gz))[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    foreach (System.Text.RegularExpressions.Match m in linkMatches)
                        sb.AppendLine($"    → {m.Groups[1].Value}");
                    sb.AppendLine();

                    // Extract JavaScript redirects
                    sb.AppendLine("  JavaScript Redirects:");
                    var jsMatches = System.Text.RegularExpressions.Regex.Matches(htmlContent, @"(?:window\.location(?:\.href)?|location\.(?:replace|assign)|document\.location)\s*=\s*[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    foreach (System.Text.RegularExpressions.Match m in jsMatches)
                        sb.AppendLine($"    → {m.Groups[1].Value}");
                    if (jsMatches.Count == 0)
                        sb.AppendLine("    (none found)");
                    sb.AppendLine();

                    // First 2000 chars of HTML
                    sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                    sb.AppendLine(" HTML PREVIEW (first 2000 chars)");
                    sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                    sb.AppendLine(htmlContent[..Math.Min(2000, htmlContent.Length)]);
                    sb.AppendLine();
                }

                // ═══ SECTION 2: BROWSER HEADERS (what Chrome actually sent) ═══
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine(" SECTION 2: BROWSER REQUEST HEADERS (captured from Chrome)");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                if (browserRawHeaders != null && browserRawHeaders.Count > 0)
                {
                    foreach (var h in browserRawHeaders)
                        sb.AppendLine($"  {h.Name}: {h.Value}");
                }
                else
                {
                    sb.AppendLine("  (NO browser headers captured by extension!)");
                }
                sb.AppendLine();

                // ═══ SECTION 3: ENGINE REQUEST HEADERS (what we actually sent) ═══
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine(" SECTION 3: ENGINE REQUEST HEADERS (what we sent to server)");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                if (browserRawHeaders != null && browserRawHeaders.Count > 0)
                {
                    sb.AppendLine("  [Using EXACT browser headers]");
                    foreach (var h in browserRawHeaders)
                        sb.AppendLine($"  {h.Name}: {h.Value}");
                }
                else
                {
                    sb.AppendLine("  [Using FALLBACK headers — no browser headers available]");
                    sb.AppendLine($"  User-Agent: {userAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/137.0.0.0"}");
                    sb.AppendLine($"  Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                    sb.AppendLine($"  Accept-Language: en-US,en;q=0.9");
                    sb.AppendLine($"  Accept-Encoding: gzip, deflate, br");
                    sb.AppendLine($"  Referer: {referer ?? "(none)"}");
                    sb.AppendLine($"  Sec-Fetch-Dest: document");
                    sb.AppendLine($"  Sec-Fetch-Mode: navigate");
                    sb.AppendLine($"  Sec-Fetch-User: ?1");
                    sb.AppendLine($"  Upgrade-Insecure-Requests: 1");
                }
                sb.AppendLine();

                // Extension headers (legacy)
                if (extensionHeaders != null && extensionHeaders.Count > 0)
                {
                    sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                    sb.AppendLine(" SECTION 3b: EXTENSION HEADERS (legacy dictionary format)");
                    sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                    foreach (var kv in extensionHeaders)
                        sb.AppendLine($"  {kv.Key}: {kv.Value}");
                    sb.AppendLine();
                }

                // ═══ SECTION 4: COOKIES ═══
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine(" SECTION 4: COOKIES SENT TO ENGINE");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                if (cookies != null && cookies.Count > 0)
                {
                    foreach (var c in cookies)
                        sb.AppendLine($"  {c.Name}={c.Value} (domain={c.Domain}, path={c.Path}, secure={c.Secure}, httpOnly={c.HttpOnly}, sameSite={c.SameSite})");
                }
                else
                {
                    sb.AppendLine("  (NO cookies available!)");
                }
                sb.AppendLine();

                // ═══ SECTION 5: ENGINE RESPONSE HEADERS ═══
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine(" SECTION 5: ENGINE RESPONSE HEADERS (what server sent back)");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                if (engineResponseHeaders.Count > 0)
                {
                    foreach (var kv in engineResponseHeaders)
                        sb.AppendLine($"  {kv.Key}: {kv.Value}");
                }
                else
                {
                    sb.AppendLine("  (no response headers captured)");
                }
                sb.AppendLine();

                // ═══ SECTION 6: DIFF ANALYSIS ═══
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine(" SECTION 6: DIFF ANALYSIS — What browser has that engine doesn't");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");

                if (browserRawHeaders != null && browserRawHeaders.Count > 0)
                {
                    var browserHeaderDict = browserRawHeaders.ToDictionary(h => h.Name.ToLowerInvariant(), h => h.Value, StringComparer.OrdinalIgnoreCase);

                    // Check critical headers
                    string[] criticalHeaders = {
                        "cookie", "authorization", "x-csrf-token", "x-requested-with",
                        "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site", "sec-fetch-user",
                        "referer", "origin", "accept", "accept-encoding", "accept-language",
                        "user-agent", "cache-control", "pragma", "range",
                        "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform"
                    };

                    sb.AppendLine("  Critical Header Check:");
                    foreach (string ch in criticalHeaders)
                    {
                        bool has = browserHeaderDict.ContainsKey(ch);
                        string val = has ? browserHeaderDict[ch] : "(MISSING)";
                        string checkMark = has ? "OK" : "MISSING";
                        sb.AppendLine($"    {ch,-30} = {checkMark} {val[..Math.Min(80, val.Length)]}");
                    }
                }
                else
                {
                    sb.AppendLine("  Cannot perform diff — no browser headers captured!");
                    sb.AppendLine("  THIS IS LIKELY THE ROOT CAUSE: Extension is not capturing headers.");
                }
                sb.AppendLine();

                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine(" END OF DIAGNOSTICS REPORT");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");

                await File.WriteAllTextAsync(diagFile, sb.ToString());
                Log($"[Engine] Diagnostics report saved: {diagFile}");
                DebugLog(debugLog, $"DIAGNOSTICS REPORT: {diagFile}");
            }
            catch (Exception ex)
            {
                Log($"[Engine] Diagnostics report FAILED: {ex.Message}");
            }
        }
    }

    internal class RedirectResult
    {
        public string OriginalUrl { get; set; } = string.Empty;
        public string FinalUrl { get; set; } = string.Empty;
        public List<string> RedirectChain { get; set; } = new();
        public int HopCount { get; set; }
        public int StatusCode { get; set; }
        public long ContentLength { get; set; }
        public bool AcceptRanges { get; set; }
        public string ETag { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
        public Dictionary<string, string> ResponseHeaders { get; set; } = new();
        public bool IsHtml { get; set; }
    }
}
