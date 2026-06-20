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

        // --- Locate the Steam DLL (defines the target directory) ---
        var dlls = DllLocator.FindAll(gameDir);
        var primary = DllLocator.SelectPrimary(dlls);
        if (primary == null)
        {
            Error("No steam_api64.dll / steam_api.dll found under the game directory.");
            return ConfigStatus.UserError;
        }
        var targetDir = Path.GetDirectoryName(primary.Path)!;
        Info($"Steam DLL: {primary.Path}");
        if (dlls.Count > 1)
        {
            Warn("Multiple Steam DLLs found; using the one above. Others:");
            foreach (var d in dlls.Skip(1))
                Warn($"  {d.Path}");
        }

        // --- Resolve AppID ---
        var gameName = Path.GetFileName(gameDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var appId = await ResolveAppIdAsync(gameDir, gameName, ct);
        if (appId == null)
        {
            Error("Could not resolve AppID. Enter it explicitly in the AppID field.");
            return ConfigStatus.UserError;
        }
        Info($"AppID: {appId}");

        // --- DRM detection ---
        var drm = DrmDetector.Analyze(gameDir);
        if (!ReportDrm(drm))
            return ConfigStatus.UserError;

        var steamSettingsDir = Path.Combine(targetDir, "steam_settings");
        if (Directory.Exists(steamSettingsDir) && !_req.Force)
        {
            Error($"steam_settings/ already exists at '{steamSettingsDir}'. Enable 'Overwrite existing config' to replace it.");
            return ConfigStatus.UserError;
        }

        // --- GBE binaries ---
        GbeRelease gbe;
        try
        {
            gbe = await GbeBinaryManager.EnsureAsync(_req.GbeFolder, _req.DownloadLatest, _http, _progress, ct);
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

        Directory.CreateDirectory(steamSettingsDir);
        var partial = false;

        // --- Build achievements.json + apply hidden descriptions ---
        var achievements = AchievementSchemaWriter.Build(schema);
        var hiddenCount = schema.Count(a => a.Hidden != 0);
        if (hiddenCount > 0)
        {
            Step($"Fetching {hiddenCount} hidden description(s) from SteamDB...");
            var descs = await SteamDbScraper.FetchHiddenDescriptionsAsync(appId, _req.FirecrawlApiKey, _http, ct);
            if (descs == null)
            {
                _progress.FailStep("Couldn't fetch hidden descriptions from SteamDB (no Firecrawl API key, or the scrape "
                    + "failed). Hidden achievements are left with placeholder descriptions.");
                partial = true;
            }
            else
            {
                var patched = AchievementSchemaWriter.ApplyDescriptions(achievements, descs);
                Info($"Patched {patched}/{hiddenCount} hidden description(s).");
                if (patched < hiddenCount)
                    partial = true;
            }
        }

        // --- Download icons ---
        Step("Downloading achievement icons...");
        var imageDir = Path.Combine(steamSettingsDir, AchievementSchemaWriter.ImageDir);
        var iconResult = await IconDownloader.DownloadAllAsync(schema, imageDir, _http, overwrite: _req.Force, ct: ct);
        Info($"Icons: {iconResult.Downloaded} downloaded, {iconResult.Skipped} skipped, {iconResult.Failed} failed.");
        if (iconResult.Failed > 0)
            partial = true;

        // --- Write config files ---
        await File.WriteAllTextAsync(Path.Combine(steamSettingsDir, "achievements.json"),
            AchievementSchemaWriter.Serialize(achievements), ct);
        await File.WriteAllTextAsync(Path.Combine(steamSettingsDir, "steam_appid.txt"), appId, ct);
        await File.WriteAllTextAsync(Path.Combine(steamSettingsDir, "configs.overlay.ini"), OverlayConfig, ct);
        Info("Wrote achievements.json, steam_appid.txt, configs.overlay.ini");

        // --- Back up original DLL and generate interfaces against it ---
        var interfaceSourceDll = primary.Path;
        if (_req.Backup)
        {
            var backupPath = primary.Path + ".original";
            if (!File.Exists(backupPath))
            {
                File.Copy(primary.Path, backupPath);
                Info($"Backed up original DLL to {Path.GetFileName(backupPath)}");
            }
            else
            {
                Info($"Backup {Path.GetFileName(backupPath)} already exists; keeping it as the original.");
            }
            interfaceSourceDll = backupPath;
        }

        try
        {
            Step("Generating steam_interfaces.txt...");
            var ok = await InterfaceGenerator.GenerateAsync(gbe.GenerateInterfacesExe64, interfaceSourceDll, steamSettingsDir, ct);
            if (!ok)
            {
                Warn("generate_interfaces produced no output; steam_interfaces.txt was not written.");
                partial = true;
            }
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException)
        {
            Warn($"Interface generation failed: {ex.Message}");
            partial = true;
        }

        // --- Replace the DLL with GBE's regular build ---
        Step("Installing GBE steam_api64.dll...");
        File.Copy(gbe.RegularDll64, primary.Path, overwrite: true);

        if (partial)
        {
            Info("Done with warnings — see messages above.");
            return ConfigStatus.PartialSuccess;
        }

        Info($"Done. steam_settings/ written to '{steamSettingsDir}'.");
        return ConfigStatus.Success;
    }

    private async Task<string?> ResolveAppIdAsync(string gameDir, string gameName, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_req.AppId))
            return _req.AppId.Trim();

        var fromTxt = AppIdResolver.FromAppIdTxt(gameDir);
        if (fromTxt != null)
        {
            Debug("AppID resolved from steam_appid.txt");
            return fromTxt;
        }

        var fromIni = AppIdResolver.FromIniFiles(gameDir);
        if (fromIni != null)
        {
            Debug("AppID resolved from a local .ini/.cfg file");
            return fromIni;
        }

        Debug($"Searching the Steam store for '{gameName}'...");
        var fromStore = await AppIdResolver.FromStoreSearchAsync(gameName, _http, ct);
        if (fromStore != null)
            Warn($"AppID {fromStore} guessed from store search for '{gameName}' — verify it's correct.");
        return fromStore;
    }

    /// <summary>True if the exception is Windows Defender blocking a file (ERROR_VIRUS_INFECTED /
    /// ERROR_VIRUS_DELETED), which GBE downloads commonly trigger as a false positive.</summary>
    private static bool IsAntivirusBlock(Exception ex)
    {
        const int errorVirusInfected = unchecked((int)0x800700E1);
        const int errorVirusDeleted = unchecked((int)0x800700E2);
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e.HResult == errorVirusInfected || e.HResult == errorVirusDeleted)
                return true;
            if (e.Message.Contains("virus", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("potentially unwanted", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool ReportDrm(DrmReport drm)
    {
        if (drm.LargestExePath != null)
            Debug($"Largest exe: {drm.LargestExePath} ({drm.LargestExeSizeBytes / (1024 * 1024)} MB)");

        if (!drm.DenuvoLikely)
            return true;

        if (drm.DenuvoStringFound)
            Warn("Denuvo string found in a game executable.");
        else if (drm.SizeSuspicious)
            Warn("Executable is unusually large — Denuvo is possible (no embedded string found).");

        if (drm.HasCrackIndicator)
        {
            Warn($"Crack indicators present ({drm.CrackIndicators.Count} file(s)); proceeding.");
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

    private void Debug(string m) => _progress.Report(ConfigLogLevel.Debug, m);
    private void Info(string m) => _progress.Report(ConfigLogLevel.Info, m);
    private void Step(string m) => _progress.Report(ConfigLogLevel.Step, m);
    private void Warn(string m) => _progress.Report(ConfigLogLevel.Warning, m);
    private void Error(string m) => _progress.Report(ConfigLogLevel.Error, m);
}
