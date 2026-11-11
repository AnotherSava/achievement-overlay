using System.IO;
using System.Net.Http;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// Orchestrates generation of a GBE-compatible <c>steam_settings/</c> folder for
/// achievement tracking, running the steps in dependency order. Front-end agnostic —
/// progress is reported through <see cref="IConfigProgress"/>.
/// </summary>
public sealed class GbeConfigGenerator
{
    private const string OverlayConfig = """
        [overlay::general]
        enable_experimental_overlay=0
        """;

    private readonly ConfigRequest _req;
    private readonly IConfigProgress _progress;
    private readonly HttpClient _http;
    private readonly IReadOnlyList<SteamSchemaAchievement>? _prefetchedSchema;

    public GbeConfigGenerator(ConfigRequest request, IConfigProgress progress, HttpClient http,
        IReadOnlyList<SteamSchemaAchievement>? prefetchedSchema = null)
    {
        _req = request;
        _progress = progress;
        _http = http;
        _prefetchedSchema = prefetchedSchema;
    }

    public async Task<ConfigStatus> RunAsync(CancellationToken ct = default)
    {
        var gameDir = Path.GetFullPath(_req.GameDir);
        if (!Directory.Exists(gameDir))
        {
            Error($"Game directory not found: '{gameDir}'");
            return ConfigStatus.UserError;
        }

        if (string.IsNullOrWhiteSpace(_req.ApiKey))
        {
            Error("A Steam Web API key is required.");
            return ConfigStatus.UserError;
        }

        if (string.IsNullOrWhiteSpace(_req.AppId))
        {
            Error("A Steam AppID is required.");
            return ConfigStatus.UserError;
        }

        // --- Locate the Steam DLL the game loads (defines the target directory) ---
        var (target, refusal) = InstallTarget.Choose(gameDir);
        if (target == null)
        {
            Error(refusal!);
            return ConfigStatus.UserError;
        }
        var dllPath = target.Dll.Path;
        Info($"Steam DLL: {dllPath} (rule {target.Dll.RuleName})");
        var keep = _req.KeepEmulator || target.Action == InstallAction.KeepGbe;
        if (!keep && target.InstallRefusal is { } cannotInstall)
        {
            Error(cannotInstall);
            return ConfigStatus.UserError;
        }

        var appId = _req.AppId.Trim();
        Info($"AppID: {appId}");

        // --- DRM detection ---
        // It asks whether the game will load the GBE DLL about to be installed, so it has nothing to say
        // about a game that keeps its emulator.
        if (!keep && !ReportDrm(DrmDetector.Analyze(gameDir)))
            return ConfigStatus.UserError;

        var steamSettingsDir = target.Dll.SettingsDir;
        if (Directory.Exists(steamSettingsDir) && !_req.Force)
        {
            Error($"steam_settings/ already exists at '{steamSettingsDir}'. Enable 'Overwrite existing config' to replace it.");
            return ConfigStatus.UserError;
        }

        // --- GBE binaries ---
        GbeRelease? gbe = null;
        try
        {
            if (!keep)
                gbe = await GbeBinaryManager.EnsureAsync(_req.GbeFolder, _req.DownloadLatest, _req.DebugBuild, _http, _progress, ct);
        }
        catch (Exception ex) when (IsAntivirusBlock(ex))
        {
            Error("Windows Defender blocked the GBE download as a virus / potentially unwanted software. This is a known "
                + "false positive — GBE's Steam emulator is flagged by Defender, not actually malware.");
            Info("To proceed, do one of the following and try again:");
            Info($"  1. Add a Defender exclusion for the GBE folder ({_req.GbeFolder}) AND the game folder, then retry.");
            Info("  2. Or download and extract a GBE release yourself, then on the Ready page open 'Advanced options', set "
                + "'GBE release folder' to it, and uncheck 'Download the latest GBE release'.");
            Info("Note: Defender may also quarantine the emulator DLL after it's copied into the game — the game folder "
                + "exclusion covers that too.");
            return ConfigStatus.DefenderBlocked;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            Error($"Failed to obtain GBE binaries: {ex.Message}");
            return ConfigStatus.Failure;
        }

        // --- Fetch achievement schema ---
        List<SteamSchemaAchievement> schema;
        if (_prefetchedSchema != null)
        {
            schema = _prefetchedSchema.ToList(); // already fetched by the wizard
        }
        else
        {
            try
            {
                Step("Fetching achievement schema...");
                schema = await SteamWebApi.GetAchievementSchemaAsync(_req.ApiKey, appId, _http, ct);
            }
            catch (HttpRequestException ex)
            {
                Error($"Failed to fetch achievement schema: {ex.Message}");
                return ConfigStatus.Failure;
            }
        }

        if (schema.Count == 0)
            Warn("No achievements returned for this AppID (check the API key and that the game has achievements).");
        Info($"Achievements: {schema.Count}");

        // Saved even when the run fails part-way, so whatever it did write can still be reverted.
        var record = SetupRecord.Begin(steamSettingsDir);
        try
        {
            return await WriteAsync(record, schema, appId, gbe, dllPath, steamSettingsDir, ct);
        }
        finally
        {
            record.Save();
        }
    }

