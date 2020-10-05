using System.Collections.Generic;
using SteamKit2;

namespace SteamDepotBrowser
{
    public static class Globals
    {
        public static SteamClient SteamClient { get; } = new SteamClient();
        public static CallbackManager CallbackManager { get; }
        public static AppState AppState { get; } = new AppState();
        public static List<SteamApps.LicenseListCallback.License> Licenses { get; } = new List<SteamApps.LicenseListCallback.License>();
        public static bool ReceivedLicenses { get; set; }
        public static Steam3Session SteamSession { get; } = new Steam3Session();

        static Globals()
        {
            CallbackManager = new CallbackManager(SteamClient);
        }
    }
}