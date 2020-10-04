using System;

namespace SteamDepotBrowser.Data
{
    public class DepotManifestInfo
    {
        public DateTime Date { get; set; }
        public ulong ManifestId { get; set; }

        public string DisplayName => $"{ManifestId} - {Date}";
    }
}