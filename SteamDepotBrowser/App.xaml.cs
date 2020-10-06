using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ShowMeTheXAML;
using SteamKit2;

namespace SteamDepotBrowser
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            XamlDisplay.Init();

            AccountSettingsStore.LoadFromFile("settings");
            Globals.AppState.LoginState.Username = AccountSettingsStore.Instance.LastUsername;
            Globals.UiDispatcher = Dispatcher;

            if (!string.IsNullOrEmpty(Globals.AppState.LoginState.Username))
                Globals.AppState.LoginState.RememberLogin = true;

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Globals.SteamSession.Shutdown().Wait();
            base.OnExit(e);
        }
    }
}