    /// <summary>Writes the config and installs GBE, recording every file it writes in <paramref name="record"/>.</summary>
    private async Task<ConfigStatus> WriteAsync(SetupRecord record, List<SteamSchemaAchievement> schema, string appId, GbeRelease? gbe, string dllPath, string steamSettingsDir, CancellationToken ct)
    {
        Directory.CreateDirectory(steamSettingsDir);
        var partial = false;

        // --- Build achievements.json + apply hidden descriptions ---
        var achievements = AchievementSchemaWriter.Build(schema);
        var hiddenCount = schema.Count(a => a.Hidden != 0);
        if (hiddenCount > 0)
        {
            Step($"Fetching {AppUtilities.CountOf(hiddenCount, "hidden description")} from SteamDB...");
            var scrape = await SteamDbScraper.FetchHiddenDescriptionsAsync(appId, _req.FirecrawlApiKey, _http, ct);
            if (scrape.Descriptions == null)
            {
                _progress.FailStep($"Couldn't fetch hidden descriptions from SteamDB. {scrape.FailureReason}");
                Warn($"Hidden achievements are left with placeholder descriptions. To fill them in by hand, see "
                    + $"{SteamDbScraper.StatsPageUrl(appId)}");
                partial = true;
            }
            else
            {
                AchievementSchemaWriter.ApplyDescriptions(achievements, scrape.Descriptions);
                var missing = schema.Where(a => a.Hidden != 0 && !scrape.Descriptions.ContainsKey(a.Name))
                    .Select(a => a.Name).ToList();
                Info($"Patched {hiddenCount - missing.Count} of {AppUtilities.CountOf(hiddenCount, "hidden description")} "
                    + $"({AppUtilities.CountOf(scrape.Descriptions.Count, "hidden description")} found on the SteamDB page).");
                if (missing.Count > 0)
                {
                    Warn($"SteamDB had no description for {AppUtilities.CountOf(missing.Count, "hidden achievement")}: {Summarize(missing)}");
                    partial = true;
                }
            }
        }

        // --- Download icons ---
        Step("Downloading achievement icons...");
        var imageDir = Path.Combine(steamSettingsDir, AchievementSchemaWriter.ImageDir);
        var iconResult = await IconDownloader.DownloadAllAsync(schema, imageDir, _http, record.BeforeWrite, overwrite: _req.Force, ct: ct);
        Info($"Icons: {iconResult.Downloaded} downloaded, {iconResult.Skipped} skipped, {iconResult.Failed} failed.");
        if (iconResult.Failed > 0)
        {
            Warn($"{AppUtilities.CountOf(iconResult.Failed, "icon")} failed to download, the first with: {iconResult.FirstError}");
            partial = true;
        }

        // --- Write config files ---
        await WriteFileAsync(record, Path.Combine(steamSettingsDir, "achievements.json"), AchievementSchemaWriter.Serialize(achievements), ct);
        await WriteFileAsync(record, Path.Combine(steamSettingsDir, "steam_appid.txt"), appId, ct);
        if (gbe == null)
        {
            Info("Wrote achievements.json, steam_appid.txt");
            Info("Kept the game's own Steam emulator; it records unlocks in a file the overlay reads.");
            return Finish(partial, steamSettingsDir);
        }

        await WriteFileAsync(record, Path.Combine(steamSettingsDir, "configs.overlay.ini"), OverlayConfig, ct);
        Info("Wrote achievements.json, steam_appid.txt, configs.overlay.ini");

        // --- Back up original DLL and generate interfaces against it ---
        // Read before the backup and the copy below, while the DLL is still the emulator's.
        var emulator = SceneEmulatorDetector.Detect(dllPath);
        var interfaceSourceDll = dllPath;
        string? backupName = null;
        if (_req.Backup)
        {
            var backupPath = dllPath + GameReverter.BackupExtension;
            backupName = Path.GetFileName(backupPath);
            if (!File.Exists(backupPath))
            {
                File.Copy(dllPath, backupPath);
                Info($"Backed up original DLL to {Path.GetFileName(backupPath)}");
            }
            else
            {
                Info($"Backup {Path.GetFileName(backupPath)} already exists; keeping it as the original.");
            }
            interfaceSourceDll = backupPath;
        }

        // GBE's tool finds no interface strings in an emulator's loader. The game's own library, where the
        // emulator kept it, is what the game was built against; steam_emu.ini's list is the fallback.
        record.BeforeWrite(Path.Combine(steamSettingsDir, "steam_interfaces.txt"));
        if (emulator?.OriginalDll is { } originalDll && await GenerateInterfacesAsync(gbe, originalDll, steamSettingsDir, ct))
        {
            Info($"Wrote steam_interfaces.txt from the game's original Steam library, {Path.GetFileName(originalDll)}.");
        }
        else if (emulator is { Interfaces.Count: > 0 })
        {
            await File.WriteAllLinesAsync(Path.Combine(steamSettingsDir, "steam_interfaces.txt"), emulator.Interfaces, ct);
            Info($"Wrote steam_interfaces.txt from the {AppUtilities.CountOf(emulator.Interfaces.Count, "interface")} {SceneEmulatorDetector.EmuIni} lists.");
        }
        else if (!await GenerateInterfacesAsync(gbe, interfaceSourceDll, steamSettingsDir, ct))
        {
            partial = true;
        }

        if (_req.KeepPlayerId && emulator?.PlayerSteamId is { } steamId)
        {
            await WriteFileAsync(record, Path.Combine(steamSettingsDir, "configs.user.ini"), $"[user::general]\naccount_steamid={steamId}\n", ct);
            Info($"Wrote configs.user.ini: GBE plays as the {emulator.Name} emulator's Steam ID ({steamId}), so saves filed under it stay found.");
        }

        // --- Replace the DLL with GBE's regular build ---
        Step(_req.DebugBuild ? "Installing GBE steam_api64.dll (debug build)..." : "Installing GBE steam_api64.dll...");
        record.RecordDll(Path.GetFileName(dllPath), backupName);
        File.Copy(gbe.RegularDll64, dllPath, overwrite: true);
        if (_req.DebugBuild)
            Info($"The debug build writes every Steam call to {Path.Combine(Path.GetDirectoryName(dllPath)!, "STEAM_LOG.txt")} while the game runs.");

        return Finish(partial, steamSettingsDir);
    }

