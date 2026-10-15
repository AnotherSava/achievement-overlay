using System.IO;
using System.Text.Json;
using Xunit;

namespace AchievementOverlay.Tests;

public sealed class SteamAppNamesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cachePath;

    public SteamAppNamesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SteamAppNamesTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cachePath = Path.Combine(_tempDir, "names.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ParseName_ReadsTheStoreName() =>
        Assert.Equal("Spacewar", SteamAppNames.ParseName("""{"480":{"success":true,"data":{"type":"game","name":"Spacewar","steam_appid":480}}}""", "480"));

    [Theory]
    [InlineData("""{"480":{"success":false}}""")]
    [InlineData("""{"440":{"success":true,"data":{"name":"Team Fortress 2"}}}""")]
    [InlineData("""{"480":{"success":true,"data":{"name":""}}}""")]
    [InlineData("""{"480":{"success":true,"data":{}}}""")]
    [InlineData("""null""")]
    [InlineData("""{"480":null}""")]
    [InlineData("""{"480":{"success":true,"data":[]}}""")]
    public void ParseName_NoNameForThatAppId_IsNull(string json) =>
        Assert.Null(SteamAppNames.ParseName(json, "480"));

    [Theory]
    [InlineData("Starfall Odyssey™ Remastered", "Starfall Odyssey Remastered")]
    [InlineData("The Long Road™ Part II", "The Long Road Part II")]
    [InlineData("MixedCase:Title™", "MixedCase:Title")]
    [InlineData("Ironclad's® Night Watch© ", "Ironclad's Night Watch")]
    [InlineData("LOUD TITLE", "LOUD TITLE")]
    public void ParseName_StripsTrademarkSignsAndKeepsCasing(string stored, string shown) =>
        Assert.Equal(shown, SteamAppNames.ParseName("""{"480":{"success":true,"data":{"name":""" + JsonSerializer.Serialize(stored) + "}}}", "480"));

    [Fact]
    public void Constructor_CleansNamesCachedBeforeTheRule()
    {
        File.WriteAllText(_cachePath, """{"480": "Spacewar™"}""");
        using var names = new SteamAppNames(_cachePath);

        Assert.Equal("Spacewar", names.Get("480"));
    }

    [Fact]
    public void Label_PrefersTheCachedStoreName_ThenTheFolderName_ThenTheAppId()
    {
        File.WriteAllText(_cachePath, """{"480": "Spacewar"}""");
        using var names = new SteamAppNames(_cachePath);
        var spacewar = new GameInfo { AppId = "480", GameName = "Spacewar folder", MetadataPath = "x", SettingsDirs = ["x"] };
        var tf2 = new GameInfo { AppId = "440", GameName = "TF2 folder", MetadataPath = "x", SettingsDirs = ["x"] };

        Assert.Equal("Spacewar", names.Label("480", spacewar));
        Assert.Equal("TF2 folder", names.Label("440", tf2));
        Assert.Equal("570", names.Label("570", null));
    }

    [Fact]
    public void Constructor_UnreadableCache_StartsEmpty()
    {
        File.WriteAllText(_cachePath, "{ not json");
        using var names = new SteamAppNames(_cachePath);

        Assert.Null(names.Get("480"));
    }
}
