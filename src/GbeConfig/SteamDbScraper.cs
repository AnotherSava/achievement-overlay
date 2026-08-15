using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// Outcome of a SteamDB scrape: the parsed API name → description map, or an explanation of
/// why nothing came back. The explanation is a full sentence meant for the config log — it
/// names the actual failure (rejected key, no credits, Cloudflare bot-check, layout change)
/// so the user can tell an unusable key apart from a page we simply couldn't parse.
/// </summary>
public sealed record SteamDbScrapeResult(Dictionary<string, string>? Descriptions, string? FailureReason)
{
    public static SteamDbScrapeResult Ok(Dictionary<string, string> descriptions) => new(descriptions, null);

    public static SteamDbScrapeResult Failed(string reason) => new(null, reason);
}

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

    /// <summary>How much of an unrecognized error body to quote in a failure reason.</summary>
    private const int MaxQuotedError = 300;

    /// <summary>
    /// Milliseconds Firecrawl waits after load before capturing. SteamDB renders the achievement
    /// table client-side — without a wait the section captures as a "Loading…" placeholder and the
    /// scrape yields nothing, which is what it did before this was added.
    /// </summary>
    private const int RenderWaitMs = 8000;

    [GeneratedRegex(@"^\d{1,3}(\.\d+)?\s*%$")]
    private static partial Regex PercentLineRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.]*$")]
    private static partial Regex ApiNameLineRegex();

    [GeneratedRegex(@"^_*\s*hidden\s+achievement\s*[:.]?\s*_*\s*", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenMarkerRegex();

    /// <summary>The SteamDB page we scrape — also quoted in failure reasons so it can be opened by hand.</summary>
    public static string StatsPageUrl(string appId) => $"https://steamdb.info/app/{appId}/stats/";

    /// <summary>
    /// Scrapes the SteamDB stats page for <paramref name="appId"/> via Firecrawl and returns a map
    /// of achievement API name → description, or a result carrying the reason it produced none.
    /// </summary>
    public static async Task<SteamDbScrapeResult> FetchHiddenDescriptionsAsync(
        string appId, string? firecrawlApiKey, HttpClient http, CancellationToken ct = default)
    {
        var url = StatsPageUrl(appId);
        if (string.IsNullOrWhiteSpace(firecrawlApiKey))
            return SteamDbScrapeResult.Failed("No Firecrawl API key is saved, and SteamDB sits behind Cloudflare, "
                + "which blocks a plain HTTP request.");

        var body = JsonSerializer.Serialize(new
        {
            url,
            formats = new[] { "markdown" },
            onlyMainContent = true,
            waitFor = RenderWaitMs
        });

        var request = new HttpRequestMessage(HttpMethod.Post, FirecrawlEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {firecrawlApiKey.Trim()}");

        try
        {
            using var response = await http.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            return Interpret((int)response.StatusCode, payload, url);
        }
        catch (HttpRequestException ex)
        {
            return SteamDbScrapeResult.Failed($"The request to {FirecrawlEndpoint} failed: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return SteamDbScrapeResult.Failed($"The request to {FirecrawlEndpoint} timed out.");
        }
    }

    /// <summary>
    /// Turns a Firecrawl response into descriptions or a reason there are none. Pure, so the
    /// wording of every failure path is unit-testable without a network round-trip.
    /// </summary>
    public static SteamDbScrapeResult Interpret(int statusCode, string payload, string url)
    {
        if (statusCode is < 200 or >= 300)
            return SteamDbScrapeResult.Failed(DescribeHttpFailure(statusCode, payload));

        var markdown = ExtractMarkdown(payload);
        if (markdown == null)
        {
            var apiError = ExtractApiError(payload);
            return SteamDbScrapeResult.Failed(apiError != null
                ? $"Firecrawl accepted the request but couldn't scrape {url}: {apiError}"
                : $"Firecrawl returned no page content for {url}. Response: {Quote(payload)}");
        }

        var parsed = ParseDescriptions(markdown);
        if (parsed.Count == 0)
            return SteamDbScrapeResult.Failed(DescribeEmptyPage(url, markdown));

        return SteamDbScrapeResult.Ok(parsed);
    }

    /// <summary>Explains a page that scraped fine but yielded no achievements.</summary>
    private static string DescribeEmptyPage(string url, string markdown)
    {
        if (LooksLikeBotCheck(markdown))
            return $"Firecrawl got Cloudflare's bot check instead of {url} — SteamDB blocked the scrape.";

        if (LooksUnrendered(markdown))
            return $"SteamDB's achievement table was still loading when {url} was captured — it renders "
                + $"client-side, and the {RenderWaitMs / 1000}s render wait wasn't enough this time.";

        return $"Firecrawl returned {markdown.Length} characters from {url}, but no achievements could be "
            + "parsed out of them — SteamDB may have changed its page layout.";
    }

    /// <summary>Explains a non-2xx Firecrawl response, naming the common causes by status code.</summary>
    private static string DescribeHttpFailure(int statusCode, string payload)
    {
        var cause = statusCode switch
        {
            400 => "Firecrawl rejected the request as malformed",
            401 or 403 => "Firecrawl rejected the API key",
            402 => "the Firecrawl account is out of credits",
            404 => "the Firecrawl endpoint was not found (the API version may have changed)",
            408 or 504 => "Firecrawl timed out loading the page",
            429 => "Firecrawl's rate limit was hit",
            >= 500 => "Firecrawl reported a server error",
            _ => "Firecrawl returned an error"
        };
        var detail = ExtractApiError(payload) ?? Quote(payload);
        return detail.Length > 0 ? $"HTTP {statusCode} — {cause}: {detail}" : $"HTTP {statusCode} — {cause}.";
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
    /// Pulls Firecrawl's own error text out of a response body — <c>error</c>, with <c>details</c>
    /// or <c>message</c> appended when present. Returns null if the body isn't JSON or carries no
    /// error, leaving the caller to quote the raw body instead.
    /// </summary>
    public static string? ExtractApiError(string firecrawlJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(firecrawlJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var parts = new List<string>();
            foreach (var name in new[] { "error", "message", "details" })
            {
                if (!doc.RootElement.TryGetProperty(name, out var value))
                    continue;
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
                if (!string.IsNullOrWhiteSpace(text) && !parts.Contains(text))
                    parts.Add(text);
            }
            return parts.Count > 0 ? Truncate(string.Join(" — ", parts)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True if the scraped page is a Cloudflare/anti-bot interstitial rather than SteamDB.</summary>
    private static bool LooksLikeBotCheck(string markdown)
    {
        string[] markers =
        [
            "just a moment", "checking your browser", "verifying you are human", "verify you are human",
            "attention required", "cf-browser-verification", "enable javascript and cookies", "ray id"
        ];
        return markers.Any(m => markdown.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True if the page is SteamDB's, but its client-rendered sections were captured as
    /// "Loading…" placeholders — the stats page fills the achievement table in after load.
    /// </summary>
    private static bool LooksUnrendered(string markdown) =>
        markdown.Contains("# Achievements", StringComparison.OrdinalIgnoreCase)
        && markdown.Contains("Loading", StringComparison.Ordinal);

    /// <summary>Collapses a raw response body to a single quotable line.</summary>
    private static string Quote(string body) => Truncate(CollapseWhitespace(body));

    private static string Truncate(string s) =>
        s.Length <= MaxQuotedError ? s : string.Concat(s.AsSpan(0, MaxQuotedError), "…");

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
