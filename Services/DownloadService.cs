using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class DownloadService : IDownloadService
    {
        private static readonly DownloadService _instance = new DownloadService();
        public static DownloadService Instance => _instance;

        private readonly HttpClient _httpClient;
        private readonly DownloadAnalyzer _analyzer;

        public ObservableCollection<DownloadTask> Downloads { get; set; }

        public DownloadService()
        {
            var handler = new HttpClientHandler() { AllowAutoRedirect = true };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromHours(4) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            _analyzer = new DownloadAnalyzer();
            Downloads = new ObservableCollection<DownloadTask>();
        }

        public void AddDownload(string url, string fileName, long fileSize, string savePath)
        {
            // --- 1. SMART PATH LOGIC ---
            string finalPath = savePath;

            // Agar rasta khali hai toh user ka Downloads folder uthao
            if (string.IsNullOrWhiteSpace(finalPath))
            {
                finalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            // Check karo ke kya raste ke aakhir mein "MAC_1" pehle se hai?
            // TrimEnd('\\') isliye taake "C:\Downloads\" aur "C:\Downloads" dono handle hon
            if (!finalPath.TrimEnd(Path.DirectorySeparatorChar).EndsWith("MAC_1", StringComparison.OrdinalIgnoreCase))
            {
                finalPath = Path.Combine(finalPath, "MAC_1");
            }

            // Folder create karna agar nahi hai
            if (!Directory.Exists(finalPath))
            {
                Directory.CreateDirectory(finalPath);
            }

            // --- 2. FILENAME FIX ---
            if (string.IsNullOrEmpty(fileName) || fileName.ToLower() == "download")
            {
                try { fileName = Path.GetFileName(new Uri(url).LocalPath); }
                catch { fileName = "MAC1_File_" + DateTime.Now.ToString("HHmmss"); }
            }

            // Naya task finalPath ke saath create karein
            var newTask = new DownloadTask(url, fileName, fileSize, finalPath);
            Downloads.Add(newTask);

            _ = StartDownloadAsync(newTask);
        }

        public async Task StartDownloadAsync(DownloadTask task)
        {
            try
            {
                task.State = "Connecting...";

                // Analyzer se asli file ka rasta aur naam mangwayein
                var analysis = await _analyzer.AnalyzeUrlAsync(task.Url);
                task.Url = analysis.FinalUrl;

                // Agar analyzer ne behtar naam dhoonda hai toh use update karein
                if (analysis.FileName != "downloaded_file")
                    task.Filename = analysis.FileName;

                if (analysis.TotalSize > 0) task.TotalSize = analysis.TotalSize;

                task.State = "Downloading...";

                // Full path for file stream
                string fullFilePath = Path.Combine(task.SavePath!, task.Filename!);

                using var response = await _httpClient.GetAsync(task.Url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(fullFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 16384, true);

                var buffer = new byte[16384];
                long totalRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;

                    task.DownloadedSize = totalRead;
                    if (task.TotalSize > 0)
                        task.Progress = Math.Round((double)totalRead / task.TotalSize * 100, 1);

                    task.Speed = "Streaming...";
                }

                task.State = "Completed";
                task.Progress = 100;
            }
            catch (Exception ex)
            {
                task.State = "Error";
                task.ErrorMessage = ex.Message;
            }
        }
    }
}