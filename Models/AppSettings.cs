namespace MAC_1.Models
{
    public class AppSettings
    {
        public int MaxSimultaneousDownloads { get; set; } = 3;
        public int DefaultConnections { get; set; } = 16;
        public bool AutoStartDownloads { get; set; } = true;
        public bool AutoCloseCompleted { get; set; } = true;
        public bool ResumeSupport { get; set; } = true;
        public string DefaultSavePath { get; set; } = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");
        public string PipeName { get; set; } = "MAC-1-Extension";
    }
}
