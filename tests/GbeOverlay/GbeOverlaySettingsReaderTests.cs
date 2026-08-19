using System.IO;
using AchievementOverlay.GbeOverlay;
using Xunit;

namespace AchievementOverlay.Tests.GbeOverlay;

public class GbeOverlaySettingsReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _steamSettings;

    public GbeOverlaySettingsReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GbeOverlayReaderTests_" + Guid.NewGuid().ToString("N"));
        _steamSettings = Path.Combine(_tempDir, "steam_settings");
        Directory.CreateDirectory(_steamSettings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void WriteIni(string fileName, string contents) =>
        File.WriteAllText(Path.Combine(_steamSettings, fileName), contents);

    private string WriteAsset(string folder, string fileName) => WriteAssetIn(_steamSettings, folder, fileName);

    private static string WriteAssetIn(string settingsDir, string folder, string fileName)
    {
        var dir = Path.Combine(settingsDir, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    /// <summary>One folder is the common case; the multi-folder tests pass their own order.</summary>
    private static GameOverlaySettings? Read(params string[] steamSettingsDirs) =>
        new GbeOverlaySettingsReader().Read(steamSettingsDirs);

    [Fact]
    public void Read_NoSettingsDir_ReturnsNothing()
    {
        Assert.Null(Read(null));
    }

    [Fact]
    public void Read_EmptyFolder_ReturnsNothing()
    {
        Assert.Null(Read(_steamSettings));
    }

    [Fact]
    public void Read_FolderThatDoesNotExist_ReturnsNothing()
    {
        Assert.Null(Read(Path.Combine(_tempDir, "nope")));
    }

    [Fact]
    public void Read_OverlayIniWithNothingUsable_ReturnsNothing()
    {
        // The stub the Add game wizard writes, and what most games ship.
        WriteIni("configs.overlay.ini", "[overlay::general]\nenable_experimental_overlay=0\n");

        Assert.Null(Read(_steamSettings));
    }

    [Fact]
    public void Read_Duration_IsTakenFromTheIni()
    {
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nNotification_Duration_Achievement=12.0\n");

        Assert.Equal(12.0, Read(_steamSettings)!.AchievementDurationSeconds);
    }

    [Fact]
    public void Read_KeyDefinedInAnEarlierFile_WinsOverTheLaterOne()
    {
        // GBE merges app -> main -> overlay -> user with the first definition winning.
        WriteIni("configs.app.ini", "[overlay::appearance]\nNotification_Duration_Achievement=3\n");
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nNotification_Duration_Achievement=12\n");

        Assert.Equal(3.0, Read(_steamSettings)!.AchievementDurationSeconds);
    }

    [Fact]
    public void Read_KeyOnlyInALaterFile_IsStillUsed()
    {
        WriteIni("configs.app.ini", "[overlay::general]\nenable_experimental_overlay=0\n");
        WriteIni("configs.user.ini", "[overlay::appearance]\nNotification_Duration_Achievement=12\n");

        Assert.Equal(12.0, Read(_steamSettings)!.AchievementDurationSeconds);
    }

    [Fact]
    public void Read_UnlockSound_IsFoundInTheSoundsFolder()
    {
        var expected = WriteAsset("sounds", "overlay_achievement_notification.wav");

        Assert.Equal(expected, Read(_steamSettings)!.SoundFilePath);
    }

    [Fact]
    public void Read_FriendSoundOnly_IsNotMistakenForTheUnlockSound()
    {
        WriteAsset("sounds", "overlay_friend_notification.wav");

        Assert.Null(Read(_steamSettings));
    }

    [Fact]
    public void Read_RelativeFontOverride_ResolvesAgainstTheFontsFolder()
    {
        var expected = WriteAsset("fonts", "poppins.ttf");
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nFont_Override=poppins.ttf\n");

        Assert.Equal(expected, Read(_steamSettings)!.FontFilePath);
    }

    [Fact]
    public void Read_AbsoluteFontOverride_IsUsedAsGiven()
    {
        var elsewhere = Path.Combine(_tempDir, "elsewhere.ttf");
        File.WriteAllBytes(elsewhere, new byte[] { 1 });
        WriteIni("configs.overlay.ini", $"[overlay::appearance]\nFont_Override={elsewhere}\n");

        Assert.Equal(elsewhere, Read(_steamSettings)!.FontFilePath);
    }

    [Fact]
    public void Read_FontOverrideNamingAMissingFile_LeavesTheFontUnset()
    {
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nFont_Override=absent.ttf\nNotification_Duration_Achievement=9\n");

        var settings = Read(_steamSettings)!;

        Assert.Null(settings.FontFilePath);
        Assert.Equal(9.0, settings.AchievementDurationSeconds);
    }

    [Fact]
    public void Read_EditedIni_IsPickedUpWithoutARestart()
    {
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nNotification_Duration_Achievement=3\n");
        var reader = new GbeOverlaySettingsReader();
        Assert.Equal(3.0, reader.Read(new[] { _steamSettings })!.AchievementDurationSeconds);

        WriteIni("configs.overlay.ini", "[overlay::appearance]\nNotification_Duration_Achievement=12\n");
        // Two writes inside one filesystem tick are indistinguishable, so the timestamp is moved
        // explicitly rather than relying on the test running slower than the clock.
        File.SetLastWriteTimeUtc(Path.Combine(_steamSettings, "configs.overlay.ini"), DateTime.UtcNow.AddSeconds(5));

        Assert.Equal(12.0, reader.Read(new[] { _steamSettings })!.AchievementDurationSeconds);
    }

    [Fact]
    public void Read_SoundAddedToAnExistingFolder_IsPickedUp()
    {
        // The folder already exists and holds another file, so only its own timestamp can betray the
        // new one — this is what makes a wav or a ttf appearing without an ini edit visible.
        WriteAsset("sounds", "overlay_friend_notification.wav");
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nNotification_Duration_Achievement=3\n");
        var reader = new GbeOverlaySettingsReader();
        Assert.Null(reader.Read(new[] { _steamSettings })!.SoundFilePath);

        var expected = WriteAsset("sounds", "overlay_achievement_notification.wav");
        Directory.SetLastWriteTimeUtc(Path.Combine(_steamSettings, "sounds"), DateTime.UtcNow.AddSeconds(5));

        Assert.Equal(expected, reader.Read(new[] { _steamSettings })!.SoundFilePath);
    }

    [Fact]
    public void Read_UnchangedFolder_ReturnsTheSameInstance()
    {
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nNotification_Duration_Achievement=3\n");
        var reader = new GbeOverlaySettingsReader();

        Assert.Same(reader.Read(new[] { _steamSettings }), reader.Read(new[] { _steamSettings }));
    }

    [Fact]
    public void Read_SourceDescription_NamesTheFolderTheValuesCameFrom()
    {
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nNotification_Duration_Achievement=3\n");

        Assert.Equal(_steamSettings, Read(_steamSettings)!.SourceDescription);
    }

    // --- Games carrying more than one steam_settings folder ---

    /// <summary>A second settings folder, as a repack's decorated copy at the game root would be.</summary>
    private string CreateSecondSettingsDir()
    {
        var dir = Path.Combine(_tempDir, "root_steam_settings");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Read_KeyOnlyInTheSecondFolder_IsAdopted()
    {
        // The Coffin of Andy and Leyley shape: the folder the emulator reads carries only the stub,
        // and the decorated copy beside it is the sole record of what the user wanted.
        WriteIni("configs.overlay.ini", "[overlay::general]\nenable_experimental_overlay=0\n");
        var second = CreateSecondSettingsDir();
        File.WriteAllText(Path.Combine(second, "configs.overlay.ini"),
            "[overlay::appearance]\nNotification_Duration_Achievement=12\n");

        Assert.Equal(12.0, Read(_steamSettings, second)!.AchievementDurationSeconds);
    }

    [Fact]
    public void Read_KeyInBothFolders_KeepsTheFirst()
    {
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nNotification_Duration_Achievement=3\n");
        var second = CreateSecondSettingsDir();
        File.WriteAllText(Path.Combine(second, "configs.overlay.ini"),
            "[overlay::appearance]\nNotification_Duration_Achievement=12\n");

        Assert.Equal(3.0, Read(_steamSettings, second)!.AchievementDurationSeconds);
    }

    [Fact]
    public void Read_SoundOnlyInTheSecondFolder_IsUsed()
    {
        var second = CreateSecondSettingsDir();
        var expected = WriteAssetIn(second, "sounds", "overlay_achievement_notification.wav");

        Assert.Equal(expected, Read(_steamSettings, second)!.SoundFilePath);
    }

    [Fact]
    public void Read_SoundInBothFolders_PrefersTheFirst()
    {
        var expected = WriteAsset("sounds", "overlay_achievement_notification.wav");
        var second = CreateSecondSettingsDir();
        WriteAssetIn(second, "sounds", "overlay_achievement_notification.wav");

        Assert.Equal(expected, Read(_steamSettings, second)!.SoundFilePath);
    }

    [Fact]
    public void Read_FontNamedInOneFolderAndHeldInAnother_Resolves()
    {
        // Font_Override and the ttf need not live in the same folder, so the name is tried against
        // every folder rather than only the one whose ini mentioned it.
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nFont_Override=poppins.ttf\n");
        var second = CreateSecondSettingsDir();
        var expected = WriteAssetIn(second, "fonts", "poppins.ttf");

        Assert.Equal(expected, Read(_steamSettings, second)!.FontFilePath);
    }

    [Fact]
    public void Read_SecondFolderEdited_IsPickedUpWithoutARestart()
    {
        WriteIni("configs.overlay.ini", "[overlay::appearance]\nNotification_Duration_Achievement=3\n");
        var second = CreateSecondSettingsDir();
        var reader = new GbeOverlaySettingsReader();
        Assert.Null(reader.Read(new[] { _steamSettings, second })!.SoundFilePath);

        var expected = WriteAssetIn(second, "sounds", "overlay_achievement_notification.wav");
        Directory.SetLastWriteTimeUtc(Path.Combine(second, "sounds"), DateTime.UtcNow.AddSeconds(5));

        Assert.Equal(expected, reader.Read(new[] { _steamSettings, second })!.SoundFilePath);
    }

    [Fact]
    public void Read_SourceDescription_NamesEveryFolderConsulted()
    {
        var second = CreateSecondSettingsDir();
        File.WriteAllText(Path.Combine(second, "configs.overlay.ini"),
            "[overlay::appearance]\nNotification_Duration_Achievement=3\n");

        Assert.Equal($"{_steamSettings} + {second}", Read(_steamSettings, second)!.SourceDescription);
    }
}
