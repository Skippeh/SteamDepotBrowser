using System.Collections.Generic;

namespace SteamDepotBrowser.Data
{
    public class AppDepot
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public List<DepotManifestInfo> Manifests { get; set; } = new List<DepotManifestInfo>();
    }
}