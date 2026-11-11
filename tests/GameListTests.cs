using System.IO;
using System.Text;
using Xunit;

namespace AchievementOverlay.Tests;

public sealed class GameListTests : IDisposable
{
    private readonly string _dir;

    public GameListTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "GameListTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private void WriteDll(string name, string content) => File.WriteAllBytes(Path.Combine(_dir, name), Encoding.ASCII.GetBytes(content));

    [Fact]
    public void WithInstalledGames_AddsOnlyAppIdsNotListed_NamedAfterTheirFolder()
    {
        var games = new List<GameChoice> { new() { AppId = "10", Name = "Configured" }, new() { AppId = "20", Name = "From saves", Unconfigured = true } };
        var installFolders = new Dictionary<string, string> { ["10"] = @"C:\Games\Configured", ["20"] = @"C:\Games\FromSaves", ["30"] = @"C:\Games\Some Game" };

        var all = GameList.WithInstalledGames(games, installFolders);

        Assert.Equal(["10", "20", "30"], all.Select(game => game.AppId));
        var added = all.Single(game => game.AppId == "30");
        Assert.True(added.Unconfigured);
        Assert.Equal("Some Game", added.Name);
    }

    [Theory]
    [InlineData(27, 30, true, "27 / 30")]
    [InlineData(0, 54, false, "0 / 54")]
    [InlineData(3, null, true, "3 / ?")]
    [InlineData(0, null, false, "")]
    public void AchievementsText_SaysOnlyWhatIsKnown(int earned, int? total, bool hasUnlockFile, string expected) =>
        Assert.Equal(expected, GameList.AchievementsText(earned, total, hasUnlockFile));

    [Theory]
    [InlineData(0, "1m")]
    [InlineData(-5, "1m")]
    [InlineData(59, "59m")]
    [InlineData(60, "1h")]
    [InlineData(60 * 24, "1d")]
    [InlineData(60 * 24 * 29, "29d")]
    [InlineData(60 * 24 * 30, "1mo")]
    [InlineData(60 * 24 * 364, "12mo")]
    [InlineData(60 * 24 * 365, "1y")]
    public void Interval_TakesTheLargestWholeUnit(int minutes, string expected) =>
        Assert.Equal(expected, TimeFormat.Interval(TimeSpan.FromMinutes(minutes)));

    [Theory]
    [InlineData("MZ ... GSE Saves ...", "GBE")]
    [InlineData("MZ ... GSE Saves ... STEAM_LOG ...", "GBE debug")]
    [InlineData("MZ ... Valve Corporation ...", "Steam")]
    public void EmulatorInFolder_ReadsTheSteamDll(string dll, string expected)
    {
        WriteDll("steam_api64.dll", dll);
        Assert.Equal(expected, GameList.EmulatorInFolder(_dir));
    }

    [Fact]
    public void EmulatorInFolder_RuneLoaderBesideItsIni_IsRune()
    {
        WriteDll("steam_api64.dll", "MZ ... Valve Corporation ...");
        WriteDll("steam_api64.rne", "");
        File.WriteAllText(Path.Combine(_dir, "steam_emu.ini"), "[Settings]\nAppId=480\n");

        Assert.Equal("RUNE", GameList.EmulatorInFolder(_dir));
    }

    [Fact]
    public void EmulatorInFolder_GbeSteamclientBehindALoader_IsGbeLoader()
    {
        WriteDll("steamclient64.dll", "MZ ... GSE Saves ...");
        Assert.Equal("GBE (loader)", GameList.EmulatorInFolder(_dir));
    }

    [Theory]
    [InlineData("RUNE", "achievements.ini", "RUNE")]
    [InlineData("CODEX", "achievements.ini", "CODEX")]
    [InlineData("Other", "achievements.ini", "CODEX/RUNE")]
    [InlineData("GSE Saves", "achievements.json", "GBE")]
    [InlineData("GSE Saves", null, "GBE")]
    public void EmulatorOfSaveFolder_ReadsTheFolderAndTheFile(string savesFolder, string? file, string expected)
    {
        var appIdFolder = Path.Combine(_dir, savesFolder, "480");
        var unlockFile = file != null ? Path.Combine(appIdFolder, file) : null;
        Assert.Equal(expected, GameList.EmulatorOfSaveFolder(appIdFolder, unlockFile, new Dictionary<string, AchievementUnlockState>()));
    }

    [Fact]
    public void EmulatorOfSaveFolder_JsonNamingItsOwnAchievements_IsAnotherEmulator()
    {
        var states = new Dictionary<string, AchievementUnlockState> { ["A"] = new() { Earned = true, DisplayName = System.Text.Json.JsonSerializer.SerializeToElement("Named here") } };
        var appIdFolder = Path.Combine(_dir, "GSE Saves", "480");
        Assert.Equal("Other emulator", GameList.EmulatorOfSaveFolder(appIdFolder, Path.Combine(appIdFolder, "achievements.json"), states));
    }
}
