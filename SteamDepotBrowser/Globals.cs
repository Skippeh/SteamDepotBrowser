using System.Collections.Generic;
using SteamKit2;

namespace SteamDepotBrowser
{
    public static class Globals
    {
        public static SteamSession SteamSession { get; } = new SteamSession();
        public static AppState AppState { get; } = new AppState();
    }
}