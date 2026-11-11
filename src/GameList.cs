using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using AchievementOverlay.GbeConfig;

namespace AchievementOverlay;

/// <summary>One row of the Games window: what it shows, and whether the game can be reverted.</summary>
public sealed class GameRow
{
    public required GameChoice Choice { get; init; }
    public required string Name { get; init; }
    public string AppId => Choice.AppId;
    public required string Emulator { get; init; }
    public required string Achievements { get; init; }

    /// <summary>"3h ago", or empty when nothing is earned yet.</summary>
    public required string LastUnlock { get; init; }

    /// <summary>The same moment as a full local timestamp, for the cell's tooltip.</summary>
    public string? LastUnlockExact { get; init; }

    /// <summary>The same moment in Unix seconds, 0 when nothing is earned, for sorting the column.</summary>
    public long LastUnlockTime { get; init; }

    /// <summary>
    /// The game's install folder. For a game never set up here, the folder under the games paths that names its
    /// AppID, if any does; empty otherwise.
    /// </summary>
    public required string GameFolder { get; init; }

    /// <summary>The folder the game's unlock file is in; empty while it has none.</summary>
    public required string SaveFolder { get; init; }

    /// <summary>The <c>steam_settings</c> folder a revert works on; null for a game never set up here.</summary>
    public string? SettingsDir { get; init; }

    /// <summary>Why the game cannot be reverted, or null when it can.</summary>
    public string? RevertBlocker { get; init; }

    /// <summary>What keeps the game's config from working fully, one sentence each; null for a game not set up.</summary>
    public IReadOnlyList<string>? ConfigProblems { get; init; }

    public bool ConfigComplete => ConfigProblems is { Count: 0 };

    public bool ConfigIncomplete => ConfigProblems is { Count: > 0 };

    public string? ConfigTooltip => ConfigProblems switch
    {
        null => null,
        { Count: 0 } => "Set up completely: every achievement has its text and icon, and its unlocks are watched.",
        var problems => string.Join("\n", problems)
    };

    /// <summary>
    /// The row's button: Set up… for a game never set up here, which can always be pointed at a folder, and
    /// Revert… for the rest, which is available only when there is something to undo.
    /// </summary>
    public string ActionLabel => Choice.Unconfigured ? "Set up…" : "Revert…";

    public bool ActionEnabled => Choice.Unconfigured || RevertBlocker == null;

    public string? ActionTooltip => Choice.Unconfigured
        ? GameFolder.Length > 0 ? $"Open Add game for {GameFolder}." : "Open Add game, then pick where the game is installed."
        : RevertBlocker;
}

/// <summary>
/// Builds the Games window's rows. Reads every game's Steam DLL and unlock file, so it runs off the UI
/// thread. The emulator is read from the game folder rather than remembered from setup: a game can be
/// switched between emulators by hand, as the one this column was written for was.
/// </summary>
public static class GameList
{
    /// <summary>
    /// The string only GBE's debug build carries, the stem of the <c>STEAM_LOG.txt</c> it writes. Measured on
    /// release-2026_09_27: once in the debug DLL, absent from the release DLL.
    /// </summary>
    private static readonly byte[] DebugMarker = Encoding.ASCII.GetBytes("STEAM_LOG");

