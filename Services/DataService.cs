using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class DataService
    {
        private static readonly Lazy<DataService> _instance = new(() => new DataService());
        public static DataService Instance => _instance.Value;

        private readonly string _dataFolder;
        private readonly string _categoriesFile;
        private readonly string _settingsFile;
        private readonly string _historyFile;

        public ObservableCollection<DownloadTask> Downloads { get; } = new();
        public List<Category> Categories { get; private set; } = new();
        public AppSettings Settings { get; private set; } = new();

        public int TotalDownloads => Downloads.Count;
        public int CompletedDownloads => Downloads.Count(d => d.State == DownloadState.Completed);
        public int ActiveDownloads => Downloads.Count(d => d.State == DownloadState.Downloading);
        public int FailedDownloads => Downloads.Count(d => d.State == DownloadState.Failed);
        public long TotalSizeDownloaded => Downloads.Where(d => d.State == DownloadState.Completed).Sum(d => d.TotalSize);

        public event Action<DownloadTask>? DownloadAdded;
        public event Action<DownloadTask>? DownloadRemoved;
        public event Action? StatsChanged;

        private DataService()
        {
            _dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MAC-1");
            _categoriesFile = Path.Combine(_dataFolder, "categories.json");
            _settingsFile = Path.Combine(_dataFolder, "settings.json");
            _historyFile = Path.Combine(_dataFolder, "history.json");

            Directory.CreateDirectory(_dataFolder);
            LoadCategories();
            LoadSettings();
            LoadHistory();

            Downloads.CollectionChanged += (_, _) => StatsChanged?.Invoke();
        }

        public void AddDownload(DownloadTask task)
        {
            task.CheckIfArchive();
            Downloads.Add(task);
            DownloadAdded?.Invoke(task);
            StatsChanged?.Invoke();
        }

        public void RemoveDownload(string taskId)
        {
            var task = Downloads.FirstOrDefault(d => d.Id == taskId);
            if (task != null)
            {
                Downloads.Remove(task);
                DownloadRemoved?.Invoke(task);
                StatsChanged?.Invoke();
            }
        }

        public DownloadTask? GetDownload(string taskId)
            => Downloads.FirstOrDefault(d => d.Id == taskId);

        public Category? GetCategoryByName(string name)
            => Categories.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public Category GetDefaultCategory()
            => Categories.FirstOrDefault(c => c.IsDefault) ?? Categories.First();

        public string GetSavePathForCategory(string categoryName)
        {
            var cat = GetCategoryByName(categoryName);
            return cat?.FolderPath ?? Settings.DefaultSavePath;
        }

        private void SaveCategories()
        {
            try
            {
                var json = JsonSerializer.Serialize(Categories, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_categoriesFile, json);
            }
            catch { }
        }

        public void AddCategory(Category category)
        {
            Categories.Add(category);
            SaveCategories();
        }

        public void RemoveCategory(string categoryId)
        {
            Categories.RemoveAll(c => c.Id == categoryId);
            SaveCategories();
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFile, json);
            }
            catch { }
        }

        private void LoadCategories()
        {
            try
            {
                if (File.Exists(_categoriesFile))
                {
                    var json = File.ReadAllText(_categoriesFile);
                    Categories = JsonSerializer.Deserialize<List<Category>>(json) ?? new();
                    return;
                }
            }
            catch { }

            Categories = Category.GetDefaults(Settings.DefaultSavePath);
            SaveCategories();
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
                    return;
                }
            }
            catch { }

            Settings = new AppSettings();
            SaveSettings();
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFile))
                {
                    var json = File.ReadAllText(_historyFile);
                    var tasks = JsonSerializer.Deserialize<List<DownloadTask>>(json) ?? new();
                    foreach (var task in tasks)
                    {
                        task.CheckIfArchive();
                        Downloads.Add(task);
                    }
                }
            }
            catch { }
        }

        public void SaveHistory()
        {
            try
            {
                var completed = Downloads.Where(d => d.State == DownloadState.Completed).ToList();
                var json = JsonSerializer.Serialize(completed, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_historyFile, json);
            }
            catch { }
        }
    }
}
