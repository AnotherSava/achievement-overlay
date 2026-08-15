using System.Net.Http;
using System.Text.Json;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public class SteamDbScraperTests
{
    private const string Url = "https://steamdb.info/app/480/stats/";

    // Mirrors the markdown Firecrawl returns for a SteamDB stats page: per achievement an icon
    // line, display name, description (hidden ones prefixed "_Hidden achievement:_"), percentage,
    // then the internal API name, then the date.
    private const string SampleMarkdown = """
        ![](https://img/a.jpg)
        ![](https://img/b.jpg)

        Tower Tussle

        _Hidden achievement:_ Complete the tower

        12.5%

        ACH_TOWER

        20 Sep 2024

        ![](https://img/c.jpg)

        People Person

        Have maximum trust

        43.3%

        ACH_TRUST

        20 Sep 2024
        """;

    [Fact]
    public void ParseDescriptions_HiddenAchievement_MapsApiNameToStrippedDescription()
    {
        var map = SteamDbScraper.ParseDescriptions(SampleMarkdown);
        Assert.Equal("Complete the tower", map["ACH_TOWER"]);
    }

    [Fact]
    public void ParseDescriptions_VisibleAchievement_MapsPlainDescription()
    {
        var map = SteamDbScraper.ParseDescriptions(SampleMarkdown);
        Assert.Equal("Have maximum trust", map["ACH_TRUST"]);
    }

    [Fact]
    public void ParseDescriptions_NoAchievements_ReturnsEmpty()
    {
        Assert.Empty(SteamDbScraper.ParseDescriptions("Just a\nchallenge page\nwith no data"));
    }

    [Fact]
    public void ParseDescriptions_PercentWithoutApiName_Skipped()
    {
        // A stray percentage not followed by an identifier-shaped token must not produce an entry.
        Assert.Empty(SteamDbScraper.ParseDescriptions("Some text\n42%\nhas spaces here"));
    }

    [Fact]
    public void ExtractMarkdown_PullsDataMarkdown()
    {
        var json = """{"success": true, "data": {"markdown": "# hello", "metadata": {}}}""";
        Assert.Equal("# hello", SteamDbScraper.ExtractMarkdown(json));
    }

    [Fact]
    public void ExtractMarkdown_SuccessFalse_ReturnsNull()
    {
        Assert.Null(SteamDbScraper.ExtractMarkdown("""{"success": false, "error": "blocked"}"""));
    }

    [Fact]
    public void ExtractMarkdown_Malformed_ReturnsNull()
    {
        Assert.Null(SteamDbScraper.ExtractMarkdown("not json"));
    }

    [Fact]
    public void ExtractApiError_JoinsErrorAndDetails()
    {
        var json = """{"success": false, "error": "Unauthorized", "details": "Invalid token"}""";
        Assert.Equal("Unauthorized — Invalid token", SteamDbScraper.ExtractApiError(json));
    }

    [Fact]
    public void ExtractApiError_NoErrorField_ReturnsNull()
    {
        Assert.Null(SteamDbScraper.ExtractApiError("""{"success": true, "data": {}}"""));
    }

    [Fact]
    public void ExtractApiError_NotJson_ReturnsNull()
    {
        Assert.Null(SteamDbScraper.ExtractApiError("<html>502 Bad Gateway</html>"));
    }

    [Fact]
    public void Interpret_Success_ReturnsDescriptions()
    {
        var payload = JsonSerializer.Serialize(new { success = true, data = new { markdown = SampleMarkdown } });
        var result = SteamDbScraper.Interpret(200, payload, Url);

        Assert.Null(result.FailureReason);
        Assert.Equal("Complete the tower", result.Descriptions!["ACH_TOWER"]);
    }

    [Theory]
    [InlineData(401, "rejected the API key")]
    [InlineData(402, "out of credits")]
    [InlineData(404, "endpoint was not found")]
    [InlineData(429, "rate limit")]
    [InlineData(503, "server error")]
    public void Interpret_HttpError_NamesTheCause(int status, string expectedFragment)
    {
        var result = SteamDbScraper.Interpret(status, """{"error": "nope"}""", Url);

        Assert.Null(result.Descriptions);
        Assert.Contains($"HTTP {status}", result.FailureReason);
        Assert.Contains(expectedFragment, result.FailureReason);
        Assert.Contains("nope", result.FailureReason);
    }

    [Fact]
    public void Interpret_HttpErrorWithNonJsonBody_QuotesTheBody()
    {
        var result = SteamDbScraper.Interpret(502, "<html>\n  Bad Gateway\n</html>", Url);
        Assert.Contains("<html> Bad Gateway </html>", result.FailureReason);
    }

    [Fact]
    public void Interpret_ScrapeFailedResponse_ReportsFirecrawlsError()
    {
        var result = SteamDbScraper.Interpret(200, """{"success": false, "error": "Request timed out"}""", Url);

        Assert.Null(result.Descriptions);
        Assert.Contains("Request timed out", result.FailureReason);
        Assert.Contains(Url, result.FailureReason);
    }

    [Fact]
    public void Interpret_CloudflareChallenge_SaysSoRatherThanBlamingTheLayout()
    {
        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            data = new { markdown = "Just a moment...\n\nEnable JavaScript and cookies to continue" }
        });
        var result = SteamDbScraper.Interpret(200, payload, Url);

        Assert.Null(result.Descriptions);
        Assert.Contains("bot check", result.FailureReason);
    }

    [Fact]
    public void Interpret_AchievementsStillLoading_SaysTheTableDidNotRender()
    {
        // What SteamDB served before the render wait was added: the real page, with the
        // client-rendered sections captured as placeholders.
        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            data = new { markdown = "# ELDEN RING\n\n## Achievements\n\nLoading…\n\n## Builds\n\nLoading…" }
        });
        var result = SteamDbScraper.Interpret(200, payload, Url);

        Assert.Null(result.Descriptions);
        Assert.Contains("still loading", result.FailureReason);
    }

    [Fact]
    public void Interpret_UnparseablePage_ReportsLengthAndLayoutSuspicion()
    {
        var markdown = "Some page that isn't the stats table";
        var payload = JsonSerializer.Serialize(new { success = true, data = new { markdown } });
        var result = SteamDbScraper.Interpret(200, payload, Url);

        Assert.Null(result.Descriptions);
        Assert.Contains($"{markdown.Length} characters", result.FailureReason);
        Assert.Contains("page layout", result.FailureReason);
    }

    [Fact]
    public async Task FetchHiddenDescriptionsAsync_NoApiKey_SaysTheKeyIsMissing()
    {
        using var http = new HttpClient();
        var result = await SteamDbScraper.FetchHiddenDescriptionsAsync("480", null, http);

        Assert.Null(result.Descriptions);
        Assert.Contains("No Firecrawl API key", result.FailureReason);
    }
}
