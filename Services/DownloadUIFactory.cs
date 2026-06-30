using FontAwesome.WPF;
using System.Collections.Generic;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class FrontButtonConfig
    {
        public string ToolTip { get; set; } = string.Empty;
        public FontAwesomeIcon Icon { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public string HexColor { get; set; } = "#FFFFFF";
    }

    public class MenuItemConfig
    {
        public string Text { get; set; } = string.Empty;
        public FontAwesomeIcon Icon { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }

    public static class DownloadUIFactory
    {
        public static List<FrontButtonConfig> GetFrontButtons(DownloadState state)
        {
            var buttons = new List<FrontButtonConfig>();

            switch (state)
            {
                case DownloadState.Downloading:
                case DownloadState.Waiting:
                    buttons.Add(new FrontButtonConfig { ToolTip = "Pause", Icon = FontAwesomeIcon.Pause, ActionName = "Pause", HexColor = "#0097E6" });
                    buttons.Add(new FrontButtonConfig { ToolTip = "Cancel", Icon = FontAwesomeIcon.Close, ActionName = "Cancel", HexColor = "#E84118" });
                    break;
                case DownloadState.Completed:
                    buttons.Add(new FrontButtonConfig { ToolTip = "Open File", Icon = FontAwesomeIcon.File, ActionName = "OpenFile", HexColor = "#0097E6" });
                    buttons.Add(new FrontButtonConfig { ToolTip = "Open Folder", Icon = FontAwesomeIcon.FolderOpen, ActionName = "OpenFolder", HexColor = "#0097E6" });
                    break;
                case DownloadState.Paused:
                    buttons.Add(new FrontButtonConfig { ToolTip = "Resume", Icon = FontAwesomeIcon.Play, ActionName = "Resume", HexColor = "#0097E6" });
                    buttons.Add(new FrontButtonConfig { ToolTip = "Cancel", Icon = FontAwesomeIcon.Close, ActionName = "Cancel", HexColor = "#E84118" });
                    break;
                case DownloadState.Idle:
                case DownloadState.Queued:
                    buttons.Add(new FrontButtonConfig { ToolTip = "Start Now", Icon = FontAwesomeIcon.Play, ActionName = "Start", HexColor = "#0097E6" });
                    buttons.Add(new FrontButtonConfig { ToolTip = "Cancel", Icon = FontAwesomeIcon.Close, ActionName = "Cancel", HexColor = "#E84118" });
                    break;
                case DownloadState.Failed:
                    buttons.Add(new FrontButtonConfig { ToolTip = "Retry", Icon = FontAwesomeIcon.Refresh, ActionName = "Retry", HexColor = "#0097E6" });
                    buttons.Add(new FrontButtonConfig { ToolTip = "Cancel", Icon = FontAwesomeIcon.Close, ActionName = "Cancel", HexColor = "#E84118" });
                    break;
            }

            return buttons;
        }

        public static List<MenuItemConfig> GetMenuItems(DownloadState state, bool isArchive)
        {
            var items = new List<MenuItemConfig>();

            items.Add(new MenuItemConfig { Text = "Properties", Icon = FontAwesomeIcon.InfoCircle, ActionName = "Props" });
            items.Add(new MenuItemConfig { Text = "Copy URL", Icon = FontAwesomeIcon.Link, ActionName = "CopyUrl" });
            items.Add(new MenuItemConfig { Text = "Delete", Icon = FontAwesomeIcon.Trash, ActionName = "Delete" });
            items.Add(new MenuItemConfig { Text = "Copy File Name", Icon = FontAwesomeIcon.Edit, ActionName = "CopyName" });

            if (state == DownloadState.Downloading)
            {
                items.Add(new MenuItemConfig { Text = "Auto Shutdown", Icon = FontAwesomeIcon.PowerOff, ActionName = "AutoOff" });
            }

            if (state == DownloadState.Completed)
            {
                items.Add(new MenuItemConfig
                {
                    Text = "Extract Here",
                    Icon = FontAwesomeIcon.FileZipOutline,
                    ActionName = "Extract",
                    IsEnabled = isArchive
                });
                items.Add(new MenuItemConfig { Text = "Redownload", Icon = FontAwesomeIcon.Refresh, ActionName = "Redownload" });
            }

            items.Add(new MenuItemConfig { Text = "Progress Color", Icon = FontAwesomeIcon.PaintBrush, ActionName = "SetColor" });

            return items;
        }
    }
}
