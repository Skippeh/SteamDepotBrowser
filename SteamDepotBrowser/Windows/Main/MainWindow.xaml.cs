﻿using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Ookii.Dialogs.Wpf;
using SteamDepotBrowser.Data;

namespace SteamDepotBrowser.Windows.Main
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private Task downloadTask;
        private CancellationTokenSource downloadTaskCancellationSource;
        
        public MainWindow()
        {
            InitializeComponent();
            DataContext = Globals.AppState;
            Resources.Add("Apps", Globals.AppState.SteamState.Apps);

            Loaded += (sender, args) =>
            {
                Task.Run(async () =>
                {
                    if (!await Globals.SteamSession.LogOnWithUI(this))
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
            if (!Globals.AppState.Downloading)
            {
                var folderDialog = new VistaFolderBrowserDialog
                {
                    ShowNewFolderButton = true,
                    Description = "Select target folder",
                    UseDescriptionForTitle = true
                };

                if (folderDialog.ShowDialog(this) != true)
                    return;

                downloadTaskCancellationSource = new CancellationTokenSource();
                downloadTask = Task.Run(() => StartDownload(folderDialog.SelectedPath));
            }
            else
            {
                Globals.AppState.CancellingDownload = true;
                downloadTaskCancellationSource.Cancel();
            }
        }

        private async Task StartDownload(string targetFolder)
        {
            Console.WriteLine("Starting download...");
            
            Globals.AppState.Downloading = true;
            Globals.AppState.DownloadPercentageComplete = 0;

            ContentDownloader.Config.InstallDirectory = targetFolder;
            ContentDownloader.Config.CellID = (int) Globals.SteamSession.Client.CellID;

            try
            {
                await ContentDownloader.DownloadAppAsync(Globals.AppState.SelectedApp.Id, Globals.AppState.SelectedDepot.Id, Globals.AppState.SelectedManifest.Id,
                    downloadTaskCancellationSource.Token);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occured: {ex.Message}", "Download error");
            }

            Globals.AppState.Downloading = false;
            Globals.AppState.DownloadPercentageComplete = 0;
            Globals.AppState.CancellingDownload = false;
            Console.WriteLine("Completed");
        }
    }
}