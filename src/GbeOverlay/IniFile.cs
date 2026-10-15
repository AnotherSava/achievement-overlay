namespace AchievementOverlay.GbeOverlay;

/// <summary>
/// The subset of INI parsing GBE's own reader does, so a file written for the emulator means the same
/// thing here. GBE uses SimpleIni configured without quote parsing and without multi-key support,
/// which is a narrower dialect than most INI libraries implement: comments are whole lines only, a
/// value runs to the end of its line, and a repeated key overwrites rather than accumulating.
/// </summary>
/// <remarks>
/// <para>
/// Shared beyond GBE's overlay files: <c>AchievementMetadata.ParseIniUnlockStates</c> reads a CODEX/RUNE
/// <c>achievements.ini</c> through it, and <c>SceneEmulatorDetector</c> reads the emulator's <c>steam_emu.ini</c>. Both rely on
/// section names and keys being case-insensitive, so a change made for GBE fidelity — matching
/// SimpleIni's case-sensitive section names, say — changes how those files are read too.
/// </para>
/// <para>
/// One deliberate divergence from GBE. It compares <c>[overlay::appearance]</c> keys against their
/// stored spelling, which makes them case-sensitive — <c>font_size=22</c> is silently discarded. Keys
/// here are case-insensitive everywhere. For a game this app configured, nothing reads the overlay
/// file at all (the wizard installs the regular GBE build, which has no overlay code, or keeps a
/// CODEX/RUNE emulator, which never reads it), so the file is a statement of what the user wants
/// rather than a live mirror of anything, and accepting more of it is the friendlier failure.
/// </para>
/// </remarks>
public sealed class IniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections;

    public static IniFile Empty { get; } = new(new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase));

    private IniFile(Dictionary<string, Dictionary<string, string>> sections)
    {
        _sections = sections;
    }

    public static IniFile Parse(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = "";

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;

            if (line[0] == '[')
            {
                var close = line.IndexOf(']');
                if (close > 0)
                    current = line[1..close].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue; // a key with no value is not a setting

            var key = line[..separator].Trim();
            // The value runs to the end of the line: GBE strips no quotes and honours no trailing
            // comment, so 'Font_Size=16 # big' really is the string "16 # big" over there too.
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
                continue; // an empty value reads as absent, as it does in GBE

            if (!sections.TryGetValue(current, out var entries))
                sections[current] = entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            entries[key] = value; // repeated inside one file: last wins
        }

        return new IniFile(sections);
    }

    public string? Get(string section, string key) =>
        _sections.TryGetValue(section, out var entries) && entries.TryGetValue(key, out var value) ? value : null;

    /// <summary>The file's section names in the order they first appear, the unnamed one before any header included.</summary>
    public IEnumerable<string> SectionNames => _sections.Keys;

    /// <summary>Every key and value of <paramref name="section"/>, empty when the file has no such section.</summary>
    public IReadOnlyDictionary<string, string> Entries(string section) =>
        _sections.TryGetValue(section, out var entries) ? entries : new Dictionary<string, string>();

    /// <summary>
    /// Folds a weaker file underneath this one, keeping every key this file already defines. GBE
    /// merges its four config filenames into a single key space in a fixed order and adds a key only
    /// if it is not there yet, so the first file to define a key is the one that counts.
    /// </summary>
    public IniFile WithFallback(IniFile weaker)
    {
        var merged = new Dictionary<string, Dictionary<string, string>>(_sections, StringComparer.OrdinalIgnoreCase);

        foreach (var (section, entries) in weaker._sections)
        {
            if (!merged.TryGetValue(section, out var existing))
            {
                merged[section] = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
                continue;
            }

            existing = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in entries)
                existing.TryAdd(key, value);
            merged[section] = existing;
        }

        return new IniFile(merged);
    }
}
