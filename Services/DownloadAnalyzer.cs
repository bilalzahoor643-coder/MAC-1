using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MAC_1.Services
{
    // Analysis ke baad jo data milega uske liye aik choti class
    public class AnalysisResult
    {
        public string FinalUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = "downloaded_file";
        public long TotalSize { get; set; } = -1;
        public bool SupportsRange { get; set; } = false;
    }

    public class DownloadAnalyzer
    {
        private readonly HttpClient _httpClient;

        public DownloadAnalyzer()
        {
            // Redirects ko khud handle karne ke liye handler
            var handler = new HttpClientHandler() { AllowAutoRedirect = true };
            _httpClient = new HttpClient(handler);

            // Sab se zaroori: Browser ki identity (User-Agent)
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public async Task<AnalysisResult> AnalyzeUrlAsync(string url, string? referrer = null)
        {
            var result = new AnalysisResult { FinalUrl = url };

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                // Security Bypass: Referrer set karna (WiseCleaner jaise servers ke liye)
                if (!string.IsNullOrEmpty(referrer))
                {
                    request.Headers.Referrer = new Uri(referrer);
                }

                // Sirf headers mangwayein, poori file nahi
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                // 1. Asli URL (Redirects ke baad wala)
                result.FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

                // 2. Asli Filename (Content-Disposition header se)
                var contentDisposition = response.Content.Headers.ContentDisposition;
                if (contentDisposition != null && !string.IsNullOrEmpty(contentDisposition.FileName))
                {
                    result.FileName = contentDisposition.FileName.Trim('"');
                }
                else
                {
                    // Agar header na ho toh URL ke aakhri hisse se naam nikalna
                    result.FileName = Path.GetFileName(new Uri(result.FinalUrl).LocalPath);
                }

                // 3. File Size
                result.TotalSize = response.Content.Headers.ContentLength ?? -1;

                // 4. Multi-threading support check (Accept-Ranges)
                result.SupportsRange = response.Headers.AcceptRanges.Contains("bytes");

                return result;
            }
            catch (Exception)
            {
                return result; // Default values wapis bhej dein agar error aaye
            }
        }
    }
}