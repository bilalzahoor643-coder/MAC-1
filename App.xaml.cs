using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using MAC_1.Services;

namespace MAC_1
{
    public partial class App : Application
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly string _logFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MAC-1", "wpf-app.log");

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Log("App starting...");

            var dataService = DataService.Instance;
            var settingsService = SettingsService.Instance;
            var cardService = CardService.Instance;
            var popupService = PopupService.Instance;

            Log("Registering DownloadSessionReceived handler...");
            ExtensionService.Instance.DownloadSessionReceived += session =>
            {
                Log($"[PASS] Stage 5: DownloadSessionReceived — filename={session.Filename}, sessionId={session.SessionId}, url={session.Url}");
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        Log("[PASS] Stage 6: Opening popup...");
                        popupService.ShowDownloadPopup(session);
                        Log("[PASS] Stage 7: Popup opened");
                    });
                }
                catch (Exception ex)
                {
                    Log($"[FAIL] Stage 6/7: ShowDownloadPopup FAILED: {ex.Message}\n{ex.StackTrace}");
                }
            };

            Log("Registering SizeUpdateReceived handler...");
            ExtensionService.Instance.SizeUpdateReceived += (url, fileSize) =>
            {
                Dispatcher.Invoke(() => popupService.UpdateFileSize(url, fileSize));
            };

            Log("Registering StatusChanged handler...");
            ExtensionService.Instance.StatusChanged += status =>
            {
                Log($"StatusChanged: {status}");
            };

            Log("Registering EngineEventReceived handler...");
            ExtensionService.Instance.EngineEventReceived += evt =>
            {
                Log($"[PASS] Stage 11: EngineEvent — type={evt.EventType} sessionId={evt.SessionId} state={evt.State} progress={evt.Progress:F1}%");
                try
                {
                    Dispatcher.Invoke(() => popupService.HandleEngineEvent(evt));
                }
                catch (Exception ex)
                {
                    Log($"[FAIL] Stage 12: HandleEngineEvent FAILED: {ex.Message}");
                }
            };

            Log("Starting ExtensionService...");
            ExtensionService.Instance.Start();

            await RestoreSessionsFromDatabaseAsync(popupService);
        }

        private async Task RestoreSessionsFromDatabaseAsync(PopupService popupService)
        {
            try
            {
                await Task.Delay(2000);
                var response = await _http.GetStringAsync("http://127.0.0.1:57575/api/sessions");
                var json = JsonSerializer.Deserialize<JsonElement>(response);
                if (json.GetProperty("success").GetBoolean())
                {
                    int count = json.GetProperty("sessions").GetArrayLength();
                    if (count > 0) Log($"Restored {count} sessions from database");
                }
            }
            catch (Exception ex)
            {
                Log($"Could not restore sessions: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ExtensionService.Instance.Stop();
            DataService.Instance.SaveHistory();
            base.OnExit(e);
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                File.AppendAllText(_logFile, line + "\n");
            }
            catch { }
        }
    }
}
