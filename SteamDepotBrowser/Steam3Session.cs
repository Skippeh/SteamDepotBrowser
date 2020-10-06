using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SteamDepotBrowser.Data;
using SteamDepotBrowser.Windows;
using SteamKit2;

namespace SteamDepotBrowser
{
    public class SteamSession
    {
        public SteamClient Client { get; }
        public bool ReceivedLicenses { get; private set; }
        public CallbackManager CallbackManager => callbacks;

        private readonly SteamUser steamUser;
        private readonly SteamApps steamApps;
        private readonly CallbackManager callbacks;

        private readonly ConcurrentDictionary<uint, Task<SteamApps.PICSProductInfoCallback.PICSProductInfo>> cachedApps = new ConcurrentDictionary<uint, Task<SteamApps.PICSProductInfoCallback.PICSProductInfo>>();
        private readonly ConcurrentDictionary<uint, Task<byte[]>> cachedDepotKeys = new ConcurrentDictionary<uint, Task<byte[]>>();
        private readonly ConcurrentDictionary<string, Task<SteamApps.CDNAuthTokenCallback>> cachedCdnAuthTokens = new ConcurrentDictionary<string, Task<SteamApps.CDNAuthTokenCallback>>();

        private readonly List<SteamApps.LicenseListCallback.License> licenses = new List<SteamApps.LicenseListCallback.License>();

        private bool isRunning = true;

        public SteamSession()
        {
            Client = new SteamClient("SteamDepotBrowser");
            steamUser = Client.GetHandler<SteamUser>();
            steamApps = Client.GetHandler<SteamApps>();
            callbacks = new CallbackManager(Client);

            callbacks.Subscribe<SteamUser.LoginKeyCallback>(OnSteamLoginKey);
            callbacks.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnSteamUpdateMachineAuth);
            callbacks.Subscribe<SteamApps.LicenseListCallback>(OnSteamLicenseList);

            Task.Run(UpdateSteamThread);
        }

