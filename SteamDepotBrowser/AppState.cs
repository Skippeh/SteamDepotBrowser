using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SteamDepotBrowser.Data;
using SteamKit2;

namespace SteamDepotBrowser
{
    public class AppState : INotifyPropertyChanged
    {
        private SteamApp selectedApp;
        private AppDepot selectedDepot;
        private DepotManifestInfo selectedManifest;
        private bool loadingManifests;
        private readonly HashSet<AppDepot> loadedManifests = new HashSet<AppDepot>();
        private ulong? manifestSize;
        private bool loadingManifestSize;

        public LoginState LoginState { get; } = new LoginState();
        public SteamState SteamState { get; } = new SteamState();
        public DownloadState DownloadState { get; } = new DownloadState();

        public SteamApp SelectedApp
        {
            get => selectedApp;
            set
            {
                selectedApp = value;
                OnPropertyChanged();
                SelectedDepot = value?.Depots.FirstOrDefault();
            }
        }

        public AppDepot SelectedDepot
        {
            get => selectedDepot;
            set
            {
                selectedDepot = value;
                OnPropertyChanged();

                if (value != null)
                    Task.Run(LoadManifestsIfNeeded);
            }
        }

        public DepotManifestInfo SelectedManifest
        {
            get => selectedManifest;
            set
            {
                selectedManifest = value;
                OnPropertyChanged();

                if (value == null)
                    ManifestSize = null;
                else
                    Task.Run(LoadManifestSizeIfNeeded);
            }
        }

        public ulong? ManifestSize
        {
            get => manifestSize;
            set
            {
                manifestSize = value;
                OnPropertyChanged();
            }
        }

        public bool LoadingManifests
        {
            get => loadingManifests;
            set
            {
                loadingManifests = value;
                OnPropertyChanged();
            }
        }

        public bool LoadingManifestSize
        {
            get => loadingManifestSize;
            set
            {
                loadingManifestSize = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task LoadManifestsIfNeeded()
        {
            if (loadedManifests.Contains(SelectedDepot))
            {
                SelectedManifest = SelectedDepot.Manifests.FirstOrDefault();
                return;
            }

            LoadingManifests = true;
            List<DepotManifestInfo> manifests = await SteamDBManager.GetManifests(SelectedDepot.Id);

            if (manifests != null)
            {
                loadedManifests.Add(SelectedDepot);
                SelectedDepot.Manifests = manifests;
            }
            
            LoadingManifests = false;
            SelectedManifest = SelectedDepot.Manifests.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedDepot)); // Triggers a refresh on things that bind SelectedDepot and its members
        }

        private async Task LoadManifestSizeIfNeeded()
        {
            if (SelectedApp == null || SelectedDepot == null || selectedManifest == null)
                return;

            ManifestSize = null;
            LoadingManifestSize = true;

            try
            {
                var newManifestSize = await ContentDownloader.GetManifestSize(SelectedApp.Id, selectedDepot.Id, selectedManifest.Id);
                ManifestSize = newManifestSize;
            }
            catch
            {
                ManifestSize = null;
                Console.WriteLine("Failed to get manifest size");
            }
            finally
            {
                LoadingManifestSize = false;
            }
        }
    }

    public class SteamState : INotifyPropertyChanged
    {
        private ObservableCollection<SteamApp> apps;
        private bool loading = true;
        private bool loadedSuccessfully;

        public ObservableCollection<SteamApp> Apps
        {
            get => apps;
            set
            {
                apps = value;
                OnPropertyChanged();
            }
        }

        public bool Loading
        {
            get => loading;
            set
            {
                loading = value;
                OnPropertyChanged();
            }
        }

        public bool LoadedSuccessfully
        {
            get => loadedSuccessfully;
            set
            {
                loadedSuccessfully = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LoginState : INotifyPropertyChanged
    {
        private bool loggingIn;
        private bool requiresAuthCode;
        private string username = "";
        private string password = "";
        private string authCode = "";
        private bool rememberLogin;
        private string loginErrorText;

        public bool LoggingIn
        {
            get => loggingIn;
            set
            {
                loggingIn = value;
                OnPropertyChanged();
            }
        }

        public bool RequiresAuthCode
        {
            get => requiresAuthCode;
            set
            {
                requiresAuthCode = value;
                OnPropertyChanged();
            }
        }

        public string Username
        {
            get => username;
            set
            {
                username = value;
                OnPropertyChanged();
            }
        }

        public string Password
        {
            get => password;
            set
            {
                password = value;
                OnPropertyChanged();
            }
        }

        public string AuthCode
        {
            get => authCode;
            set
            {
                authCode = value;
                OnPropertyChanged();
            }
        }

        public bool RememberLogin
        {
            get => rememberLogin;
            set
            {
                rememberLogin = value;
                OnPropertyChanged();
            }
        }

        public string LoginErrorText
        {
            get => loginErrorText;
            set
            {
                loginErrorText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DownloadState : INotifyPropertyChanged
    {
        private double downloadPercentageComplete;
        private bool downloading;
        private string downloadCurrentFile;
        private bool cancellingDownload;
        private ulong totalBytes;
        private ulong downloadedBytes;
        private ulong bytesPerSecond;

        public bool Downloading
        {
            get => downloading;
            set
            {
                downloading = value;
                OnPropertyChanged();
            }
        }

        public double DownloadPercentageComplete
        {
            get => downloadPercentageComplete;
            set
            {
                downloadPercentageComplete = value;
                OnPropertyChanged();
            }
        }

        public string DownloadCurrentFile
        {
            get => downloadCurrentFile;
            set
            {
                downloadCurrentFile = value;
                OnPropertyChanged();
            }
        }

        public bool CancellingDownload
        {
            get => cancellingDownload;
            set
            {
                cancellingDownload = value;
                OnPropertyChanged();
            }
        }

        public ulong DownloadedBytes
        {
            get => downloadedBytes;
            set
            {
                downloadedBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RemainingBytes));
            }
        }

        public ulong TotalBytes
        {
            get => totalBytes;
            set
            {
                totalBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RemainingBytes));
            }
        }

        public ulong RemainingBytes => TotalBytes - DownloadedBytes;

        public ulong BytesPerSecond
        {
            get => bytesPerSecond;
            set
            {
                bytesPerSecond = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}