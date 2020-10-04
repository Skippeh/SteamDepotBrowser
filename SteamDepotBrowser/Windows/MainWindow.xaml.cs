using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SteamDepotBrowser.Data;
using SteamDepotBrowser.Windows;
using SteamKit2;

namespace SteamDepotBrowser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = Globals.AppState;
            Resources.Add("Apps", Globals.AppState.SteamState.Apps);

            Loaded += (sender, args) =>
            {
                var loginWindow = new LoginWindow
                {
                    Owner = this
                };

                if (loginWindow.ShowDialog() != true)
                {
                    Close();
                    return;
                }

                while (!Globals.ReceivedLicenses)
                    Thread.Sleep(50);

                Console.WriteLine($"Num licenses: {Globals.Licenses.Count}");
                Task.Run(FetchLicenseData);
            };
        }

        private async Task FetchLicenseData()
        {
            var packageRequests = new List<SteamApps.PICSRequest>();
            var accessTokens = new Dictionary<uint, ulong>();

            foreach (var license in Globals.Licenses)
            {
                packageRequests.Add(new SteamApps.PICSRequest(license.PackageID, license.AccessToken, false));
                accessTokens.Add(license.PackageID, license.AccessToken);
            }
                
            var steamApps = Globals.SteamClient.GetHandler<SteamApps>();
            var packageInfos = await steamApps.PICSGetProductInfo(new SteamApps.PICSRequest[0], packageRequests);

            if (packageInfos.Failed || !packageInfos.Complete || packageInfos.Results == null)
                throw new NotImplementedException("Did not receive package data from Steam.");

            var productInfos = packageInfos.Results.SelectMany(appInfo => appInfo.Packages.Values);
            var appRequests = new Dictionary<uint, SteamApps.PICSRequest>();
            
            Directory.CreateDirectory("packages");
            
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

            Directory.CreateDirectory("apps");
            
            foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo appInfo in productInfos)
            {
                KeyValue common = appInfo.KeyValues["common"];
                string appName = common["name"].AsString();
                string type = common["type"].AsString();

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
                    Name = appName
                };

                KeyValue depots = appInfo.KeyValues["depots"];

                foreach (KeyValue depotInfo in depots.Children)
                {
                    string strDepotId = depotInfo.Name;

                    if (!int.TryParse(strDepotId, out int depotId))
                        continue;

                    if (depotInfo["sharedinstall"].AsString() == "1") // Skip shared depots (redistributables)
                        continue;

                    if (depotInfo["manifests"]["public"].Value == null) // Skip depots that don't have a public manifest (encrypted)
                        continue;

                    steamApp.Depots.Add(new AppDepot
                    {
                        Id = depotId,
                        Name = depotInfo["name"].AsString()
                    });
                }

                if (steamApp.Depots.Count == 0)
                    continue;

                if (steamApp.Depots.Count > 1)
                    steamApp.Depots.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.Ordinal));

                apps.Add(steamApp);
            }

            apps.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.Ordinal));

            Dispatcher.Invoke(() =>
            {
                Globals.AppState.SteamState.Apps = new ObservableCollection<SteamApp>(apps);
                Globals.AppState.SteamState.Loading = false;
                Globals.AppState.SteamState.LoadedSuccessfully = true;

                Globals.AppState.SelectedApp = apps.FirstOrDefault();
            });
        }

        private void OnDownloadClicked(object sender, RoutedEventArgs e)
        {
            Task.Run(StartDownload);
        }

        private async Task StartDownload()
        {
            Console.WriteLine("Starting download...");
            
            Globals.AppState.Downloading = true;
            Globals.AppState.DownloadPercentageComplete = 0;

            

            Globals.AppState.Downloading = false;
            Globals.AppState.DownloadPercentageComplete = 0;
            Console.WriteLine("Completed");
        }
    }
}