        public async Task Shutdown()
        {
            isRunning = false;

            while (Client.IsConnected)
            {
                callbacks.RunCallbacks();
                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        private Task UpdateSteamThread()
        {
            return Task.Run(async () =>
            {
                while (isRunning)
                {
                    try
                    {
                        callbacks.RunCallbacks();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                    }

                    await Task.Delay(50).ConfigureAwait(false);
                }

                if (steamUser.SteamID != null)
                    steamUser.LogOff();

                Client.Disconnect();
            });
        }

        public async Task<bool> LogOnWithUI(Window parentWindow)
        {
            bool loggedIn = await parentWindow.Dispatcher.InvokeAsync(() =>
            {
                var loginWindow = new LoginWindow();
                loginWindow.Owner = parentWindow;

                if (loginWindow.ShowDialog() == true)
                    return true;

                return false;
            });

            if (!loggedIn)
                return false;

            while (!ReceivedLicenses)
                await Task.Delay(50).ConfigureAwait(false);

            return true;
        }

        public Task<SteamApps.PICSProductInfoCallback.PICSProductInfo> RequestAppInfoAsync(uint appId, bool allowCached = true)
        {
            if (allowCached && cachedApps.TryGetValue(appId, out var runningTask))
                return runningTask;

            return cachedApps[appId] = Task.Run(async () =>
            {
                var productInfo = await steamApps.PICSGetProductInfo(appId, null, false);

                if (productInfo.Failed)
                    return null;
                
                return productInfo.Results.First().Apps.First().Value;
            });
        }

        public Task<byte[]> RequestDepotKey(uint depotId, uint appId, bool allowCached = true)
        {
            if (allowCached && cachedDepotKeys.TryGetValue(depotId, out var runningTask))
                return runningTask;

            return cachedDepotKeys[depotId] = Task.Run<byte[]>(async () =>
            {
                var depotDecryptionKey = await steamApps.GetDepotDecryptionKey(depotId, appId);

                if (depotDecryptionKey.Result != EResult.OK)
                    throw new Exception($"GetDepotDecryptionKey result = {depotDecryptionKey.Result}");
                
                return depotDecryptionKey.DepotKey;
            });
        }

        public Task<SteamApps.CDNAuthTokenCallback> RequestCDNAuthToken(uint appId, uint depotId, string host, string cdnKey, bool allowCached = true)
        {
            if (allowCached && cachedCdnAuthTokens.TryGetValue(cdnKey, out var runningTask))
                return runningTask;

            return cachedCdnAuthTokens[cdnKey] = Task.Run(async () =>
            {
                var cdnTokenCallback = await steamApps.GetCDNAuthToken(appId, depotId, host);
                return cdnTokenCallback;
            });
        }

        public string ResolveCDNTopLevelHost(string host)
        {
            // SteamPipe CDN shares tokens with all hosts
            if (host.EndsWith( ".steampipe.steamcontent.com" ) )
            {
                return "steampipe.steamcontent.com";
            }
            else if (host.EndsWith(".steamcontent.com"))
            {
                return "steamcontent.com";
            }

            return host;
        }

        #region Steam callbacks

        private void OnSteamLoginKey(SteamUser.LoginKeyCallback data)
        {
            SteamSentryManager.WriteLoginKey(data, Globals.AppState.LoginState.Username);
        }

        private void OnSteamUpdateMachineAuth(SteamUser.UpdateMachineAuthCallback data)
        {
            SteamSentryManager.WriteSentryFile(data, Globals.AppState.LoginState.Username);
        }

        private void OnSteamLicenseList(SteamApps.LicenseListCallback data)
        {
            if (data.Result != EResult.OK)
            {
                throw new NotImplementedException($"OnLicenseList EResult != OK ({data.Result})");
            }

            licenses.AddRange(data.LicenseList);
            ReceivedLicenses = true;
            Console.WriteLine($"Received {licenses.Count} license(s)");
        }

        #endregion

        public async Task<List<SteamApp>> RequestAllApps()
        {
            var packageRequests = new List<SteamApps.PICSRequest>();
            var accessTokens = new Dictionary<uint, ulong>();

            foreach (var license in licenses)
            {
                packageRequests.Add(new SteamApps.PICSRequest(license.PackageID, license.AccessToken, false));
                accessTokens.Add(license.PackageID, license.AccessToken);
            }

            var packageInfos = await steamApps.PICSGetProductInfo(new SteamApps.PICSRequest[0], packageRequests);

            if (packageInfos.Failed || !packageInfos.Complete || packageInfos.Results == null)
                throw new NotImplementedException("Did not receive package data from Steam.");

            var productInfos = packageInfos.Results.SelectMany(appInfo => appInfo.Packages.Values);
            var appRequests = new Dictionary<uint, SteamApps.PICSRequest>();
            
            foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo in productInfos)
            {
                foreach (KeyValue appIdKv in productInfo.KeyValues["appids"].Children)
                {
                    uint appId = uint.Parse(appIdKv.Value);

                    if (!appRequests.ContainsKey(appId))
                    {
                        appRequests.Add(appId, new SteamApps.PICSRequest(appId, accessTokens[productInfo.ID], false));
                    }
                }
            }

            var appInfos = await steamApps.PICSGetProductInfo(appRequests.Values, new SteamApps.PICSRequest[0]);

            if (appInfos.Failed || !appInfos.Complete || appInfos.Results == null)
                throw new NotImplementedException("Did not receive app data from Steam.");

            productInfos = appInfos.Results.SelectMany(appInfo => appInfo.Apps.Values);
            var apps = new List<SteamApp>();
            
            foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo appInfo in productInfos)
            {
                KeyValue common = appInfo.KeyValues["common"];
                string appName = common["name"].AsString();
                string type = common["type"].AsString();

                cachedApps.TryAdd(appInfo.ID, Task.Run(() => appInfo));

                string[] validTypes =
                {
                    "game",
                    "dlc",
                    "demo",
                    "tool",
                    "application",
                    "music"
                };

                if (type == null || !validTypes.Contains(type?.ToLowerInvariant()))
                    continue;

                var steamApp = new SteamApp
                {
                    Id = appInfo.ID,
                    Name = appName
                };

                KeyValue depots = appInfo.KeyValues["depots"];

                foreach (KeyValue depotInfo in depots.Children)
                {
                    string strDepotId = depotInfo.Name;

                    if (!int.TryParse(strDepotId, out int depotId))
                        continue;

                    if (depotInfo["sharedinstall"] != KeyValue.Invalid) // Skip shared depots (redistributables)
                        continue;

                    if (depotInfo["manifests"]["public"] == KeyValue.Invalid) // Skip depots that don't have a public manifest (encrypted)
                        continue;

                    steamApp.Depots.Add(new AppDepot
                    {
                        Id = (uint) depotId,
                        Name = depotInfo["name"].AsString()
                    });
                }

                if (steamApp.Depots.Count == 0)
                    continue;

                if (steamApp.Depots.Count > 1)
                    steamApp.Depots.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.Ordinal));

                apps.Add(steamApp);
            }

            return apps;
        }
    }
}