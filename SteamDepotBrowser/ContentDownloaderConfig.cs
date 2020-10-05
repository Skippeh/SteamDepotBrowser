namespace SteamDepotBrowser
{
    public class ContentDownloaderConfig
    {
        public string InstallDirectory { get; set; }
        public int CellID { get; set; }
        public int MaxDownloads { get; set; } = 10;
        public bool VerifyAll { get; set; }
    }
}