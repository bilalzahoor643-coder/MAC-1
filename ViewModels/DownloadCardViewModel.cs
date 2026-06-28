using MAC_1.Models;
using MAC_1.Services;
using MAC_1.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics; // Isay top par add karein

namespace MAC_1.ViewModels
{
    public class DownloadCardViewModel : ViewModelBase
    {
        // 1. Fields
        private bool _isExpanded = false;
        private List<FrontButtonConfig> _frontButtons = new List<FrontButtonConfig>();
        private List<MenuItemConfig> _menuItems = new List<MenuItemConfig>();

        // 2. Properties
        public DownloadTask Task { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
        }

        // Card ke samne wale 2 buttons
        public List<FrontButtonConfig> FrontButtons
        {
            get => _frontButtons;
            set { _frontButtons = value; OnPropertyChanged(nameof(FrontButtons)); }
        }

        // 3-Dot menu ke andar wale items
        public List<MenuItemConfig> MenuItems
        {
            get => _menuItems;
            set { _menuItems = value; OnPropertyChanged(nameof(MenuItems)); }
        }

        public string ProgressPercent => $"{Task.Progress}%";
        public string SizeDisplay => $"{FormatSize(Task.DownloadedSize)} / {FormatSize(Task.TotalSize)}";

        // 3. Commands
        public ICommand ToggleExpandCommand { get; }
        public ICommand ActionCommand { get; }

        // 4. Constructor
        public DownloadCardViewModel(DownloadTask task)
        {
            Task = task;

            // Shuruat mein UI ko configure karein
            UpdateUI();

            // Task ki properties par nazar rakhna
            Task.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Task.Progress)) OnPropertyChanged(nameof(ProgressPercent));
                if (e.PropertyName == nameof(Task.DownloadedSize)) OnPropertyChanged(nameof(SizeDisplay));

                // AGAR STATE YA FILE TYPE BADLE TO UI REFRESH KARO
                if (e.PropertyName == nameof(Task.State) || e.PropertyName == nameof(Task.IsArchive))
                {
                    UpdateUI();
                }
            };

            ToggleExpandCommand = new RelayCommand(o => { IsExpanded = !IsExpanded; });
            ActionCommand = new RelayCommand(async o => await HandleButtonAction(o?.ToString()));
        }

        // 5. Factory Se Naya UI Mangwana
        private void UpdateUI()
        {
            // Front Buttons refresh karein
            FrontButtons = DownloadUIFactory.GetFrontButtons(Task.State);

            // 3-Dot Menu Items refresh karein (IsArchive check ke saath)
            MenuItems = DownloadUIFactory.GetMenuItems(Task.State, Task.IsArchive);
        }

        // 6. Central Action Handler (Pause, Resume, Extract, etc.)
        private async Task HandleButtonAction(string? action)
        {
            if (string.IsNullOrEmpty(action)) return;

            switch (action)
            {
                case "Pause":
                    // Engine pause logic yahan aayegi
                    break;
                case "Resume":
                case "Start":
                case "Retry":
                    await DownloadService.Instance.StartDownloadAsync(Task);
                    break;
                case "OpenFile":
                    try
                    {
                        string fullPath = Path.Combine(Task.SavePath, Task.Filename);
                        if (File.Exists(fullPath))
                        {
                            // File ko uske default app mein kholna
                            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                        }
                    }
                    catch { /* File ghum gayi ya koi aur masla */ }
                    break;

                case "OpenFolder":
                    try
                    {
                        string fullPath = Path.Combine(Task.SavePath, Task.Filename);
                        if (File.Exists(fullPath))
                        {
                            // Folder kholna aur file ko "Highlight" karna
                            Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                        }
                        else if (Directory.Exists(Task.SavePath))
                        {
                            // Agar file nahi hai toh sirf folder khol do
                            Process.Start("explorer.exe", Task.SavePath);
                        }
                    }
                    catch { }
                    break;
                case "Extract":
                    // Zip extraction logic yahan aayegi
                    break;
                case "Delete":
                    // Task delete logic
                    break;
                    // Baqi actions (CopyURL, Props etc.) yahan add honge
            }
        }

        private string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suf = { "B", "KB", "MB", "GB", "TB" };
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return num.ToString() + " " + suf[place];
        }
    }
}