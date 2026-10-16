using System.IO;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public sealed class InstallTargetTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), "InstallTargetTests_" + Guid.NewGuid().ToString("N"));

    public InstallTargetTests()
    {
        Directory.CreateDirectory(_game);
    }

    public void Dispose()
    {
        if (Directory.Exists(_game))
            Directory.Delete(_game, recursive: true);
    }

    private void WriteLoader(bool gbe)
    {
        PeFixture.WriteImporter(Path.Combine(_game, "bin", "Game.exe"), delayImports: ["steam_api64.dll"]);
        File.WriteAllText(Path.Combine(_game, "bin", "ColdClientLoader.ini"), "[SteamClient]\nSteamClient64Dll=coldclient\\steamclient64.dll\n");
        var client = Path.Combine(_game, "bin", "coldclient", "steamclient64.dll");
        if (gbe)
            PeFixture.WriteGbeDll(client);
        else
            PeFixture.WriteValveDll(client);
    }

    [Fact]
    public void Choose_SteamApiBesideTheExecutable_ReplacesIt()
    {
        PeFixture.WriteImporter(Path.Combine(_game, "Game.exe"), imports: ["steam_api64.dll"]);
        PeFixture.WriteValveDll(Path.Combine(_game, "steam_api64.dll"));

        var (target, _) = InstallTarget.Choose(_game);
        Assert.Equal(InstallAction.ReplaceSteamApi, target!.Action);
        Assert.Equal(Path.Combine(_game, "steam_settings"), target.Dll.SettingsDir);
    }

    [Fact]
    public void Choose_GbeSteamClientBehindALoader_KeepsIt()
    {
        WriteLoader(gbe: true);

        var (target, _) = InstallTarget.Choose(_game);
        Assert.Equal(InstallAction.KeepGbe, target!.Action);
        Assert.Equal(Path.Combine(_game, "bin", "coldclient", "steam_settings"), target.Dll.SettingsDir);
    }

    [Fact]
    public void Choose_OtherSteamClientBehindALoader_IsRefused()
    {
        WriteLoader(gbe: false);

        var (target, refusal) = InstallTarget.Choose(_game);
        Assert.Null(target);
        Assert.Contains("not GBE", refusal);
    }

    [Fact]
    public void Choose_ThirtyTwoBitSteamApi_CannotTakeGbe()
    {
        PeFixture.WriteImporter(Path.Combine(_game, "Game.exe"), imports: ["steam_api.dll"]);
        PeFixture.WriteValveDll(Path.Combine(_game, "steam_api.dll"));

        var (target, _) = InstallTarget.Choose(_game);
        Assert.Contains("32-bit steam_api.dll", target!.InstallRefusal);
    }

    [Fact]
    public void Choose_SixtyFourBitSteamApi_CanTakeGbe()
    {
        PeFixture.WriteImporter(Path.Combine(_game, "Game.exe"), imports: ["steam_api64.dll"]);
        PeFixture.WriteValveDll(Path.Combine(_game, "steam_api64.dll"));

        Assert.Null(InstallTarget.Choose(_game).Target!.InstallRefusal);
    }

    [Fact]
    public void Choose_NoSteamDll_IsRefused() => Assert.Contains("no Steam DLL", InstallTarget.Choose(_game).Refusal);
}
