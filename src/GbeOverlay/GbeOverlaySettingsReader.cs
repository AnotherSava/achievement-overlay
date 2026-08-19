using System.Collections.Concurrent;
using System.IO;

namespace AchievementOverlay.GbeOverlay;

/// <summary>
/// Reads a game's own GBE overlay settings out of its <c>steam_settings/</c> folders — plural,
/// because one install commonly has several. The only place that touches disk for this feature, and
/// the only cache.
/// </summary>
/// <remarks>
/// Not part of <see cref="GameCache"/>: its entries are replaced wholesale on every <c>ScanAll</c>,
/// so parsed state hung off it would be thrown away every time the games paths change or a game is
/// added. Separate lifetimes, separate owner.
/// <para>
/// There is no global <c>GSE Saves/settings/</c> layer. GBE has one and fills unset keys from it, but
/// the request was for a game's own settings, and honouring the global folder would also mean
/// honouring GBE's <c>local_save_path</c> rule, which inverts the precedence for exactly the games
/// that set it.
/// </para>
/// </remarks>
public sealed class GbeOverlaySettingsReader
{
    /// <summary>
    /// All four are read, in this order, with the first definition of a key winning — GBE merges them
    /// into a single key space where the section decides meaning, not the filename, so
    /// <c>[overlay::appearance]</c> in <c>configs.app.ini</c> counts just as much.
    /// </summary>
    private static readonly string[] ConfigFileNames =
    {
        "configs.app.ini", "configs.main.ini", "configs.overlay.ini", "configs.user.ini"
    };

    private const string SoundsFolder = "sounds";
    private const string FontsFolder = "fonts";
    private const string UnlockSoundFileName = "overlay_achievement_notification.wav";

    private sealed record CacheEntry(DateTime[] Stamps, GameOverlaySettings? Settings);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The game's settings, or null when its folders say nothing this app can use. Never throws: this
    /// runs on the dispatcher just before a popup is built, and a disconnected drive must cost the
    /// override, not the notification.
    /// </summary>
    /// <param name="steamSettingsDirs">
    /// Every <c>steam_settings</c> folder the game has, strongest first — see
    /// <see cref="GameInfo.SettingsDirs"/>. Folded rather than picked: the folder the emulator
    /// actually reads is frequently the one <em>without</em> the sound and font, because a repack
    /// decorates the copy at the game root and leaves the working copy bare.
    /// </param>
    public GameOverlaySettings? Read(IReadOnlyList<string>? steamSettingsDirs)
    {
        if (steamSettingsDirs == null || steamSettingsDirs.Count == 0)
            return null;

        var key = string.Join('|', steamSettingsDirs);
        try
        {
            var stamps = Stamp(steamSettingsDirs);
            if (_cache.TryGetValue(key, out var cached) && cached.Stamps.AsSpan().SequenceEqual(stamps))
                return cached.Settings;

            var settings = Load(steamSettingsDirs);
            _cache[key] = new CacheEntry(stamps, settings);
            if (settings != null)
                Logger.Info($"Game overlay settings: {settings}");
            return settings;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not read game overlay settings from '{key}': {ex.Message}");
            return _cache.TryGetValue(key, out var previous) ? previous.Settings : null;
        }
    }

    /// <summary>
    /// The timestamps a re-parse depends on: the four config files, plus the two asset folders — a
    /// folder's own timestamp moves when a file is added or removed inside it, so a wav or a ttf
    /// appearing later is noticed without an ini edit. A path that does not exist stamps as
    /// 1601-01-01 rather than throwing, which is exactly the "absent" state we want to compare.
    /// </summary>
    private static DateTime[] Stamp(IReadOnlyList<string> steamSettingsDirs)
    {
        var perDir = ConfigFileNames.Length + 2;
        var stamps = new DateTime[steamSettingsDirs.Count * perDir];
        for (var d = 0; d < steamSettingsDirs.Count; d++)
        {
            var dir = steamSettingsDirs[d];
            var at = d * perDir;
            for (var i = 0; i < ConfigFileNames.Length; i++)
                stamps[at + i] = File.GetLastWriteTimeUtc(Path.Combine(dir, ConfigFileNames[i]));
            stamps[at + ConfigFileNames.Length] = Directory.GetLastWriteTimeUtc(Path.Combine(dir, SoundsFolder));
            stamps[at + ConfigFileNames.Length + 1] = Directory.GetLastWriteTimeUtc(Path.Combine(dir, FontsFolder));
        }
        return stamps;
    }

    private static GameOverlaySettings? Load(IReadOnlyList<string> steamSettingsDirs)
    {
        // One key space across every folder and every file name, first definition winning — the same
        // rule GBE applies across its four config files, extended to a game that has more than one
        // folder to hold them in.
        var merged = IniFile.Empty;
        foreach (var dir in steamSettingsDirs)
        {
            foreach (var fileName in ConfigFileNames)
            {
                var path = Path.Combine(dir, fileName);
                if (!File.Exists(path))
                    continue;

                try
                {
                    merged = merged.WithFallback(IniFile.Parse(File.ReadAllText(path)));
                }
                catch (Exception ex)
                {
                    // Per file, so one unreadable config does not discard the others' values.
                    Logger.Warn($"Could not read '{path}': {ex.Message}");
                }
            }
        }

        var config = GameOverlayConfig.Parse(merged);
        var settings = new GameOverlaySettings(
            config.AchievementDurationSeconds,
            FirstExisting(steamSettingsDirs, dir => Path.Combine(dir, SoundsFolder, UnlockSoundFileName)),
            ResolveFont(steamSettingsDirs, config.FontOverride),
            string.Join(" + ", steamSettingsDirs));

        return settings.IsEmpty ? null : settings;
    }

    /// <summary>
    /// GBE takes an absolute <c>Font_Override</c> as given and looks a relative one up inside
    /// <c>steam_settings/fonts</c>; a name that resolves to nothing is dropped rather than falling
    /// back to some other font. The name is tried against every folder because the file naming it and
    /// the folder holding it need not be the same one.
    /// </summary>
    private static string? ResolveFont(IReadOnlyList<string> steamSettingsDirs, string? fontOverride)
    {
        if (fontOverride == null)
            return null;

        var resolved = Path.IsPathRooted(fontOverride)
            ? ExistingFile(fontOverride)
            : FirstExisting(steamSettingsDirs, dir => Path.Combine(dir, FontsFolder, fontOverride));

        if (resolved == null)
            Logger.Warn($"Font_Override '{fontOverride}' does not resolve to a file under {string.Join(" or ", steamSettingsDirs)}; ignoring it.");
        return resolved;
    }

    /// <summary>The first folder that actually holds the asset, in the caller's order of preference.</summary>
    private static string? FirstExisting(IReadOnlyList<string> steamSettingsDirs, Func<string, string> candidate) =>
        steamSettingsDirs.Select(dir => ExistingFile(candidate(dir))).FirstOrDefault(path => path != null);

    /// <summary>Full path when it is a file, null otherwise — a directory at that name is not a file.</summary>
    private static string? ExistingFile(string path) => File.Exists(path) ? Path.GetFullPath(path) : null;
}
