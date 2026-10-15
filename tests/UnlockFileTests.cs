using System.IO;
using System.Text.Json;
using Xunit;

namespace AchievementOverlay.Tests;

public sealed class UnlockFileTests : IDisposable
{
    /// <summary>Shaped like a RUNE file: sections sorted by name, the index section between them.</summary>
    private const string RuneFile = "[BeatTheTutorial]\r\nAchieved=1\r\nCurProgress=0\r\nMaxProgress=0\r\nUnlockTime=1700000000\r\n\r\n"
        + "[SteamAchievements]\r\n00000=BeatTheTutorial\r\n00001=CarStorage\r\nCount=2\r\n\r\n"
        + "[CarStorage]\r\nAchieved=1\r\nCurProgress=0\r\nMaxProgress=0\r\nUnlockTime=1710018873\r\n";

    private readonly string _tempDir;

    public UnlockFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "UnlockFileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ParseIni_RuneFile_ReadsEveryAchievementAndSkipsTheIndex()
    {
        var states = AchievementMetadata.ParseIniUnlockStates(RuneFile);

        Assert.Equal(["BeatTheTutorial", "CarStorage"], states.Keys);
        Assert.True(states["CarStorage"].Earned);
        Assert.Equal(1710018873L, states["CarStorage"].EarnedTime);
        Assert.Null(states["CarStorage"].DisplayName);
    }

    [Fact]
    public void ParseIni_IndexOnly_IsEmpty() =>
        Assert.Empty(AchievementMetadata.ParseIniUnlockStates("[SteamAchievements]\r\nCount=0\r\n"));

    [Fact]
    public void ParseIni_AchievedZero_IsNotEarned()
    {
        var states = AchievementMetadata.ParseIniUnlockStates("[A]\nAchieved=0\nCurProgress=3\nMaxProgress=10\nUnlockTime=0\n");
        Assert.False(states["A"].Earned);
    }

    [Theory]
    [InlineData("CurProgress=5\nMaxProgress=5", true)]
    [InlineData("CurProgress=6\nMaxProgress=5", true)]
    [InlineData("CurProgress=4\nMaxProgress=5", false)]
    [InlineData("CurProgress=0\nMaxProgress=0", false)]
    [InlineData("", false)]
    public void ParseIni_NoAchievedKey_EarnedWhenProgressReachedANonZeroTarget(string progress, bool earned)
    {
        // How CODEX's Gears 5 and Gears Tactics record an unlock.
        var states = AchievementMetadata.ParseIniUnlockStates($"[A]\n{progress}\nUnlockTime=1700000000\n");
        Assert.Equal(earned, states["A"].Earned);
    }

    [Fact]
    public void ParseIni_NoUnlockTime_ReadsAsZero() =>
        Assert.Equal(0L, AchievementMetadata.ParseIniUnlockStates("[A]\nAchieved=1\n")["A"].EarnedTime);

    [Theory]
    [InlineData("Achieved=yes\nUnlockTime=1700000000")]
    [InlineData("Achieved=1\nUnlockTime=soon")]
    public void ParseIni_BadValue_CostsOnlyThatAchievement(string bad)
    {
        var states = AchievementMetadata.ParseIniUnlockStates($"[Bad]\n{bad}\n\n[Good]\nAchieved=1\nUnlockTime=1700000000\n");

        Assert.Equal(["Good"], states.Keys);
    }

    [Fact]
    public void Find_PrefersJson_ThenIni_ThenNothing()
    {
        Assert.Null(UnlockFile.Find(_tempDir));

        File.WriteAllText(Path.Combine(_tempDir, UnlockFile.IniName), RuneFile);
        Assert.Equal(Path.Combine(_tempDir, UnlockFile.IniName), UnlockFile.Find(_tempDir));

        File.WriteAllText(Path.Combine(_tempDir, UnlockFile.JsonName), "{}");
        Assert.Equal(Path.Combine(_tempDir, UnlockFile.JsonName), UnlockFile.Find(_tempDir));
    }

    [Fact]
    public void Parse_DispatchesOnTheFileName()
    {
        Assert.Equal(2, UnlockFile.Parse(Path.Combine(_tempDir, "ACHIEVEMENTS.INI"), RuneFile).Count);
        Assert.Throws<JsonException>(() => UnlockFile.Parse(Path.Combine(_tempDir, UnlockFile.JsonName), RuneFile));
    }
}