    private static async Task WriteFileAsync(SetupRecord record, string path, string text, CancellationToken ct)
    {
        record.BeforeWrite(path);
        await File.WriteAllTextAsync(path, text, ct);
    }

    /// <summary>Runs GBE's interface tool on <paramref name="dll"/>; false, with a warning, when it writes nothing.</summary>
    private async Task<bool> GenerateInterfacesAsync(GbeRelease gbe, string dll, string steamSettingsDir, CancellationToken ct)
    {
        try
        {
            Step("Generating steam_interfaces.txt...");
            if (await InterfaceGenerator.GenerateAsync(gbe.GenerateInterfacesExe64, dll, steamSettingsDir, ct))
                return true;
            Warn($"generate_interfaces found no interfaces in {Path.GetFileName(dll)}.");
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException)
        {
            Warn($"Interface generation failed: {ex.Message}");
        }
        return false;
    }

    private ConfigStatus Finish(bool partial, string steamSettingsDir)
    {
        if (partial)
        {
            Info("Done with warnings — see messages above.");
            return ConfigStatus.PartialSuccess;
        }

        Info($"Done. steam_settings/ written to '{steamSettingsDir}'.");
        return ConfigStatus.Success;
    }

    /// <summary>True if the exception is Windows Defender blocking a file (ERROR_VIRUS_INFECTED /
    /// ERROR_VIRUS_DELETED), which GBE downloads commonly trigger as a false positive.</summary>
    private static bool IsAntivirusBlock(Exception ex)
    {
        const int errorVirusInfected = unchecked((int)0x800700E1);
        const int errorVirusDeleted = unchecked((int)0x800700E2);
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e.HResult is errorVirusInfected or errorVirusDeleted)
                return true;
            if (e.Message.Contains("virus", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("potentially unwanted", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool ReportDrm(DrmReport drm)
    {
        if (drm.LargestExePath != null)
            Debug($"DRM size check: largest exe is {drm.LargestExePath} ({drm.LargestExeSizeBytes / (1024.0 * 1024):0.#} MB; Denuvo is suspected above {DrmDetector.SuspiciousExeSizeBytes / (1024 * 1024)} MB)");

        if (!drm.DenuvoLikely)
            return true;

        if (drm.DenuvoStringFound)
            Warn("Denuvo string found in a game executable.");
        else if (drm.SizeSuspicious)
            Warn("Executable is unusually large — Denuvo is possible (no embedded string found).");

        if (drm.HasCrackIndicator)
        {
            Warn($"Crack indicators present ({AppUtilities.CountOf(drm.CrackIndicators.Count, "file")}); proceeding.");
            return true;
        }

        if (drm.DenuvoStringFound)
        {
            Error("Denuvo detected with no crack indicator — the game won't load steam_api64.dll. Aborting.");
            return false;
        }

        Warn("No crack indicator found; if the game ignores steam_api64.dll this won't work.");
        return true;
    }

    /// <summary>Joins names for a log line, capping the list so one bad game can't flood the log.</summary>
    private static string Summarize(IReadOnlyList<string> names)
    {
        const int cap = 10;
        var shown = string.Join(", ", names.Take(cap));
        return names.Count <= cap ? shown : $"{shown} (+{names.Count - cap} more)";
    }

    private void Debug(string m) => _progress.Report(ConfigLogLevel.Debug, m);
    private void Info(string m) => _progress.Report(ConfigLogLevel.Info, m);
    private void Step(string m) => _progress.Report(ConfigLogLevel.Step, m);
    private void Warn(string m) => _progress.Report(ConfigLogLevel.Warning, m);
    private void Error(string m) => _progress.Report(ConfigLogLevel.Error, m);
}
