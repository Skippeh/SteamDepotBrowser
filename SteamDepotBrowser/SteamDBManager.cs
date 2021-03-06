using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AngleSharp.Dom;
using Flurl;
using Flurl.Http;
using SteamDepotBrowser.Data;

namespace SteamDepotBrowser
{
    public static class SteamDBManager
    {
        private const string BaseUrl = "https://steamdb.info";

        public static Task<List<DepotManifestInfo>> GetManifests(uint depotId)
        {
            return Task.Run(async () =>
            {
                var result = new List<DepotManifestInfo>();
                string html = await BaseUrl
                    .AppendPathSegments("depot", depotId, "manifests")
                    .WithHeader("User-Agent", "SteamDepotBrowser")
                    .GetStringAsync().ConfigureAwait(false);

                if (html == null)
                {
                    Console.WriteLine("Error: Could not get manifest history from SteamDB");
                    return null;
                }

                var document = await HtmlManager.ParseDocument(html).ConfigureAwait(false);
                var manifestElements =
                    document.QuerySelector("#manifests")
                        ?.QuerySelector("table")
                        ?.QuerySelector("tbody")
                        ?.QuerySelectorAll("tr");

                if (manifestElements == null)
                    return null;

                foreach (IElement manifestElm in manifestElements)
                {
                    // Title attribute of relative date column is a parsable date string
                    string dateString = manifestElm.Children[1].GetAttribute("title");
                    string manifestIdString = manifestElm.Children[2].TextContent;

                    DateTime date = DateTime.Parse(dateString);
                    ulong manifestId = ulong.Parse(manifestIdString);

                    result.Add(new DepotManifestInfo
                    {
                        Date = date,
                        Id = manifestId
                    });
                }
                
                return result;
            });
        }
    }
}