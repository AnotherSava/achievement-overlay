using System.IO;
using System.Net.Http;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

/// <summary>
/// The run's keep gating: a game that keeps its emulator gets the schema and <c>steam_appid.txt</c>
/// and nothing else — no DRM check, no download, no backup and no DLL replacement — and is exempt
/// from <see cref="InstallTarget.InstallRefusal"/>. A wrong keep would copy GBE's
/// <c>steam_api64.dll</c> over a repack's GBE <c>steamclient64.dll</c>.
/// </summary>
public sealed class GbeConfigGeneratorTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), "GbeConfigGeneratorTests_" + Guid.NewGuid().ToString("N"));
    private readonly Progress _progress = new();

    public GbeConfigGeneratorTests()
    {
        Directory.CreateDirectory(_game);
    }

    public void Dispose()
    {
        if (Directory.Exists(_game))
            Directory.Delete(_game, recursive: true);
    }

    [Fact]
    public async Task Run_GbeSteamClientBehindALoader_WritesTheSchemaAndInstallsNothing()
    {
        PeFixture.WriteImporter(Path.Combine(_game, "bin", "Game.exe"), delayImports: ["steam_api64.dll"]);
        await File.WriteAllTextAsync(Path.Combine(_game, "bin", "ColdClientLoader.ini"), "[SteamClient]\nSteamClient64Dll=coldclient\\steamclient64.dll\n");
        var client = Path.Combine(_game, "bin", "coldclient", "steamclient64.dll");
        PeFixture.WriteGbeDll(client);
        // Would abort a replacing run: Denuvo named in an executable, with no crack indicator beside it.
        await File.WriteAllTextAsync(Path.Combine(_game, "Launcher.exe"), "bytes Denuvo Anti-Tamper bytes");
        var before = await File.ReadAllBytesAsync(client);

        var status = await RunAsync(keepEmulator: false);

        Assert.Equal(ConfigStatus.Success, status);
        var settings = Path.Combine(_game, "bin", "coldclient", "steam_settings");
        AssertOnlySchemaWritten(settings);
        Assert.Equal(before, await File.ReadAllBytesAsync(client));
        Assert.False(File.Exists(client + GameReverter.BackupExtension));
        Assert.DoesNotContain(_progress.Lines, line => line.Contains("Denuvo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_KeptEmulatorOnA32BitSteamApi_IsNotRefused()
    {
        var dll = WriteRune32Bit();
        var before = await File.ReadAllBytesAsync(dll);

        var status = await RunAsync(keepEmulator: true);

        Assert.Equal(ConfigStatus.Success, status);
        AssertOnlySchemaWritten(Path.Combine(_game, "steam_settings"));
        Assert.Equal(before, await File.ReadAllBytesAsync(dll));
        Assert.False(File.Exists(dll + GameReverter.BackupExtension));
    }

    [Fact]
    public async Task Run_ReplacedEmulatorOnA32BitSteamApi_IsRefusedBeforeWriting()
    {
        WriteRune32Bit();

        var status = await RunAsync(keepEmulator: false);

        Assert.Equal(ConfigStatus.UserError, status);
        Assert.Contains(_progress.Lines, line => line.Contains("32-bit steam_api.dll", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(_game, "steam_settings")));
    }

    private string WriteRune32Bit()
    {
        PeFixture.WriteImporter(Path.Combine(_game, "Game.exe"), imports: ["steam_api.dll"]);
        var dll = Path.Combine(_game, "steam_api.dll");
        PeFixture.WriteValveDll(dll);
        File.WriteAllText(Path.Combine(_game, SceneEmulatorDetector.EmuIni), "[Settings]\nAppId=480\n");
        return dll;
    }

    private static void AssertOnlySchemaWritten(string settings)
    {
        Assert.True(File.Exists(Path.Combine(settings, "achievements.json")));
        Assert.Equal("480", File.ReadAllText(Path.Combine(settings, "steam_appid.txt")));
        Assert.False(File.Exists(Path.Combine(settings, "configs.overlay.ini")));
        Assert.False(File.Exists(Path.Combine(settings, "steam_interfaces.txt")));
    }

    private async Task<ConfigStatus> RunAsync(bool keepEmulator)
    {
        var request = new ConfigRequest
        {
            GameDir = _game,
            AppId = "480",
            ApiKey = "test-key",
            GbeFolder = Path.Combine(_game, "gbe-not-used"),
            KeepEmulator = keepEmulator,
        };
        // Any request fails the test: a kept emulator downloads nothing, and the schema is prefetched.
        using var handler = new RefusingHandler();
        using var http = new HttpClient(handler, disposeHandler: false);
        return await new GbeConfigGenerator(request, _progress, http, prefetchedSchema: []).RunAsync();
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
    }

    private sealed class Progress : IConfigProgress
    {
        public List<string> Lines { get; } = [];

        public void Report(ConfigLogLevel level, string message) => Lines.Add(message);

        public void UpdateStep(string text)
        {
        }

        public void FailStep(string message) => Lines.Add(message);
    }
}
