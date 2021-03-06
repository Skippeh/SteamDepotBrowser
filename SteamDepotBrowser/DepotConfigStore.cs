using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using ProtoBuf;

namespace SteamDepotBrowser
{
    [ProtoContract]
    class DepotConfigStore
    {
        [ProtoMember(1)]
        public Dictionary<uint, ulong> InstalledManifestIDs { get; }

        string FileName;

        DepotConfigStore()
        {
            InstalledManifestIDs = new Dictionary<uint, ulong>();
        }

        public static bool Loaded => Instance != null;

        public static DepotConfigStore Instance;

        public static void LoadFromFile(string filename)
        {
            if (Loaded)
                throw new Exception("Config already loaded");

            if (File.Exists(filename))
            {
                using (FileStream fs = File.Open(filename, FileMode.Open))
                using (DeflateStream ds = new DeflateStream(fs, CompressionMode.Decompress))
                    Instance = Serializer.Deserialize<DepotConfigStore>(ds);
            }
            else
            {
                Instance = new DepotConfigStore();
            }

            Instance.FileName = filename;
        }

        public static void Save()
        {
            if (!Loaded)
                throw new Exception("Saved config before loading");

            using (FileStream fs = File.Open(Instance.FileName, FileMode.Create))
            using (DeflateStream ds = new DeflateStream(fs, CompressionMode.Compress))
                Serializer.Serialize<DepotConfigStore>(ds, Instance);
        }
    }
}