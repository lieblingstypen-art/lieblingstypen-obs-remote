using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LieblingstypenRemote;

public record YouTubeVideo(string Id, string Title, string ThumbnailUrl, int DurationSeconds);

public class YouTubeService
{
    private readonly HttpClient _client;
    private const int SHORTS_CUTOFF_SECONDS = 60;

    public YouTubeService()
    {
        _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _client.DefaultRequestHeaders.Add("Cookie", "CONSENT=YES+cb; SOCS=CAI");
    }

    public async Task<List<YouTubeVideo>> FetchLatestAsync(string handleOrId, int max = 25)
    {
        var handle = handleOrId.StartsWith("UC") ? "" : handleOrId.TrimStart('@');
        var pageUrl = !string.IsNullOrEmpty(handle)
            ? $"https://www.youtube.com/@{handle}/videos"
            : $"https://www.youtube.com/channel/{handleOrId}/videos";

        var html = await _client.GetStringAsync(pageUrl);

        // Each video entry: vi/{id}/... thumbnailBadgeViewModel.text=duration ... title.content=title
        var rx = new Regex(
            @"vi/(?<id>[a-zA-Z0-9_-]{11})/[^""]+""[\s\S]{1,5000}?""thumbnailBadgeViewModel"":\{""text"":""(?<dur>[\d:]+)""[\s\S]{1,5000}?""title"":\{""content"":""(?<title>(?:[^""\\]|\\.)*?)""",
            RegexOptions.None);

        var seen = new HashSet<string>();
        var result = new List<YouTubeVideo>();
        foreach (Match m in rx.Matches(html))
        {
            var id = m.Groups["id"].Value;
            if (!seen.Add(id)) continue;

            var seconds = ParseDurationToSeconds(m.Groups["dur"].Value);
            if (seconds <= SHORTS_CUTOFF_SECONDS) continue; // skip Shorts

            var title = DecodeJsonString(m.Groups["title"].Value);
            result.Add(new YouTubeVideo(
                id,
                title,
                $"https://i.ytimg.com/vi/{id}/mqdefault.jpg",
                seconds
            ));
            if (result.Count >= max) break;
        }
        return result;
    }

    private static int ParseDurationToSeconds(string s)
    {
        var total = 0;
        foreach (var part in s.Split(':'))
        {
            if (!int.TryParse(part, out var n)) return 0;
            total = total * 60 + n;
        }
        return total;
    }

    private static string DecodeJsonString(string s)
    {
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
}
