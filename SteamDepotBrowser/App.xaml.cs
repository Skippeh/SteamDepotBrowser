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
        private bool isRunning = true;
        
        protected override void OnStartup(StartupEventArgs e)
        {
            XamlDisplay.Init();
            Task.Run(UpdateSteamThread);
            
            Globals.CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnSteamLoginKey);
            Globals.CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnSteamUpdateMachineAuth);
            Globals.CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnSteamLicenseList);

            if (File.Exists("lastusername"))
            {
                Globals.AppState.LoginState.Username = File.ReadAllText("lastusername");
                Globals.AppState.LoginState.RememberLogin = true;
            }
            
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            isRunning = false;
            base.OnExit(e);
        }

        private async Task UpdateSteamThread()
        {
            while (isRunning)
            {
                try
                {
                    Globals.CallbackManager.RunCallbacks();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }

                await Task.Delay(50);
            }

            Globals.SteamClient.Disconnect();
        }

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

            Globals.Licenses.AddRange(data.LicenseList);
            Globals.ReceivedLicenses = true;
        }
    }
}