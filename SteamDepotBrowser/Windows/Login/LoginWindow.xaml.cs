using System;
using System.Collections.Generic;
using System.Windows;
using SteamKit2;

namespace SteamDepotBrowser.Windows.Login
{
    public partial class LoginWindow : Window
    {
        private SteamClient Client => Globals.SteamSession.Client;
        private LoginState State => Globals.AppState.LoginState;

        private bool requiresTwoFactorCode;
        private readonly List<IDisposable> subscribedEvents = new List<IDisposable>();
        private bool loggedInSuccessfully;

        public LoginWindow()
        {
            InitializeComponent();
            DataContext = Globals.AppState;

            subscribedEvents.Add(Globals.SteamSession.CallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnSteamConnected));
            subscribedEvents.Add(Globals.SteamSession.CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnSteamDisconnected));
            subscribedEvents.Add(Globals.SteamSession.CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnSteamLoggedOn));

            Closing += (sender, args) =>
            {
                DialogResult = loggedInSuccessfully;
            };

            Closed += (sender, args) =>
            {
                foreach (var disposable in subscribedEvents)
                    disposable.Dispose();
            };

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (State.Username != "" && AccountSettingsStore.Instance.LoginKeys.ContainsKey(State.Username))
            {
                State.LoggingIn = true;
                Client.Connect();
            }
        }

        private void OnLoginClicked(object sender, RoutedEventArgs e)
        {
            if (State.Username == "" || State.Password == "")
            {
                State.LoginErrorText = "Invalid username or password";
                return;
            }

            if (!State.RememberLogin)
            {
                AccountSettingsStore.Instance.LastUsername = null;
                AccountSettingsStore.Save();
            }
            
            State.LoggingIn = true;
            Client.Connect();
        }

        private void OnSteamConnected(SteamClient.ConnectedCallback data)
        {
            var user = Client.GetHandler<SteamUser>();
            var sentryHash = SteamSentryManager.GetSentryHash(State.Username);
            var loginKey = SteamSentryManager.GetLoginKey(State.Username);
            var logonDetails = new SteamUser.LogOnDetails()
            {
                Username = State.Username,
                Password = State.Password,
                AuthCode = State.RequiresAuthCode && !requiresTwoFactorCode ? State.AuthCode : null,
                TwoFactorCode = State.RequiresAuthCode && requiresTwoFactorCode ? State.AuthCode : null,
                ShouldRememberPassword = State.RememberLogin,
                SentryFileHash = sentryHash,
                LoginKey = State.RememberLogin ? loginKey : null,
                LoginID = 1891 // Can be any integer, as long as it's unique per application
            };

            user.LogOn(logonDetails);
        }

        private void OnSteamDisconnected(SteamClient.DisconnectedCallback data)
        {
            State.LoggingIn = false;
        }

        private void OnSteamLoggedOn(SteamUser.LoggedOnCallback data)
        {
            if (data.Result != EResult.OK)
            {
                State.LoggingIn = false;

                switch (data.Result)
                {
                    case EResult.AccountLogonDenied:
                        State.LoginErrorText = "E-mail auth code required";
                        State.RequiresAuthCode = true;
                        requiresTwoFactorCode = false;
                        break;
                    case EResult.AccountLoginDeniedNeedTwoFactor:
                        State.LoginErrorText = "Two factor auth code required";
                        State.RequiresAuthCode = true;
                        requiresTwoFactorCode = true;
                        break;
                    case EResult.InvalidPassword:
                        State.LoginErrorText = "Invalid username or password";
                        break;
                    default:
                        State.LoginErrorText = $"Failed to login: {data.Result}";
                        break;
                }

                return;
            }

            if (State.RememberLogin)
            {
                AccountSettingsStore.Instance.LastUsername = State.Username;
                AccountSettingsStore.Save();
            }

            loggedInSuccessfully = true;
            Dispatcher.Invoke(Close);
        }
    }
}