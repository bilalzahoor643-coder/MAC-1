using FontAwesome.WPF;
using System.Collections.Generic;

namespace MAC_1.Services
{
    // 1. Configuration for Front Buttons (Pause, Resume, etc.)
    public class FrontButtonConfig
    {
        public string ToolTip { get; set; } = string.Empty;
        public FontAwesomeIcon Icon { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public string HexColor { get; set; } = "#FFFFFF";
    }

    // 2. Configuration for 3-Dot Menu Items
    public class MenuItemConfig
    {
        public string Text { get; set; } = string.Empty;
        public FontAwesomeIcon Icon { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true; // For ZIP/RAR logic
    }

    public static class DownloadUIFactory
    {
        // FRONT BUTTONS LOGIC
        public static List<FrontButtonConfig> GetFrontButtons(string state)
        {
            var buttons = new List<FrontButtonConfig>();
            if (string.IsNullOrEmpty(state)) return buttons;

            if (state.Contains("Downloading") || state.Contains("Connecting"))
            {
                buttons.Add(new FrontButtonConfig { ToolTip = "Pause", Icon = FontAwesomeIcon.Pause, ActionName = "Pause", HexColor = "#0097E6" });
                buttons.Add(new FrontButtonConfig { ToolTip = "Cancel", Icon = FontAwesomeIcon.Close, ActionName = "Cancel", HexColor = "#E84118" });
            }
            else if (state.Contains("Completed") || state.Contains("Finished"))
            {
                buttons.Add(new FrontButtonConfig { ToolTip = "Open File", Icon = FontAwesomeIcon.File, ActionName = "OpenFile", HexColor = "#0097E6" });
                buttons.Add(new FrontButtonConfig { ToolTip = "Open Folder", Icon = FontAwesomeIcon.FolderOpen, ActionName = "OpenFolder", HexColor = "#0097E6" });
            }
            else if (state.Contains("Paused"))
            {
                buttons.Add(new FrontButtonConfig { ToolTip = "Resume", Icon = FontAwesomeIcon.Play, ActionName = "Resume", HexColor = "#0097E6" });
                buttons.Add(new FrontButtonConfig { ToolTip = "Cancel", Icon = FontAwesomeIcon.Close, ActionName = "Cancel", HexColor = "#E84118" });
            }
            else if (state.Contains("Queued") || state.Contains("Idle"))
            {
                buttons.Add(new FrontButtonConfig { ToolTip = "Start Now", Icon = FontAwesomeIcon.Play, ActionName = "Start", HexColor = "#0097E6" });
                buttons.Add(new FrontButtonConfig { ToolTip = "Cancel", Icon = FontAwesomeIcon.Close, ActionName = "Cancel", HexColor = "#E84118" });
            }
            else if (state.Contains("Error") || state.Contains("Failed"))
            {
                buttons.Add(new FrontButtonConfig { ToolTip = "Retry", Icon = FontAwesomeIcon.Refresh, ActionName = "Retry", HexColor = "#0097E6" });
                buttons.Add(new FrontButtonConfig { ToolTip = "Cancel", Icon = FontAwesomeIcon.Close, ActionName = "Cancel", HexColor = "#E84118" });
            }

            return buttons;
        }

        // 3-DOT MENU ITEMS LOGIC
        public static List<MenuItemConfig> GetMenuItems(string state, bool isArchive)
        {
            var items = new List<MenuItemConfig>();

            // Common items for almost all states
            items.Add(new MenuItemConfig { Text = "Properties", Icon = FontAwesomeIcon.InfoCircle, ActionName = "Props" });
            items.Add(new MenuItemConfig { Text = "Copy URL", Icon = FontAwesomeIcon.Link, ActionName = "CopyUrl" });
            items.Add(new MenuItemConfig { Text = "Delete", Icon = FontAwesomeIcon.Trash, ActionName = "Delete" });
            items.Add(new MenuItemConfig { Text = "Copy File Name", Icon = FontAwesomeIcon.Edit, ActionName = "CopyName" });

            // State specific items
            if (state.Contains("Downloading"))
            {
                items.Add(new MenuItemConfig { Text = "Auto Shutdown", Icon = FontAwesomeIcon.PowerOff, ActionName = "AutoOff" });
            }

            if (state.Contains("Completed") || state.Contains("Finished"))
            {
                // SPECIAL ZIP/RAR LOGIC: Button hamesha dikhega magar enabled sirf archive par hoga
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