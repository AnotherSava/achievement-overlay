using System.IO;

namespace AchievementOverlay;

/// <summary>
/// The file an emulator records unlocks in, inside an appid folder of a save path. Two formats share
/// the layout <c>&lt;save path&gt;\&lt;appid&gt;\&lt;file&gt;</c>, told apart by name: GBE and the
/// Uplay R2 emulator write <c>achievements.json</c>, CODEX and RUNE write <c>achievements.ini</c>.
/// Every reader of an unlock file goes through here, so none of them names a format of its own.
/// </summary>
public static class UnlockFile
{
    public const string JsonName = "achievements.json";
    public const string IniName = "achievements.ini";

    /// <summary>Both names, in the order <see cref="Find"/> prefers them.</summary>
    public static IReadOnlyList<string> Names { get; } = [JsonName, IniName];

    /// <summary>The unlock file in <paramref name="appIdFolder"/>, or null when it has none.</summary>
    public static string? Find(string appIdFolder) =>
        Names.Select(name => Path.Combine(appIdFolder, name)).FirstOrDefault(File.Exists);

    /// <summary>
    /// Parses an unlock file's text by the format its <paramref name="path"/> names. Throws
    /// <see cref="System.Text.Json.JsonException"/> on a malformed JSON document, as
    /// <see cref="AchievementMetadata.ParseUnlockStates"/> does; an INI file never throws.
    /// </summary>
    public static Dictionary<string, AchievementUnlockState> Parse(string path, string text) =>
        IsIni(path) ? AchievementMetadata.ParseIniUnlockStates(text) : AchievementMetadata.ParseUnlockStates(text);

    /// <summary>Reads and parses the unlock file at <paramref name="path"/>.</summary>
    public static Dictionary<string, AchievementUnlockState> Read(string path) => Parse(path, File.ReadAllText(path));

    /// <summary>
    /// The first readable unlock file for <paramref name="appId"/> across <paramref name="savesPaths"/>, and its
    /// states; both null when none exists or none could be read, <c>Unreadable</c> telling the two apart.
    /// </summary>
    public static (string? File, Dictionary<string, AchievementUnlockState>? States, bool Unreadable) ReadFirst(string appId, IEnumerable<string> savesPaths)
    {
        var unreadable = false;
        foreach (var savesPath in savesPaths)
        {
            if (Find(Path.Combine(savesPath, appId)) is not { } file)
                continue;
            try
            {
                return (file, Read(file), false);
            }
#pragma warning disable CA1031 // Per-path boundary: logs the failure at Warn and reports the file as unreadable
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Logger.Warn($"Could not read achievements for appid {appId}: {ex.Message}");
                unreadable = true;
            }
        }

        return (null, null, unreadable);
    }

    public static bool IsIni(string path) => Path.GetFileName(path).Equals(IniName, StringComparison.OrdinalIgnoreCase);
}
