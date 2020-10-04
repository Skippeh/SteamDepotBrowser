using System.Collections.Generic;

namespace SteamDepotBrowser.Data
{
    public class SteamApp
    {
        public string Name { get; set; }
        public List<AppDepot> Depots { get; set; } = new List<AppDepot>();
    }
}