using System.IO;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public class AppIdResolverTests : IDisposable
{
    private readonly string _tempDir;

    public AppIdResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AppIdResolverTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Theory]
    [InlineData("AppId=480", "480")]
    [InlineData("steam_appid = 1601580", "1601580")]
    [InlineData("SteamAppId: 12345", "12345")]
    [InlineData("steamappid=99", "99")]
    [InlineData("steamappid=9", null)] // single digit not matched
    [InlineData("no appid here", null)]
    public void ParseAppIdFromText_MatchesKnownForms(string text, string? expected)
    {
        Assert.Equal(expected, AppIdResolver.ParseAppIdFromText(text));
    }

    [Fact]
    public void ParseAppIdFromStoreHtml_ExtractsFirstAppId()
    {
        var html = """<a class="search_result_row" data-ds-appid="1601580" href="...">Frostpunk 2</a>""";
        Assert.Equal("1601580", AppIdResolver.ParseAppIdFromStoreHtml(html));
    }

    [Fact]
    public void ParseAppIdFromStoreHtml_NoMatch_ReturnsNull()
    {
        Assert.Null(AppIdResolver.ParseAppIdFromStoreHtml("<div>nothing</div>"));
    }

    [Fact]
    public void FromAppIdTxt_ReadsNestedFile()
    {
        var nested = Path.Combine(_tempDir, "Engine", "Win64");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "steam_appid.txt"), "  1601580 \n");

        Assert.Equal("1601580", AppIdResolver.FromAppIdTxt(_tempDir));
    }

    [Fact]
    public void FromAppIdTxt_NoFile_ReturnsNull()
    {
        Assert.Null(AppIdResolver.FromAppIdTxt(_tempDir));
    }

    [Fact]
    public void FromIniFiles_FindsAppIdInIni()
    {
        File.WriteAllText(Path.Combine(_tempDir, "game.ini"), "[Steam]\nSteamAppId=730\n");
        Assert.Equal("730", AppIdResolver.FromIniFiles(_tempDir));
    }

    [Fact]
    public void FromIniFiles_NoMatch_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_tempDir, "game.ini"), "[Display]\nWidth=1920\n");
        Assert.Null(AppIdResolver.FromIniFiles(_tempDir));
    }
}
