using System.Diagnostics;
using System.IO;
using Xunit;

namespace AchievementOverlay.Tests;

/// <summary>
/// One fixture per install layout <c>docs/pages/development/steam-dll-detection.md</c> describes. An
/// install the rules get wrong becomes one more fixture here.
/// </summary>
public sealed class SteamDllDetectorTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), "SteamDllDetectorTests_" + Guid.NewGuid().ToString("N"));

    public SteamDllDetectorTests()
    {
        Directory.CreateDirectory(_game);
    }

    public void Dispose()
    {
        if (Directory.Exists(_game))
            Directory.Delete(_game, recursive: true);
    }

    private string At(params string[] parts) => Path.Combine(new[] { _game }.Concat(parts).ToArray());

    private LoadedSteamDll Detected()
    {
        var detection = SteamDllDetector.Detect(_game);
        Assert.True(detection.Loaded != null, detection.Undecided);
        return detection.Loaded;
    }

    [Fact]
    public void Fixture_ValveDll_CarriesValvesCompanyName()
    {
        // The rules tell Valve's DLL by its version info, so the fixture has to produce one Windows reads.
        PeFixture.WriteValveDll(At("steam_api64.dll"));
        Assert.Equal("Valve Corporation", FileVersionInfo.GetVersionInfo(At("steam_api64.dll")).CompanyName);
    }

    [Fact]
    public void Fixture_Importer_ListsItsImports()
    {
        PeFixture.WriteImporter(At("Game.exe"), imports: ["kernel32.dll", "steam_api64.dll"], delayImports: ["steam_api.dll"]);
        Assert.Equal(new[] { "kernel32.dll", "steam_api.dll", "steam_api64.dll" }, SteamDllDetector.ReadImports(At("Game.exe")).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Detect_GbeBesideTheExecutable_IsTheEmulatorSteamApi()
    {
        PeFixture.WriteImporter(At("Game.exe"), imports: ["steam_api64.dll"]);
        PeFixture.WriteGbeDll(At("steam_api64.dll"));

        Assert.Equal(new LoadedSteamDll(At("steam_api64.dll"), SteamDllRule.EmulatorSteamApi), Detected());
    }

    [Fact]
    public void Detect_UnrealSteamworksFolder_LoadsWithNothingBesideIt()
    {
        var dll = At("Engine", "Binaries", "ThirdParty", "Steamworks", "Steamv157", "Win64", "steam_api64.dll");
        PeFixture.WriteImporter(At("Game", "Binaries", "Win64", "Game-Win64-Shipping.exe"), delayImports: ["steam_api64.dll"]);
        PeFixture.WriteGbeDll(dll);

        Assert.Equal(new LoadedSteamDll(dll, SteamDllRule.EmulatorSteamApi), Detected());
    }

    [Fact]
    public void Detect_UnityPluginFolder_IsValvesSteamApiOnACleanInstall()
    {
        var dll = At("Game_Data", "Plugins", "x86_64", "steam_api64.dll");
        PeFixture.WriteImporter(At("Game.exe"));
        PeFixture.WriteValveDll(dll);

        Assert.Equal(new LoadedSteamDll(dll, SteamDllRule.ValveSteamApi), Detected());
    }

    [Fact]
    public void Detect_ValveDllWithSteamEmuIni_IsAnEmulator()
    {
        // CODEX and RUNE keep Valve's DLL, version info included, and load themselves through it.
        PeFixture.WriteImporter(At("Game.exe"), imports: ["steam_api64.dll"]);
        PeFixture.WriteValveDll(At("steam_api64.dll"));
        File.WriteAllText(At("steam_emu.ini"), "[Settings]\nAppId=480\n");

        Assert.Equal(SteamDllRule.EmulatorSteamApi, Detected().Rule);
    }

    [Fact]
    public void Detect_CrackCopyInASubfolder_LosesToTheShallowerInstall()
    {
        foreach (var folder in new[] { _game, At("_crack") })
        {
            PeFixture.WriteImporter(Path.Combine(folder, "Game.exe"), imports: ["steam_api64.dll"]);
            PeFixture.WriteGbeDll(Path.Combine(folder, "steam_api64.dll"));
        }

        Assert.Equal(At("steam_api64.dll"), Detected().Path);
    }

    [Fact]
    public void Detect_SameFolderHoldsBothBuilds_Prefers64Bit()
    {
        var win64 = At("Engine", "Binaries", "ThirdParty", "Steamworks", "Steamv157", "Win64");
        PeFixture.WriteValveDll(Path.Combine(win64, "steam_api.dll"));
        PeFixture.WriteGbeDll(Path.Combine(win64, "steam_api64.dll"));
        File.WriteAllText(Path.Combine(win64, "steam_emu.ini"), "");

        Assert.Equal(Path.Combine(win64, "steam_api64.dll"), Detected().Path);
    }

    [Fact]
    public void Detect_LoaderTarget_BeatsAGbeSteamApiNothingImports()
    {
        // The Add game wizard's GBE at the root, beside no executable, while the game runs from bin\.
        PeFixture.WriteGbeDll(At("steam_api64.dll"));
        PeFixture.WriteImporter(At("bin", "Game.exe"), delayImports: ["steam_api64.dll"]);
        File.WriteAllText(At("bin", "ColdClientLoader.ini"), "[SteamClient]\nExe=Game.exe\nSteamClient64Dll=coldclient\\steamclient64.dll\n");
        PeFixture.WriteGbeDll(At("bin", "coldclient", "steamclient64.dll"));

        Assert.Equal(new LoadedSteamDll(At("bin", "coldclient", "steamclient64.dll"), SteamDllRule.ColdClientLoader), Detected());
    }

    [Fact]
    public void Detect_EmulatorSteamApiThatLoads_BeatsTheLoader()
    {
        // A GBE steam_api answers Steam calls itself and never loads the steamclient the ini names.
        File.WriteAllText(At("ColdClientLoader.ini"), "[SteamClient]\nSteamClient64Dll=steamclient64.dll\n");
        PeFixture.WriteGbeDll(At("steamclient64.dll"));
        var lib = At("Game", "www", "greenworks", "lib");
        PeFixture.WriteImporter(Path.Combine(lib, "greenworks-win32.node"), imports: ["steam_api.dll"]);
        PeFixture.WriteGbeDll(Path.Combine(lib, "steam_api.dll"));

        Assert.Equal(new LoadedSteamDll(Path.Combine(lib, "steam_api.dll"), SteamDllRule.EmulatorSteamApi), Detected());
    }

    [Fact]
    public void Detect_PackedExecutableImportingNothing_FallsToTheOnlyEmulatorDll()
    {
        PeFixture.WriteImporter(At("Game.exe"));
        PeFixture.WriteGbeDll(At("steam_api64.dll"));

        Assert.Equal(new LoadedSteamDll(At("steam_api64.dll"), SteamDllRule.OnlyEmulatorDll), Detected());
    }

    [Fact]
    public void Detect_ValveDllNothingImports_FallsToTheOnlySteamApi()
    {
        // A Python or .NET game that loads the DLL itself, from a folder no engine rule knows.
        PeFixture.WriteImporter(At("Game.exe"));
        PeFixture.WriteValveDll(At("lib", "steam_api64.dll"));

        Assert.Equal(new LoadedSteamDll(At("lib", "steam_api64.dll"), SteamDllRule.OnlySteamApi), Detected());
    }

    [Fact]
    public void Detect_OnlyEmulatorFolderHoldsMixedNames_Prefers64Bit()
    {
        // Candidates enumerate steam_api names before steamclient ones, so only the 64-bit preference
        // puts steamclient64.dll ahead of the 32-bit steam_api.dll here.
        PeFixture.WriteImporter(At("Game.exe"));
        PeFixture.WriteGbeDll(At("steam_api.dll"));
        PeFixture.WriteGbeDll(At("steamclient64.dll"));

        Assert.Equal(new LoadedSteamDll(At("steamclient64.dll"), SteamDllRule.OnlyEmulatorDll), Detected());
    }

    [Fact]
    public void Detect_TwoFoldersAtOneDepth_IsUndecided()
    {
        foreach (var folder in new[] { At("A"), At("B") })
        {
            PeFixture.WriteImporter(Path.Combine(folder, "Game.exe"), imports: ["steam_api64.dll"]);
            PeFixture.WriteGbeDll(Path.Combine(folder, "steam_api64.dll"));
        }

        var detection = SteamDllDetector.Detect(_game);
        Assert.Null(detection.Loaded);
        Assert.Contains("2 folders at one depth", detection.Undecided);
    }

    [Fact]
    public void Detect_NoSteamDll_IsUndecided() => Assert.Contains("no Steam DLL", SteamDllDetector.Detect(_game).Undecided);
}
