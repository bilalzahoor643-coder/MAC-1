using System;
using System.IO;
using System.Text.Json;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class SettingsService
    {
        private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
        public static SettingsService Instance => _instance.Value;

        private readonly string _settingsPath;

        public AppSettings Settings => DataService.Instance.Settings;

        private SettingsService()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MAC-1", "settings.json");
        }

        public void UpdateMaxSimultaneous(int max)
        {
            Settings.MaxSimultaneousDownloads = max;
            Save();
        }

        public void UpdateDefaultConnections(int connections)
        {
            Settings.DefaultConnections = connections;
            Save();
        }

        public void UpdateDefaultSavePath(string path)
        {
            Settings.DefaultSavePath = path;
            Save();
        }

        public void ToggleAutoStart(bool enabled)
        {
            Settings.AutoStartDownloads = enabled;
            Save();
        }

        public void ToggleAutoClose(bool enabled)
        {
            Settings.AutoCloseCompleted = enabled;
            Save();
        }

        public void Save()
        {
            DataService.Instance.SaveSettings();
        }
    }
}