    /// <summary>
    /// Emulator labels by DLL folder, kept while the DLLs in it are unchanged. Naming one means reading the
    /// whole DLL, tens of megabytes for GBE, so without this every open of the window re-reads them all.
    /// </summary>
    private static readonly ConcurrentDictionary<string, (string Stamp, string Label)> EmulatorCache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="installFolders">Install folders found for games never set up here, by AppID, from <see cref="FindInstallFolders"/>.</param>
    public static GameRow Describe(GameChoice choice, IReadOnlyList<string> gseSavesPaths, IReadOnlyDictionary<string, string> installFolders, DateTimeOffset now)
    {
        var saveFolder = gseSavesPaths.Select(path => Path.Combine(path, choice.AppId)).FirstOrDefault(Directory.Exists);
        var (unlockFile, states, _) = UnlockFile.ReadFirst(choice.AppId, gseSavesPaths);
        var earned = states?.Values.Where(state => state.Earned).ToList() ?? [];
        // Only an achievement list knows the total: an unlock file may hold just the achievements touched so far.
        var total = choice.Game != null ? GameCache.LoadDefinitions(choice.Game)?.Count : null;
        var last = earned.Count > 0 ? earned.Max(state => state.EarnedTime) : 0;
        var lastTime = last > 0 ? DateTimeOffset.FromUnixTimeSeconds(last) : (DateTimeOffset?)null;

        string gameFolder;
        string emulator;
        string? settingsDir = null;
        string? blocker;
        IReadOnlyList<string>? problems = null;
        if (choice.Game is { } game)
        {
            settingsDir = game.SettingsDirs.FirstOrDefault(dir => File.Exists(Path.Combine(dir, SetupRecord.FileName))) ?? game.SettingsDirs[0];
            gameFolder = game.GameDir ?? Path.GetDirectoryName(game.SettingsDirs[0])!;
            emulator = ReadEmulatorInFolder(Path.GetDirectoryName(game.SettingsDirs[0])!);
            blocker = GameReverter.CannotRevert(settingsDir);
            problems = ConfigProblemsOf(game, emulator, gseSavesPaths);
        }
        else
        {
            gameFolder = installFolders.GetValueOrDefault(choice.AppId, "");
            emulator = saveFolder != null ? EmulatorOfSaveFolder(saveFolder, unlockFile, states) : "Unknown";
            blocker = "This game was not set up with Add game.";
        }

        return new GameRow
        {
            Choice = choice,
            Name = string.IsNullOrWhiteSpace(choice.Name) ? choice.AppId : choice.Name,
            Emulator = emulator,
            Achievements = AchievementsText(earned.Count, total, states != null),
            LastUnlock = lastTime is { } time ? TimeFormat.Interval(now - time) + " ago" : "",
            LastUnlockExact = lastTime?.LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
            LastUnlockTime = last,
            GameFolder = gameFolder,
            SaveFolder = unlockFile != null ? Path.GetDirectoryName(unlockFile)! : saveFolder ?? "",
            SettingsDir = settingsDir,
            RevertBlocker = blocker,
            ConfigProblems = problems
        };
    }

    private static string ReadEmulatorInFolder(string dllDir)
    {
        try
        {
            var stamp = DllStamp(dllDir);
            if (EmulatorCache.TryGetValue(dllDir, out var cached) && cached.Stamp == stamp)
                return cached.Label;
            var label = EmulatorInFolder(dllDir);
            EmulatorCache[dllDir] = (stamp, label);
            return label;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn($"Games window could not read the Steam DLL in '{dllDir}': {ex.Message}");
            return "Unreadable";
        }
    }

    /// <summary>Size and write time of every file <see cref="EmulatorInFolder"/> reads, so a swapped DLL is noticed.</summary>
    private static string DllStamp(string dllDir) =>
        string.Join("|", SteamDllDetector.SteamClientNames.Concat(SteamDllDetector.SteamApiNames).Append(SceneEmulatorDetector.EmuIni).Select(name => new FileInfo(Path.Combine(dllDir, name)))
            .Select(file => file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : "-"));

