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

            if (File.Exists("lastusername"))
            {
                Globals.AppState.LoginState.Username = File.ReadAllText("lastusername");
                Globals.AppState.LoginState.RememberLogin = true;
            }
            
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Globals.SteamSession.Shutdown().Wait();
            base.OnExit(e);
        }
    }
}