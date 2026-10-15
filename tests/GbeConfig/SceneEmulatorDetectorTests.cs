using System.IO;
using System.Text;
using AchievementOverlay.GbeConfig;
using AchievementOverlay.GbeOverlay;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public sealed class SceneEmulatorDetectorTests : IDisposable
{
    private const string EmuIni = "[Settings]\r\nAppId=480\r\nUserName=Player\r\nLanguage=english\r\n[Interfaces]\r\nSteamClient=SteamClient017\r\n";

    private readonly string _tempDir;
    private readonly string _dll;

    public SceneEmulatorDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SceneEmulatorDetectorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dll = Path.Combine(_tempDir, "steam_api64.dll");
        File.WriteAllBytes(_dll, Encoding.ASCII.GetBytes("MZ loader with Valve Corporation version info"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(_tempDir, name), text);

    [Fact]
    public void Detect_NoEmuIni_ReturnsNull() => Assert.Null(SceneEmulatorDetector.Detect(_dll));

    [Fact]
    public void Detect_Rune_NamesRuneAndItsSavesFolder()
    {
        Write("steam_emu.ini", EmuIni);
        Write("steam_api64.rne", "");

        var emulator = SceneEmulatorDetector.Detect(_dll);

        Assert.NotNull(emulator);
        Assert.Equal("RUNE", emulator.Name);
        Assert.Equal("480", emulator.AppId);
        Assert.Equal([SceneEmulatorDetector.RuneSaves], emulator.SavesFolders);
        Assert.Equal(["SteamClient017"], emulator.Interfaces);
    }

    [Fact]
    public void Detect_Codex_NamesCodexAndItsSavesFolder()
    {
        Write("steam_emu.ini", EmuIni);
        Write("steam_api64.cdx", "");

        var emulator = SceneEmulatorDetector.Detect(_dll);

        Assert.NotNull(emulator);
        Assert.Equal("CODEX", emulator.Name);
        Assert.Equal([SceneEmulatorDetector.CodexSaves], emulator.SavesFolders);
    }

    [Fact]
    public void Detect_NoPayloadFiles_OffersBothSavesFolders()
    {
        Write("steam_emu.ini", EmuIni);

        var emulator = SceneEmulatorDetector.Detect(_dll);

        Assert.NotNull(emulator);
        Assert.Equal("CODEX/RUNE", emulator.Name);
        Assert.Equal([SceneEmulatorDetector.RuneSaves, SceneEmulatorDetector.CodexSaves], emulator.SavesFolders);
    }

    [Theory]
    [InlineData("GSE Saves", false)]
    [InlineData("GSE Saves", true)]
    [InlineData("Goldberg SteamEmu Saves", false)]
    public void Detect_EmulatorFilesBesideGbe_ReturnsNull(string marker, bool utf16)
    {
        // A game switched to GBE keeps the emulator's files beside the new DLL.
        Write("steam_emu.ini", EmuIni);
        Write("steam_api64.rne", "");
        var encoding = utf16 ? Encoding.Unicode : Encoding.ASCII;
        File.WriteAllBytes(_dll, [.. Encoding.ASCII.GetBytes("MZ padding "), .. encoding.GetBytes(marker), .. Encoding.ASCII.GetBytes(" tail")]);

        Assert.Null(SceneEmulatorDetector.Detect(_dll));
    }

    [Fact]
    public void Detect_EmuIniWithoutAppId_StillDetectsTheEmulator()
    {
        Write("steam_emu.ini", "[Settings]\r\nUserName=Player\r\n");
        Write("steam_api64.rne", "");

        var emulator = SceneEmulatorDetector.Detect(_dll);

        Assert.NotNull(emulator);
        Assert.Null(emulator.AppId);
    }

    [Theory]
    [InlineData("[Settings]\r\nAppId=440\r\n", "440")]
    [InlineData("[settings]\nappid = 570\n", "570")]
    [InlineData("[Settings]\r\nAppId=\r\n", null)]
    [InlineData("[Settings]\r\nAppId=12a\r\n", null)]
    [InlineData("[Other]\r\nAppId=440\r\n", null)]
    public void ReadAppId_ReadsOnlyANumericSettingsAppId(string text, string? expected) =>
        Assert.Equal(expected, SceneEmulatorDetector.ReadAppId(IniFile.Parse(text)));
}