    /// <summary>
    /// What keeps a configured game from working fully: an achievement list no emulator reads, achievements with no
    /// description or icon, or unlocks written where the overlay is not watching. All of it is read from disk, so it
    /// runs offline; whether the list is still Steam's current one is not checked.
    /// </summary>
    public static List<string> ConfigProblemsOf(GameInfo game, string emulator, IReadOnlyList<string> gseSavesPaths)
    {
        var problems = new List<string>();
        var settingsDir = game.SettingsDirs[0];
        var dllDir = Path.GetDirectoryName(settingsDir)!;

        if (game.SteamDll?.Loaded is { } loaded && !FolderPath.Parse(loaded.SettingsDir).IsSameFolder(FolderPath.Parse(settingsDir)))
            problems.Add($"The game loads {loaded.Path}, and no achievement list sits beside it.");
        else if (!SteamDllDetector.SteamApiNames.Concat(SteamDllDetector.SteamClientNames).Any(name => File.Exists(Path.Combine(dllDir, name))))
            problems.Add("No Steam library sits beside the achievement list, so no emulator reads it.");

        var definitions = GameCache.LoadDefinitions(game);
        if (definitions == null)
        {
            problems.Add("The achievement list cannot be read.");
        }
        else
        {
            var noDescription = definitions.Count(definition => AchievementMetadata.GetDisplayText(definition.Description, "english").Trim() is "" or "No description available");
            if (noDescription > 0)
                problems.Add($"{AppUtilities.CountOf(noDescription, "achievement")} without a description, usually hidden ones.");
            var noIcon = definitions.Count(definition => AchievementMetadata.ResolveIconPath(definition, settingsDir) is not { } icon || !File.Exists(icon));
            if (noIcon > 0)
                problems.Add($"{AppUtilities.CountOf(noIcon, "achievement")} without an icon.");
        }

        if (emulator == "Steam")
            problems.Add("The game runs Steam's own library, so no emulator records its unlocks.");
        else if (DefaultSavesFolder(emulator) is { } saves && !UsesLocalSaves(game) && !gseSavesPaths.Any(path => FolderPath.Parse(path).IsSameFolder(FolderPath.Parse(saves))))
            problems.Add($"{emulator} records unlocks in {saves}, which is not one of the save folders in Settings.");

        return problems;
    }

    /// <summary>Where an emulator records unlocks unless told otherwise; null for one this cannot say.</summary>
    private static string? DefaultSavesFolder(string emulator) => emulator switch
    {
        "RUNE" => AppConfig.ExpandEnvironmentVariables(SceneEmulatorDetector.RuneSaves),
        "CODEX" => AppConfig.ExpandEnvironmentVariables(SceneEmulatorDetector.CodexSaves),
        "GBE" or "GBE debug" or "GBE (loader)" => AppConfig.ExpandEnvironmentVariables(@"%appdata%\GSE Saves"),
        _ => null
    };

    /// <summary>Whether a <c>configs.user.ini</c> moves GBE's saves next to the game, which this cannot follow.</summary>
    private static bool UsesLocalSaves(GameInfo game) =>
        game.SettingsDirs.Select(dir => Path.Combine(dir, "configs.user.ini")).Where(File.Exists)
            .Any(ini => GbeOverlay.IniFile.Parse(File.ReadAllText(ini)).Get("user::saves", "local_save_path") is { Length: > 0 });

    /// <summary>
    /// Earned out of the total when the achievement list gives one, earned before it is anything written; the
    /// earned count over "?" when only an unlock file says anything; empty when nothing does.
    /// </summary>
    public static string AchievementsText(int earned, int? total, bool hasUnlockFile) =>
        total is { } count ? $"{earned} / {count}" : hasUnlockFile ? $"{earned} / ?" : "";

    /// <summary>
    /// Which emulator the Steam DLL in <paramref name="dllDir"/> is: GBE behind a loader, GBE or its debug
    /// build, CODEX/RUNE, or Steam's own when it is none of those.
    /// </summary>
    public static string EmulatorInFolder(string dllDir)
    {
        if (SteamDllDetector.SteamClientNames.Select(name => Path.Combine(dllDir, name)).Any(steamclient => File.Exists(steamclient) && SceneEmulatorDetector.IsGbe(File.ReadAllBytes(steamclient))))
            return "GBE (loader)";

        foreach (var dll in SteamDllDetector.SteamApiNames.Select(name => Path.Combine(dllDir, name)).Where(File.Exists))
        {
            var bytes = File.ReadAllBytes(dll);
            if (SceneEmulatorDetector.IsGbe(bytes))
                return bytes.AsSpan().IndexOf(DebugMarker) >= 0 ? "GBE debug" : "GBE";
            return SceneEmulatorDetector.Detect(dll)?.Name ?? "Steam";
        }

        return "Unknown";
    }

