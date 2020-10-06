using System.Collections.Generic;
using System.Windows.Threading;
using SteamKit2;

namespace SteamDepotBrowser
{
    public static class Globals
    {
        public static SteamSession SteamSession { get; } = new SteamSession();
        public static AppState AppState { get; } = new AppState();
        public static  Dispatcher UiDispatcher { get; set; }
    }
}