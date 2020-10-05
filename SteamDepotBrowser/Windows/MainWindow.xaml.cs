using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SteamDepotBrowser.Data;

namespace SteamDepotBrowser.Windows
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
                Task.Run(async () =>
                {
                    if (!await Globals.SteamSession.LogOnWithUI(Dispatcher))
                    {
                        Close();
                        return;
                    }
                    
                    var apps = await Globals.SteamSession.RequestAllApps();
                    apps.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.Ordinal));

                    Dispatcher.Invoke(() =>
                    {
                        Globals.AppState.SteamState.Apps = new ObservableCollection<SteamApp>(apps);
                        Globals.AppState.SteamState.Loading = false;
                        Globals.AppState.SteamState.LoadedSuccessfully = true;
                        Globals.AppState.SelectedApp = apps.FirstOrDefault();
                    });
                });
            };
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