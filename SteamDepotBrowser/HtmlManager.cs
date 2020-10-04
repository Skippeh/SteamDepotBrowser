using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;

namespace SteamDepotBrowser
{
    public class HtmlManager
    {
        public static async Task<IDocument> ParseDocument(string html)
        {
            IBrowsingContext context = BrowsingContext.New(Configuration.Default);
            IDocument document = await context.OpenAsync(req => req.Content(html));

            return document;
        }
    }
}