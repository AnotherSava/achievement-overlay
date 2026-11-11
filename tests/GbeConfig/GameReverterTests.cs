using System.IO;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public sealed class GameReverterTests : IDisposable
{
    private readonly string _gameDir;
    private readonly string _settingsDir;
    private readonly string _dll;

    public GameReverterTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "GameReverterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_gameDir);
        _settingsDir = Path.Combine(_gameDir, "steam_settings");
        _dll = Path.Combine(_gameDir, "steam_api64.dll");
        File.WriteAllText(_dll, "game's own dll");
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameDir))
            Directory.Delete(_gameDir, true);
    }

    private string Settings(string relative) => Path.Combine(_settingsDir, relative);

    /// <summary>Writes through the record, as the wizard does.</summary>
    private static void Write(SetupRecord record, string path, string text)
    {
        record.BeforeWrite(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Backs the DLL up and replaces it with "gbe", recording both, as the wizard does.</summary>
    private void ReplaceDll(SetupRecord record)
    {
        File.Copy(_dll, _dll + GameReverter.BackupExtension);
        record.RecordDll("steam_api64.dll", "steam_api64.dll" + GameReverter.BackupExtension);
        File.WriteAllText(_dll, "gbe");
    }

    [Fact]
    public void Revert_FreshSetup_LeavesTheGameAsItWas()
    {
        var record = SetupRecord.Begin(_settingsDir);
        Write(record, Settings("achievements.json"), "[]");
        Write(record, Settings(Path.Combine("achievement_images", "a.jpg")), "icon");
        ReplaceDll(record);
        record.Save();

        var result = GameReverter.Revert(_settingsDir);

        Assert.Equal("game's own dll", File.ReadAllText(_dll));
        Assert.False(File.Exists(_dll + GameReverter.BackupExtension));
        Assert.False(Directory.Exists(_settingsDir));
        Assert.Empty(result.Kept);
    }

    [Fact]
    public void Revert_OverARepacksSettings_PutsItsFilesBackAndKeepsTheRest()
    {
        Directory.CreateDirectory(_settingsDir);
        File.WriteAllText(Settings("achievements.json"), "repack's");
        File.WriteAllText(Settings("configs.app.ini"), "repack's app config");
        Directory.CreateDirectory(Settings("empty_from_repack"));

        var record = SetupRecord.Begin(_settingsDir);
        Write(record, Settings("achievements.json"), "wizard's");
        Write(record, Settings(Path.Combine("achievement_images", "a.jpg")), "icon");
        record.Save();

        GameReverter.Revert(_settingsDir);

        Assert.Equal("repack's", File.ReadAllText(Settings("achievements.json")));
        Assert.Equal("repack's app config", File.ReadAllText(Settings("configs.app.ini")));
        Assert.False(Directory.Exists(Settings("achievement_images")));
        Assert.True(Directory.Exists(Settings("empty_from_repack")));
        Assert.False(File.Exists(Settings(SetupRecord.FileName)));
        Assert.False(Directory.Exists(Settings(SetupRecord.BackupFolder)));
    }

    [Fact]
    public void Revert_AfterASecondRun_StillRemovesWhatTheFirstRunCreated()
    {
        var first = SetupRecord.Begin(_settingsDir);
        Write(first, Settings("achievements.json"), "first run");
        first.Save();

        var second = SetupRecord.Begin(_settingsDir);
        Write(second, Settings("achievements.json"), "second run");
        second.Save();

        GameReverter.Revert(_settingsDir);

        Assert.False(Directory.Exists(_settingsDir));
    }

    [Fact]
    public void Revert_ARepackFileOverwrittenTwice_PutsBackTheRepacksCopy()
    {
        Directory.CreateDirectory(_settingsDir);
        File.WriteAllText(Settings("achievements.json"), "repack's");

        var first = SetupRecord.Begin(_settingsDir);
        Write(first, Settings("achievements.json"), "first run");
        first.Save();

        var second = SetupRecord.Begin(_settingsDir);
        Write(second, Settings("achievements.json"), "second run");
        second.Save();

        GameReverter.Revert(_settingsDir);

        Assert.Equal("repack's", File.ReadAllText(Settings("achievements.json")));
    }

    [Fact]
    public void Revert_NoBackupWasMade_KeepsTheDllAndSaysWhy()
    {
        var record = SetupRecord.Begin(_settingsDir);
        Write(record, Settings("achievements.json"), "[]");
        record.RecordDll("steam_api64.dll", null);
        File.WriteAllText(_dll, "gbe");
        record.Save();

        var result = GameReverter.Revert(_settingsDir);

        Assert.Equal("gbe", File.ReadAllText(_dll));
        Assert.Contains(result.Kept, line => line.Contains("no backup", StringComparison.Ordinal));
    }

    [Fact]
    public void Revert_SetUpBeforeTheRecord_RestoresTheDllAndKeepsEveryFile()
    {
        Directory.CreateDirectory(_settingsDir);
        File.WriteAllText(Settings("achievements.json"), "[]");
        File.Move(_dll, _dll + GameReverter.BackupExtension);
        File.WriteAllText(_dll, "gbe");

        Assert.Null(GameReverter.CannotRevert(_settingsDir));
        var result = GameReverter.Revert(_settingsDir);

        Assert.Equal("game's own dll", File.ReadAllText(_dll));
        Assert.False(File.Exists(_dll + GameReverter.BackupExtension));
        Assert.True(File.Exists(Settings("achievements.json")));
        Assert.Single(result.Kept);
    }

    [Fact]
    public void CannotRevert_NoRecordAndNoBackup_SaysSo()
    {
        Directory.CreateDirectory(_settingsDir);
        File.WriteAllText(Settings("achievements.json"), "[]");

        Assert.NotNull(GameReverter.CannotRevert(_settingsDir));
    }
}
