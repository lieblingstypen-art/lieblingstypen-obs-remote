using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LieblingstypenRemote;

public record YouTubeVideo(string Id, string Title, string ThumbnailUrl, string Published);

public class YouTubeService
{
    private readonly HttpClient _client;

    public YouTubeService()
    {
        _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        // Realistic UA so YouTube serves the full page
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        // EU consent cookies so we skip the consent.youtube.com redirect
        _client.DefaultRequestHeaders.Add("Cookie", "CONSENT=YES+cb; SOCS=CAI");
    }

    public async Task<List<YouTubeVideo>> FetchLatestAsync(string handleOrId, int max = 25)
    {
        // Try RSS first (cleanest). If that fails (some channels return 404), fall back to scraping.
        string channelId = handleOrId.StartsWith("UC") ? handleOrId : "";
        var handle = handleOrId.StartsWith("UC") ? "" : handleOrId.TrimStart('@');
        var channelPageUrl = !string.IsNullOrEmpty(handle)
            ? $"https://www.youtube.com/@{handle}"
            : $"https://www.youtube.com/channel/{channelId}";

        try
        {
            if (string.IsNullOrEmpty(channelId))
                channelId = await GetChannelIdAsync(channelPageUrl);
            if (!string.IsNullOrEmpty(channelId))
            {
                var rssUrl = $"https://www.youtube.com/feeds/videos.xml?channel_id={channelId}";
                var xml = await _client.GetStringAsync(rssUrl);
                var doc = XDocument.Parse(xml);
                XNamespace ns = "http://www.w3.org/2005/Atom";
                XNamespace ytNs = "http://www.youtube.com/xml/schemas/2015";
                var fromRss = doc.Root!.Elements(ns + "entry")
                    .Select(e =>
                    {
                        var id = e.Element(ytNs + "videoId")?.Value ?? "";
                        return new YouTubeVideo(
                            id,
                            e.Element(ns + "title")?.Value ?? "",
                            $"https://i.ytimg.com/vi/{id}/mqdefault.jpg",
                            e.Element(ns + "published")?.Value ?? ""
                        );
                    })
                    .Take(max)
                    .ToList();
                if (fromRss.Count > 0) return fromRss;
            }
        }
        catch { /* fall through to scraping */ }

        // Fallback: scrape the /videos page for video IDs + titles
        var videosPageUrl = !string.IsNullOrEmpty(handle)
            ? $"https://www.youtube.com/@{handle}/videos"
            : $"https://www.youtube.com/channel/{channelId}/videos";
        var html = await _client.GetStringAsync(videosPageUrl);

        // Each video entry has: "vi/{ID}/..." ... within a few KB ... "title":{"content":"{TITLE}"
        var entryRx = new Regex(
            @"vi/(?<id>[a-zA-Z0-9_-]{11})/[^""]*""[\s\S]{1,5000}?""title"":\{""content"":""(?<title>(?:[^""\\]|\\.)*)""",
            RegexOptions.None);

        var seen = new HashSet<string>();
        var result = new List<YouTubeVideo>();
        foreach (Match m in entryRx.Matches(html))
        {
            var id = m.Groups["id"].Value;
            if (!seen.Add(id)) continue;
            var title = DecodeJsonString(m.Groups["title"].Value);
            result.Add(new YouTubeVideo(
                id,
                title,
                $"https://i.ytimg.com/vi/{id}/mqdefault.jpg",
                ""
            ));
            if (result.Count >= max) break;
        }
        return result;
    }

    private static string DecodeJsonString(string s)
    {
        // Handle common JSON escape sequences (the regex captured the raw escapes)
        return s
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t")
            .Replace("\\/", "/")
            .Replace("\\u003c", "<")
            .Replace("\\u003e", ">")
            .Replace("\\u0026", "&")
            .Replace("\\u0027", "'")
            .Replace("\\u0022", "\"");
    }

    private async Task<string> GetChannelIdAsync(string channelUrl)
    {
        var html = await _client.GetStringAsync(channelUrl);
        // Try most specific pattern first
        foreach (var pattern in new[]
        {
            @"""externalId"":""(UC[A-Za-z0-9_-]{22})""",
            @"""channelId"":""(UC[A-Za-z0-9_-]{22})""",
            @"""browseId"":""(UC[A-Za-z0-9_-]{22})""",
            @"channel/(UC[A-Za-z0-9_-]{22})"
        })
        {
            var m = Regex.Match(html, pattern);
            if (m.Success) return m.Groups[1].Value;
        }
        return "";
    }
}
