using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// Fetches hidden-achievement descriptions from SteamDB's stats page. Steam's public
/// <c>GetSchemaForGame</c> redacts descriptions for <c>hidden=1</c> achievements; SteamDB
/// exposes the real text.
///
/// SteamDB sits behind Cloudflare, which blocks a plain HTTP client regardless of cookies
/// (the cookie is bound to the browser's TLS fingerprint). So we scrape it through the
/// Firecrawl API (https://firecrawl.dev) — a hosted scraper that solves Cloudflare and
/// returns clean markdown. The caller supplies a Firecrawl API key; if it's missing or the
/// scrape fails, the orchestrator leaves placeholder descriptions in place.
/// </summary>
public static partial class SteamDbScraper
{
    private const string FirecrawlEndpoint = "https://api.firecrawl.dev/v1/scrape";

    [GeneratedRegex(@"^\d{1,3}(\.\d+)?\s*%$")]
    private static partial Regex PercentLineRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.]*$")]
    private static partial Regex ApiNameLineRegex();

    [GeneratedRegex(@"^_*\s*hidden\s+achievement\s*[:.]?\s*_*\s*", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenMarkerRegex();

    /// <summary>
    /// Scrapes the SteamDB stats page for <paramref name="appId"/> via Firecrawl and returns a map
    /// of achievement API name → description. Returns null if no API key, the scrape fails, or
    /// nothing was parsed.
    /// </summary>
    public static async Task<Dictionary<string, string>?> FetchHiddenDescriptionsAsync(
        string appId, string? firecrawlApiKey, HttpClient http, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(firecrawlApiKey))
            return null;

        var body = JsonSerializer.Serialize(new
        {
            url = $"https://steamdb.info/app/{appId}/stats/",
            formats = new[] { "markdown" },
            onlyMainContent = true
        });

        var request = new HttpRequestMessage(HttpMethod.Post, FirecrawlEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {firecrawlApiKey.Trim()}");

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var markdown = ExtractMarkdown(json);
            if (markdown == null)
                return null;

            var parsed = ParseDescriptions(markdown);
            return parsed.Count > 0 ? parsed : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Pulls <c>data.markdown</c> out of a Firecrawl scrape response, or null.</summary>
    public static string? ExtractMarkdown(string firecrawlJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(firecrawlJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                return null;
            if (root.TryGetProperty("data", out var data) && data.TryGetProperty("markdown", out var md)
                && md.ValueKind == JsonValueKind.String)
                return md.GetString();
        }
        catch (JsonException)
        {
            // fall through
        }
        return null;
    }

    /// <summary>
    /// Parses the SteamDB stats markdown into a map of achievement API name → description.
    /// Anchors on the per-achievement percentage line: each achievement renders as display name,
    /// description, percentage, then API name (the line right after the percentage).
    /// </summary>
    public static Dictionary<string, string> ParseDescriptions(string markdown)
    {
        var result = new Dictionary<string, string>();
        var lines = Linearize(markdown);

        for (var i = 0; i < lines.Count; i++)
        {
            if (!PercentLineRegex().IsMatch(lines[i]))
                continue;

            var apiName = i + 1 < lines.Count ? lines[i + 1] : null;   // API name follows the percentage
            var description = i - 1 >= 0 ? lines[i - 1] : null;          // description precedes it
            if (apiName == null || description == null || !ApiNameLineRegex().IsMatch(apiName))
                continue;

            description = HiddenMarkerRegex().Replace(description, "").Trim();
            if (description.Length > 0)
                result[apiName] = description;
        }

        return result;
    }

    /// <summary>Splits markdown into trimmed, non-empty content lines, dropping image lines.</summary>
    private static List<string> Linearize(string markdown)
    {
        var lines = new List<string>();
        foreach (var raw in markdown.Split('\n'))
        {
            var trimmed = CollapseWhitespace(raw);
            if (trimmed.Length == 0 || trimmed.StartsWith("![", StringComparison.Ordinal))
                continue;
            lines.Add(trimmed);
        }
        return lines;
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastWasSpace = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0)
                    sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }
}
