using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;

namespace SteamDepotBrowser
{
    public class ContentDownloaderException : System.Exception
    {
        public ContentDownloaderException(String value) : base(value)
        {
        }
    }

    public static class ContentDownloader
    {
        public const uint INVALID_APP_ID = uint.MaxValue;
        public const uint INVALID_DEPOT_ID = uint.MaxValue;
        public const ulong INVALID_MANIFEST_ID = ulong.MaxValue;
        public const string DEFAULT_BRANCH = "Public";

        private static Steam3Session SteamSession => Globals.SteamSession;
        private static CDNClientPool cdnPool;

        private const string DEFAULT_DOWNLOAD_DIR = "depots";
        private const string CONFIG_DIR = ".DepotDownloader";
        private static readonly string STAGING_DIR = Path.Combine(CONFIG_DIR, "staging");

        private sealed class DepotDownloadInfo
        {
            public uint Id { get; }

            public string InstallDir { get; }

            public string ContentName { get; }

            public ulong ManifestId { get; }
            public byte[] DepotKey;

            public DepotDownloadInfo(uint depotId, ulong manifestId, string installDir, string contentName)
            {
                Id = depotId;
                ManifestId = manifestId;
                InstallDir = installDir;
                ContentName = contentName;
            }
        }

        static bool CreateDirectories(uint depotId, uint depotVersion, out string installDir)
        {
            installDir = null;
            try
            {
                if (string.IsNullOrWhiteSpace(ContentDownloader.Config.InstallDirectory))
                {
                    Directory.CreateDirectory(DEFAULT_DOWNLOAD_DIR);

                    string depotPath = Path.Combine(DEFAULT_DOWNLOAD_DIR, depotId.ToString());
                    Directory.CreateDirectory(depotPath);

                    installDir = Path.Combine(depotPath, depotVersion.ToString());
                    Directory.CreateDirectory(installDir);

                    Directory.CreateDirectory(Path.Combine(installDir, CONFIG_DIR));
                    Directory.CreateDirectory(Path.Combine(installDir, STAGING_DIR));
                }
                else
                {
                    Directory.CreateDirectory(ContentDownloader.Config.InstallDirectory);

                    installDir = ContentDownloader.Config.InstallDirectory;

                    Directory.CreateDirectory(Path.Combine(installDir, CONFIG_DIR));
                    Directory.CreateDirectory(Path.Combine(installDir, STAGING_DIR));
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        public static ContentDownloaderConfig Config { get; set; }

        internal static KeyValue GetSteam3AppSection(uint appId, EAppInfoSection section)
        {
            if (SteamSession == null || SteamSession.AppInfo == null)
            {
                return null;
            }

            SteamApps.PICSProductInfoCallback.PICSProductInfo app;
            if (!SteamSession.AppInfo.TryGetValue(appId, out app) || app == null)
            {
                return null;
            }

            KeyValue appinfo = app.KeyValues;
            string section_key;

            switch (section)
            {
                case EAppInfoSection.Common:
                    section_key = "common";
                    break;
                case EAppInfoSection.Extended:
                    section_key = "extended";
                    break;
                case EAppInfoSection.Config:
                    section_key = "config";
                    break;
                case EAppInfoSection.Depots:
                    section_key = "depots";
                    break;
                default:
                    throw new NotImplementedException();
            }

            KeyValue sectionKv = appinfo.Children.FirstOrDefault(c => c.Name == section_key);
            return sectionKv;
        }

        static uint GetSteam3AppBuildNumber(uint appId, string branch)
        {
            if (appId == INVALID_APP_ID)
                return 0;

            KeyValue depots = GetSteam3AppSection(appId, EAppInfoSection.Depots);
            KeyValue branches = depots["branches"];
            KeyValue node = branches[branch];

            if (node == KeyValue.Invalid)
                return 0;

            KeyValue buildId = node["buildid"];

            if (buildId == KeyValue.Invalid)
                return 0;

            return uint.Parse(buildId.Value);
        }

        static ulong GetSteam3DepotManifest(uint depotId, uint appId, string branch)
        {
            KeyValue depots = GetSteam3AppSection(appId, EAppInfoSection.Depots);
            KeyValue depotChild = depots[depotId.ToString()];

            if (depotChild == KeyValue.Invalid)
                return INVALID_MANIFEST_ID;

            // Shared depots can either provide manifests, or leave you relying on their parent app.
            // It seems that with the latter, "sharedinstall" will exist (and equals 2 in the one existance I know of).
            // Rather than relay on the unknown sharedinstall key, just look for manifests. Test cases: 111710, 346680.
            if (depotChild["manifests"] == KeyValue.Invalid && depotChild["depotfromapp"] != KeyValue.Invalid)
            {
                uint otherAppId = depotChild["depotfromapp"].AsUnsignedInteger();
                if (otherAppId == appId)
                {
                    // This shouldn't ever happen, but ya never know with Valve. Don't infinite loop.
                    Console.WriteLine("App {0}, Depot {1} has depotfromapp of {2}!",
                        appId, depotId, otherAppId);
                    return INVALID_MANIFEST_ID;
                }

                SteamSession.RequestAppInfo(otherAppId);

                return GetSteam3DepotManifest(depotId, otherAppId, branch);
            }

            var manifests = depotChild["manifests"];
            var manifests_encrypted = depotChild["encryptedmanifests"];

            if (manifests.Children.Count == 0 && manifests_encrypted.Children.Count == 0)
                return INVALID_MANIFEST_ID;

            var node = manifests[branch];

            if (branch != "Public" && node == KeyValue.Invalid)
            {
                var node_encrypted = manifests_encrypted[branch];
                if (node_encrypted != KeyValue.Invalid)
                {
                    string password = Config.BranchPassword;
                    if (password == null)
                    {
                        throw new ContentDownloaderException("Specified branch required a password.");
                    }

                    var encrypted_v1 = node_encrypted["encrypted_gid"];
                    var encrypted_v2 = node_encrypted["encrypted_gid_2"];

                    if (encrypted_v1 != KeyValue.Invalid)
                    {
                        byte[] input = Util.DecodeHexString(encrypted_v1.Value);
                        byte[] manifest_bytes = CryptoHelper.VerifyAndDecryptPassword(input, password);

                        if (manifest_bytes == null)
                        {
                            Console.WriteLine("Password was invalid for branch {0}", branch);
                            return INVALID_MANIFEST_ID;
                        }

                        return BitConverter.ToUInt64(manifest_bytes, 0);
                    }
                    else if (encrypted_v2 != KeyValue.Invalid)
                    {
                        // Submit the password to Steam now to get encryption keys
                        SteamSession.CheckAppBetaPassword(appId, Config.BranchPassword);

                        if (!SteamSession.AppBetaPasswords.ContainsKey(branch))
                        {
                            Console.WriteLine("Password was invalid for branch {0}", branch);
                            return INVALID_MANIFEST_ID;
                        }

                        byte[] input = Util.DecodeHexString(encrypted_v2.Value);
                        byte[] manifest_bytes;
                        try
                        {
                            manifest_bytes = CryptoHelper.SymmetricDecryptECB(input, SteamSession.AppBetaPasswords[branch]);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Failed to decrypt branch {0}: {1}", branch, e.Message);
                            return INVALID_MANIFEST_ID;
                        }

                        return BitConverter.ToUInt64(manifest_bytes, 0);
                    }
                    else
                    {
                        Console.WriteLine("Unhandled depot encryption for depotId {0}", depotId);
                        return INVALID_MANIFEST_ID;
                    }
                }

                return INVALID_MANIFEST_ID;
            }

            if (node.Value == null)
                return INVALID_MANIFEST_ID;

            return UInt64.Parse(node.Value);
        }

        static string GetAppOrDepotName(uint depotId, uint appId)
        {
            if (depotId == INVALID_DEPOT_ID)
            {
                KeyValue info = GetSteam3AppSection(appId, EAppInfoSection.Common);

                if (info == null)
                    return String.Empty;

                return info["name"].AsString();
            }
            else
            {
                KeyValue depots = GetSteam3AppSection(appId, EAppInfoSection.Depots);

                if (depots == null)
                    return String.Empty;

                KeyValue depotChild = depots[depotId.ToString()];

                if (depotChild == null)
                    return String.Empty;

                return depotChild["name"].AsString();
            }
        }

        public static async Task DownloadAppAsync(uint appId, uint depotId, ulong manifestId)
        {
            // Load our configuration data containing the depots currently installed
            string configPath = ContentDownloader.Config.InstallDirectory;
            if (string.IsNullOrWhiteSpace(configPath))
            {
                configPath = DEFAULT_DOWNLOAD_DIR;
            }

            Directory.CreateDirectory(Path.Combine(configPath, CONFIG_DIR));
            DepotConfigStore.LoadFromFile(Path.Combine(configPath, CONFIG_DIR, "depot.config"));

            if (SteamSession != null)
                SteamSession.RequestAppInfo(appId);

            var depotIDs = new List<uint>() {depotId};
            KeyValue depots = GetSteam3AppSection(appId, EAppInfoSection.Depots);

            var infos = new List<DepotDownloadInfo>();

            foreach (var depot in depotIDs)
            {
                var info = GetDepotInfo(depot, appId, manifestId, "Public");
                
                if (info != null)
                {
                    infos.Add(info);
                }
            }

            try
            {
                await DownloadSteam3Async(appId, infos).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("App {0} was not completely downloaded.", appId);
                throw;
            }
        }

        static DepotDownloadInfo GetDepotInfo(uint depotId, uint appId, ulong manifestId, string branch)
        {
            if (SteamSession != null && appId != INVALID_APP_ID)
                SteamSession.RequestAppInfo((uint) appId);

            string contentName = GetAppOrDepotName(depotId, appId);

            // Skip requesting an app ticket
            SteamSession.AppTickets[depotId] = null;

            if (manifestId == INVALID_MANIFEST_ID)
            {
                manifestId = GetSteam3DepotManifest(depotId, appId, branch);
                if (manifestId == INVALID_MANIFEST_ID && branch != "public")
                {
                    Console.WriteLine("Warning: Depot {0} does not have branch named \"{1}\". Trying public branch.", depotId, branch);
                    branch = "public";
                    manifestId = GetSteam3DepotManifest(depotId, appId, branch);
                }

                if (manifestId == INVALID_MANIFEST_ID)
                {
                    Console.WriteLine("Depot {0} ({1}) missing public subsection or manifest section.", depotId, contentName);
                    return null;
                }
            }

            uint uVersion = GetSteam3AppBuildNumber(appId, branch);

            string installDir;
            if (!CreateDirectories(depotId, uVersion, out installDir))
            {
                Console.WriteLine("Error: Unable to create install directories!");
                return null;
            }

            SteamSession.RequestDepotKey(depotId, appId);
            if (!SteamSession.DepotKeys.ContainsKey(depotId))
            {
                Console.WriteLine("No valid depot key for {0}, unable to download.", depotId);
                return null;
            }

            byte[] depotKey = SteamSession.DepotKeys[depotId];

            var info = new DepotDownloadInfo(depotId, manifestId, installDir, contentName);
            info.DepotKey = depotKey;
            return info;
        }

        private class ChunkMatch
        {
            public ChunkMatch(ProtoManifest.ChunkData oldChunk, ProtoManifest.ChunkData newChunk)
            {
                OldChunk = oldChunk;
                NewChunk = newChunk;
            }

            public ProtoManifest.ChunkData OldChunk { get; private set; }
            public ProtoManifest.ChunkData NewChunk { get; private set; }
        }

        private static async Task DownloadSteam3Async(uint appId, List<DepotDownloadInfo> depots)
        {
            ulong TotalBytesCompressed = 0;
            ulong TotalBytesUncompressed = 0;

            foreach (var depot in depots)
            {
                ulong DepotBytesCompressed = 0;
                ulong DepotBytesUncompressed = 0;

                Console.WriteLine("Downloading depot {0} - {1}", depot.Id, depot.ContentName);

                CancellationTokenSource cts = new CancellationTokenSource();
                cdnPool.ExhaustedToken = cts;

                ProtoManifest oldProtoManifest = null;
                ProtoManifest newProtoManifest = null;
                string configDir = Path.Combine(depot.InstallDir, CONFIG_DIR);

                ulong lastManifestId = INVALID_MANIFEST_ID;
                DepotConfigStore.Instance.InstalledManifestIDs.TryGetValue(depot.Id, out lastManifestId);

                // In case we have an early exit, this will force equiv of verifyall next run.
                DepotConfigStore.Instance.InstalledManifestIDs[depot.Id] = INVALID_MANIFEST_ID;
                DepotConfigStore.Save();

                if (lastManifestId != INVALID_MANIFEST_ID)
                {
                    var oldManifestFileName = Path.Combine(configDir, string.Format("{0}.bin", lastManifestId));

                    if (File.Exists(oldManifestFileName))
                    {
                        byte[] expectedChecksum, currentChecksum;

                        try
                        {
                            expectedChecksum = File.ReadAllBytes(oldManifestFileName + ".sha");
                        }
                        catch (IOException)
                        {
                            expectedChecksum = null;
                        }

                        oldProtoManifest = ProtoManifest.LoadFromFile(oldManifestFileName, out currentChecksum);

                        if (expectedChecksum == null || !expectedChecksum.SequenceEqual(currentChecksum))
                        {
                            // We only have to show this warning if the old manifest ID was different
                            if (lastManifestId != depot.ManifestId)
                                Console.WriteLine("Manifest {0} on disk did not match the expected checksum.", lastManifestId);
                            oldProtoManifest = null;
                        }
                    }
                }

                if (lastManifestId == depot.ManifestId && oldProtoManifest != null)
                {
                    newProtoManifest = oldProtoManifest;
                    Console.WriteLine("Already have manifest {0} for depot {1}.", depot.ManifestId, depot.Id);
                }
                else
                {
                    var newManifestFileName = Path.Combine(configDir, string.Format("{0}_{1}.bin", depot.Id, depot.ManifestId));
                    if (newManifestFileName != null)
                    {
                        byte[] expectedChecksum, currentChecksum;

                        try
                        {
                            expectedChecksum = File.ReadAllBytes(newManifestFileName + ".sha");
                        }
                        catch (IOException)
                        {
                            expectedChecksum = null;
                        }

                        newProtoManifest = ProtoManifest.LoadFromFile(newManifestFileName, out currentChecksum);

                        if (newProtoManifest != null && (expectedChecksum == null || !expectedChecksum.SequenceEqual(currentChecksum)))
                        {
                            Console.WriteLine("Manifest {0} on disk did not match the expected checksum.", depot.ManifestId);
                            newProtoManifest = null;
                        }
                    }

                    if (newProtoManifest != null)
                    {
                        Console.WriteLine("Already have manifest {0} for depot {1}.", depot.ManifestId, depot.Id);
                    }
                    else
                    {
                        Console.Write("Downloading depot manifest...");

                        DepotManifest depotManifest = null;

                        while (depotManifest == null)
                        {
                            Tuple<CDNClient.Server, string> connection = null;
                            try
                            {
                                connection = await cdnPool.GetConnectionForDepot(appId, depot.Id, CancellationToken.None);

                                depotManifest = await cdnPool.CDNClient.DownloadManifestAsync(depot.Id, depot.ManifestId,
                                    connection.Item1, connection.Item2, depot.DepotKey).ConfigureAwait(false);

                                cdnPool.ReturnConnection(connection);
                            }
                            catch (SteamKitWebRequestException e)
                            {
                                cdnPool.ReturnBrokenConnection(connection);

                                if (e.StatusCode == HttpStatusCode.Unauthorized || e.StatusCode == HttpStatusCode.Forbidden)
                                {
                                    Console.WriteLine("Encountered 401 for depot manifest {0} {1}. Aborting.", depot.Id, depot.ManifestId);
                                    break;
                                }
                                else
                                {
                                    Console.WriteLine("Encountered error downloading depot manifest {0} {1}: {2}", depot.Id, depot.ManifestId, e.StatusCode);
                                }
                            }
                            catch (Exception e)
                            {
                                cdnPool.ReturnBrokenConnection(connection);
                                Console.WriteLine("Encountered error downloading manifest for depot {0} {1}: {2}", depot.Id, depot.ManifestId, e.Message);
                            }
                        }

                        if (depotManifest == null)
                        {
                            Console.WriteLine("\nUnable to download manifest {0} for depot {1}", depot.ManifestId, depot.Id);
                            return;
                        }

                        byte[] checksum;

                        newProtoManifest = new ProtoManifest(depotManifest, depot.ManifestId);
                        newProtoManifest.SaveToFile(newManifestFileName, out checksum);
                        File.WriteAllBytes(newManifestFileName + ".sha", checksum);

                        Console.WriteLine(" Done!");
                    }
                }

                newProtoManifest.Files.Sort((x, y) => string.Compare(x.FileName, y.FileName, StringComparison.Ordinal));

                Console.WriteLine("Manifest {0} ({1})", depot.ManifestId, newProtoManifest.CreationTime);

                ulong complete_download_size = 0;
                ulong size_downloaded = 0;
                string stagingDir = Path.Combine(depot.InstallDir, STAGING_DIR);

                var filesAfterExclusions = newProtoManifest.Files;

                // Pre-process
                filesAfterExclusions.ForEach(file =>
                {
                    var fileFinalPath = Path.Combine(depot.InstallDir, file.FileName);
                    var fileStagingPath = Path.Combine(stagingDir, file.FileName);

                    if (file.Flags.HasFlag(EDepotFileFlag.Directory))
                    {
                        Directory.CreateDirectory(fileFinalPath);
                        Directory.CreateDirectory(fileStagingPath);
                    }
                    else
                    {
                        // Some manifests don't explicitly include all necessary directories
                        Directory.CreateDirectory(Path.GetDirectoryName(fileFinalPath));
                        Directory.CreateDirectory(Path.GetDirectoryName(fileStagingPath));

                        complete_download_size += file.TotalSize;
                    }
                });

                var semaphore = new SemaphoreSlim(Config.MaxDownloads);
                var files = filesAfterExclusions.Where(f => !f.Flags.HasFlag(EDepotFileFlag.Directory)).ToArray();
                var tasks = new Task[files.Length];
                for (var i = 0; i < files.Length; i++)
                {
                    var file = files[i];
                    var task = Task.Run(async () =>
                    {
                        cts.Token.ThrowIfCancellationRequested();

                        try
                        {
                            await semaphore.WaitAsync().ConfigureAwait(false);
                            cts.Token.ThrowIfCancellationRequested();

                            string fileFinalPath = Path.Combine(depot.InstallDir, file.FileName);
                            string fileStagingPath = Path.Combine(stagingDir, file.FileName);

                            // This may still exist if the previous run exited before cleanup
                            if (File.Exists(fileStagingPath))
                            {
                                File.Delete(fileStagingPath);
                            }

                            FileStream fs = null;
                            List<ProtoManifest.ChunkData> neededChunks;
                            FileInfo fi = new FileInfo(fileFinalPath);
                            if (!fi.Exists)
                            {
                                // create new file. need all chunks
                                fs = File.Create(fileFinalPath);
                                fs.SetLength((long) file.TotalSize);
                                neededChunks = new List<ProtoManifest.ChunkData>(file.Chunks);
                            }
                            else
                            {
                                // open existing
                                ProtoManifest.FileData oldManifestFile = null;
                                if (oldProtoManifest != null)
                                {
                                    oldManifestFile = oldProtoManifest.Files.SingleOrDefault(f => f.FileName == file.FileName);
                                }

                                if (oldManifestFile != null)
                                {
                                    neededChunks = new List<ProtoManifest.ChunkData>();

                                    if (Config.VerifyAll || !oldManifestFile.FileHash.SequenceEqual(file.FileHash))
                                    {
                                        // we have a version of this file, but it doesn't fully match what we want

                                        var matchingChunks = new List<ChunkMatch>();

                                        foreach (var chunk in file.Chunks)
                                        {
                                            var oldChunk = oldManifestFile.Chunks.FirstOrDefault(c => c.ChunkID.SequenceEqual(chunk.ChunkID));
                                            if (oldChunk != null)
                                            {
                                                matchingChunks.Add(new ChunkMatch(oldChunk, chunk));
                                            }
                                            else
                                            {
                                                neededChunks.Add(chunk);
                                            }
                                        }

                                        File.Move(fileFinalPath, fileStagingPath);

                                        fs = File.Open(fileFinalPath, FileMode.Create);
                                        fs.SetLength((long) file.TotalSize);

                                        using (var fsOld = File.Open(fileStagingPath, FileMode.Open))
                                        {
                                            foreach (var match in matchingChunks)
                                            {
                                                fsOld.Seek((long) match.OldChunk.Offset, SeekOrigin.Begin);

                                                byte[] tmp = new byte[match.OldChunk.UncompressedLength];
                                                fsOld.Read(tmp, 0, tmp.Length);

                                                byte[] adler = Util.AdlerHash(tmp);
                                                if (!adler.SequenceEqual(match.OldChunk.Checksum))
                                                {
                                                    neededChunks.Add(match.NewChunk);
                                                }
                                                else
                                                {
                                                    fs.Seek((long) match.NewChunk.Offset, SeekOrigin.Begin);
                                                    fs.Write(tmp, 0, tmp.Length);
                                                }
                                            }
                                        }

                                        File.Delete(fileStagingPath);
                                    }
                                }
                                else
                                {
                                    // No old manifest or file not in old manifest. We must validate.

                                    fs = File.Open(fileFinalPath, FileMode.Open);
                                    if ((ulong) fi.Length != file.TotalSize)
                                    {
                                        fs.SetLength((long) file.TotalSize);
                                    }

                                    neededChunks = Util.ValidateSteam3FileChecksums(fs, file.Chunks.OrderBy(x => x.Offset).ToArray());
                                }

                                if (neededChunks.Count() == 0)
                                {
                                    size_downloaded += file.TotalSize;
                                    Console.WriteLine("{0,6:#00.00}% {1}", ((float) size_downloaded / (float) complete_download_size) * 100.0f, fileFinalPath);
                                    if (fs != null)
                                        fs.Dispose();
                                    return;
                                }
                                else
                                {
                                    size_downloaded += (file.TotalSize - (ulong) neededChunks.Select(x => (long) x.UncompressedLength).Sum());
                                }
                            }

                            foreach (var chunk in neededChunks)
                            {
                                if (cts.IsCancellationRequested) break;

                                string chunkID = Util.EncodeHexString(chunk.ChunkID);
                                CDNClient.DepotChunk chunkData = null;

                                while (!cts.IsCancellationRequested)
                                {
                                    Tuple<CDNClient.Server, string> connection;
                                    try
                                    {
                                        connection = await cdnPool.GetConnectionForDepot(appId, depot.Id, cts.Token);
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        break;
                                    }

                                    DepotManifest.ChunkData data = new DepotManifest.ChunkData();
                                    data.ChunkID = chunk.ChunkID;
                                    data.Checksum = chunk.Checksum;
                                    data.Offset = chunk.Offset;
                                    data.CompressedLength = chunk.CompressedLength;
                                    data.UncompressedLength = chunk.UncompressedLength;

                                    try
                                    {
                                        chunkData = await cdnPool.CDNClient.DownloadDepotChunkAsync(depot.Id, data,
                                            connection.Item1, connection.Item2, depot.DepotKey).ConfigureAwait(false);
                                        cdnPool.ReturnConnection(connection);
                                        break;
                                    }
                                    catch (SteamKitWebRequestException e)
                                    {
                                        cdnPool.ReturnBrokenConnection(connection);

                                        if (e.StatusCode == HttpStatusCode.Unauthorized || e.StatusCode == HttpStatusCode.Forbidden)
                                        {
                                            Console.WriteLine("Encountered 401 for chunk {0}. Aborting.", chunkID);
                                            cts.Cancel();
                                            break;
                                        }
                                        else
                                        {
                                            Console.WriteLine("Encountered error downloading chunk {0}: {1}", chunkID, e.StatusCode);
                                        }
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        Console.WriteLine("Connection timeout downloading chunk {0}", chunkID);
                                    }
                                    catch (Exception e)
                                    {
                                        cdnPool.ReturnBrokenConnection(connection);
                                        Console.WriteLine("Encountered unexpected error downloading chunk {0}: {1}", chunkID, e.Message);
                                    }
                                }

                                if (chunkData == null)
                                {
                                    Console.WriteLine("Failed to find any server with chunk {0} for depot {1}. Aborting.", chunkID, depot.Id);
                                    cts.Cancel();
                                }

                                // Throw the cancellation exception if requested so that this task is marked failed
                                cts.Token.ThrowIfCancellationRequested();

                                TotalBytesCompressed += chunk.CompressedLength;
                                DepotBytesCompressed += chunk.CompressedLength;
                                TotalBytesUncompressed += chunk.UncompressedLength;
                                DepotBytesUncompressed += chunk.UncompressedLength;

                                fs.Seek((long) chunk.Offset, SeekOrigin.Begin);
                                fs.Write(chunkData.Data, 0, chunkData.Data.Length);

                                size_downloaded += chunk.UncompressedLength;
                            }

                            fs.Dispose();

                            Console.WriteLine("{0,6:#00.00}% {1}", ((float) size_downloaded / (float) complete_download_size) * 100.0f, fileFinalPath);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    tasks[i] = task;
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);

                DepotConfigStore.Instance.InstalledManifestIDs[depot.Id] = depot.ManifestId;
                DepotConfigStore.Save();

                Console.WriteLine("Depot {0} - Downloaded {1} bytes ({2} bytes uncompressed)", depot.Id, DepotBytesCompressed, DepotBytesUncompressed);
            }

            Console.WriteLine("Total downloaded: {0} bytes ({1} bytes uncompressed) from {2} depots", TotalBytesCompressed, TotalBytesUncompressed, depots.Count);
        }
    }

    public class ContentDownloaderConfig
    {
        public string InstallDirectory { get; set; }
        public int CellID { get; set; } // todo: use SteamUser.CellID
        public string BranchPassword { get; set; }
        public int MaxDownloads { get; set; } = 10;
        public bool VerifyAll { get; set; }
    }
}