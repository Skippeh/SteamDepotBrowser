using System;
using System.IO;
using System.Security.Cryptography;
using SteamKit2;

namespace SteamDepotBrowser
{
    public static class SteamSentryManager
    {
        public static byte[] GetSentryHash(string username)
        {
            string filePath = Path.Combine("sentry", $"sentry_{username}");

            if (File.Exists(filePath))
                return GetSentryHash(File.ReadAllBytes(filePath));

            return null;
        }

        public static byte[] GetSentryHash(byte[] sentryBytes)
        {
            return CryptoHelper.SHAHash(sentryBytes);
        }

        public static void WriteSentryFile(SteamUser.UpdateMachineAuthCallback data, string username)
        {
            EnsureDirectoryExists();
            var filePath = Path.Combine("sentry", $"sentry_{username}");
            int fileSize;

            using (var file = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                file.Seek(data.Offset, SeekOrigin.Begin);
                file.Write(data.Data, 0, data.BytesToWrite);
                fileSize = (int) file.Length;
            }

            var sentryHash = GetSentryHash(File.ReadAllBytes(filePath));

            var user = Globals.SteamSession.Client.GetHandler<SteamUser>();
            user.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
            {
                JobID = data.JobID,
                FileName = data.FileName,
                BytesWritten = data.BytesToWrite,
                FileSize = fileSize,
                Offset = data.Offset,
                Result = EResult.OK,
                LastError = 0,
                OneTimePassword = data.OneTimePassword,
                SentryFileHash = sentryHash
            });
        }

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists("sentry"))
                Directory.CreateDirectory("sentry");
        }

        public static void WriteLoginKey(SteamUser.LoginKeyCallback data, string username)
        {
            AccountSettingsStore.Instance.LoginKeys[username] = data.LoginKey;
            AccountSettingsStore.Save();
        }

        public static string GetLoginKey(string username)
        {
            if (AccountSettingsStore.Instance.LoginKeys.TryGetValue(username, out var loginKey))
                return loginKey;

            return null;
        }
    }
}