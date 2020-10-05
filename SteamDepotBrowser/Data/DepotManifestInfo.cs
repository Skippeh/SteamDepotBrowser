using System;

namespace SteamDepotBrowser.Data
{
    public class DepotManifestInfo
    {
        public ulong Id { get; set; }
        public DateTime Date { get; set; }
        public string DisplayName => $"{Id} - {Date}";
    }
}