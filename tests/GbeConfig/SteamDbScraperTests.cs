using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public class SteamDbScraperTests
{
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
}
