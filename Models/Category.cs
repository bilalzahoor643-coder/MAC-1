using System.Collections.Generic;

namespace MAC_1.Models
{
    public class Category
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string Icon { get; set; } = "Archive";
        public string Color { get; set; } = "#6366F1";
        public bool IsDefault { get; set; }
        public bool UseAsDefaultLocation { get; set; } = true;

        public static List<Category> GetDefaults(string baseDownloadsPath)
        {
            return new List<Category>
            {
                new() { Name = "All Downloads", FolderPath = baseDownloadsPath, Icon = "Download", Color = "#2563EB", IsDefault = true },
                new() { Name = "Programs", FolderPath = System.IO.Path.Combine(baseDownloadsPath, "Programs"), Icon = "Archive", Color = "#16A34A" },
                new() { Name = "Videos", FolderPath = System.IO.Path.Combine(baseDownloadsPath, "Videos"), Icon = "Archive", Color = "#7C3AED" },
                new() { Name = "Music", FolderPath = System.IO.Path.Combine(baseDownloadsPath, "Music"), Icon = "Archive", Color = "#EA580C" },
                new() { Name = "Images", FolderPath = System.IO.Path.Combine(baseDownloadsPath, "Images"), Icon = "Archive", Color = "#0D9488" },
                new() { Name = "Compressed", FolderPath = System.IO.Path.Combine(baseDownloadsPath, "Compressed"), Icon = "Archive", Color = "#DB2777" },
            };
        }
    }
}