    /// <summary>
    /// Which emulator owns a game's save folder, for a game with no install folder to read a DLL from. CODEX and
    /// RUNE each save under a folder named after themselves and write INI; a JSON file naming its own
    /// achievements is not GBE's; any other folder, file or not yet, is GBE's, the one emulator that creates its
    /// appid folder before writing anything into it.
    /// </summary>
    public static string EmulatorOfSaveFolder(string appIdFolder, string? unlockFile, IReadOnlyDictionary<string, AchievementUnlockState>? states)
    {
        var emulatorFolder = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(appIdFolder)));
        if (emulatorFolder is { } name && (name.Equals("RUNE", StringComparison.OrdinalIgnoreCase) || name.Equals("CODEX", StringComparison.OrdinalIgnoreCase)))
            return name.ToUpperInvariant();
        if (unlockFile != null && UnlockFile.IsIni(unlockFile))
            return "CODEX/RUNE";
        return states != null && AchievementMetadata.IsSelfDescribing(states) ? "Other emulator" : "GBE";
    }

    /// <summary>
    /// Every AppID named under <paramref name="gamesPaths"/>, with its install folder: the game folder of a
    /// <c>steam_appid.txt</c> or a <c>steam_emu.ini</c> naming it, decided by <see cref="GameCache.NameGame"/> over
    /// the walk the cache makes (<see cref="GameCache.PlanScan"/>, <see cref="GameCache.FindAppIdFolders"/>). The game
    /// cache keeps only games with an <c>achievements.json</c>, so this is how a game never set up here is found at
    /// all, and where one known from its save folder is installed.
    /// </summary>
    public static Dictionary<string, string> FindInstallFolders(IReadOnlyList<string> gamesPaths)
    {
        var (_, roots, scanned) = GameCache.PlanScan(gamesPaths);
        var appIdFolders = new List<(string AppId, string GameDir, FolderPath Folder)>();
        var emuFolders = new List<(string AppId, FolderPath Folder)>();
        foreach (var (root, _) in scanned.Where(entry => Directory.Exists(entry.Path)))
        {
            appIdFolders.AddRange(GameCache.FindAppIdFolders(root, skipped: []));
            foreach (var file in Directory.EnumerateFiles(root, SceneEmulatorDetector.EmuIni, AppUtilities.RecursiveScan))
            {
                if (ReadEmuIniAppId(file) is { } appId)
                    emuFolders.Add((appId, FolderPath.Parse(Path.GetDirectoryName(file)!)));
            }
        }

        var foldersByAppId = GameCache.ByAppId(appIdFolders);
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (appId, folder) in appIdFolders.Select(entry => (entry.AppId, entry.Folder)).Concat(emuFolders))
            found.TryAdd(appId, GameCache.NameGame(folder, roots, foldersByAppId[appId]).Folder);
        return found;
    }

    /// <summary>
    /// <paramref name="games"/> plus a not-set-up entry for every AppID <paramref name="installFolders"/> names that
    /// none of them has, named after its folder; configured games first, then the rest, each group by name.
    /// </summary>
    public static List<GameChoice> WithInstalledGames(IReadOnlyList<GameChoice> games, IReadOnlyDictionary<string, string> installFolders)
    {
        var listed = games.Select(game => game.AppId).ToHashSet(StringComparer.Ordinal);
        var all = games.Concat(installFolders.Where(entry => !listed.Contains(entry.Key))
            .Select(entry => new GameChoice { AppId = entry.Key, Unconfigured = true, Name = Path.GetFileName(entry.Value) })).ToList();
        all.Sort(GameChoice.ListOrder);
        return all;
    }

    private static string? ReadEmuIniAppId(string file)
    {
        try
        {
            return SceneEmulatorDetector.ReadAppId(GbeOverlay.IniFile.Parse(File.ReadAllText(file)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
