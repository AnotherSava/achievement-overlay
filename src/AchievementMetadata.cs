using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AchievementOverlay;

/// <summary>
/// Represents the unlock state of a single achievement from GSE Saves achievements.json.
/// GBE writes {"ACH01": {"earned": true, "earned_time": 1774855788}, ...}; the Goldberg Uplay R2
/// emulator writes the same file with a numeric "earned" (0/1), no "earned_time" until the
/// achievement unlocks, and the display text inlined per entry — which makes its file
/// self-describing, so a notification can be built without any steam_settings/ schema.
/// </summary>
public sealed class AchievementUnlockState
{
    [JsonPropertyName("earned")]
    [JsonConverter(typeof(FlexibleBooleanConverter))]
    public bool Earned { get; set; }

    [JsonPropertyName("earned_time")]
    [JsonConverter(typeof(FlexibleInt64Converter))]
    public long EarnedTime { get; set; }

    /// <summary>Present only in self-describing files (Uplay); null for GBE's.</summary>
    [JsonPropertyName("displayName")]
    public JsonElement? DisplayName { get; set; }

    /// <summary>Present only in self-describing files (Uplay); null for GBE's.</summary>
    [JsonPropertyName("description")]
    public JsonElement? Description { get; set; }
}

/// <summary>
/// Reads a boolean written as a JSON bool, a number (0 = false), or a string ("true"/"1").
/// Applied per-property rather than through the shared options so it cannot affect any other
/// bool in the app. Null reads as false — a missing value must cost at most one notification,
/// never the whole file.
/// </summary>
internal sealed class FlexibleBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False:
            case JsonTokenType.Null: return false;
            case JsonTokenType.Number: return reader.TryGetInt64(out var number) ? number != 0 : reader.GetDouble() != 0;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (bool.TryParse(text, out var parsed)) return parsed;
                if (long.TryParse(text, out var numeric)) return numeric != 0;
                throw new JsonException($"Cannot convert string '{text}' to a boolean.");
            default:
                throw new JsonException($"Cannot convert token {reader.TokenType} to a boolean.");
        }
    }

    // Write a real boolean so a file we round-trip stays in GBE's canonical shape.
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}

/// <summary>
/// Reads an integer written as a JSON number or a quoted string. Null reads as 0.
/// Insurance against an emulator quoting earned_time the way it already quotes nothing else —
/// without it, one quoted value costs that achievement its notification.
/// </summary>
internal sealed class FlexibleInt64Converter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return 0;
            case JsonTokenType.Number: return reader.TryGetInt64(out var number) ? number : (long)reader.GetDouble();
            case JsonTokenType.String:
                var text = reader.GetString();
                if (long.TryParse(text, out var parsed)) return parsed;
                if (double.TryParse(text, out var asDouble)) return (long)asDouble;
                throw new JsonException($"Cannot convert string '{text}' to an integer.");
            default:
                throw new JsonException($"Cannot convert token {reader.TokenType} to an integer.");
        }
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

/// <summary>
/// Represents a single achievement entry from the game's steam_settings/achievements.json.
/// Display text fields (displayName, description) can be either a plain string or a
/// multi-language object like {"english": "...", "german": "..."}.
/// </summary>
public sealed class AchievementDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("displayName")]
    public JsonElement? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public JsonElement? Description { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}

public static class AchievementMetadata
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Parses the GSE Saves achievements.json (dict of name -> unlock state).
    /// Converts each entry separately so one unreadable value costs that achievement only: the
    /// file is written by an emulator we don't control, and three of the four call sites swallow
    /// a parse failure, so a whole-document throw means silence with no notification and no clue.
    /// A malformed document still throws, as callers rely on.
    /// </summary>
    public static Dictionary<string, AchievementUnlockState> ParseUnlockStates(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
        if (raw == null)
            return new Dictionary<string, AchievementUnlockState>();

        var states = new Dictionary<string, AchievementUnlockState>();
        var skipped = 0;
        string? firstError = null;

        foreach (var (name, element) in raw)
        {
            try
            {
                var state = element.Deserialize<AchievementUnlockState>(JsonOptions);
                if (state != null)
                    states[name] = state;
            }
            catch (JsonException ex)
            {
                skipped++;
                firstError ??= $"'{name}': {ex.Message}";
            }
        }

        // One line per parse, not per entry — a systematically bad field would otherwise flood the
        // log (and Logger auto-flushes, so every line is a synchronous disk write).
        if (skipped > 0)
            Logger.Warn($"Skipped {skipped}/{raw.Count} unreadable achievement entries (first: {firstError})");

        return states;
    }

    /// <summary>
    /// True when the unlock state carries its own display text, i.e. it came from a
    /// self-describing writer (Uplay) rather than GBE.
    /// </summary>
    public static bool HasInlineText(AchievementUnlockState? state)
        => state != null && (IsNonEmpty(state.DisplayName) || IsNonEmpty(state.Description));

    /// <summary>
    /// True when any entry in an unlock file carries its own display text. Used to decide whether
    /// a game with no steam_settings/ schema can still be tracked.
    /// </summary>
    public static bool IsSelfDescribing(IReadOnlyDictionary<string, AchievementUnlockState> states)
        => states.Values.Any(HasInlineText);

    /// <summary>
    /// True when the element holds usable text — a non-empty string, or a multi-language object
    /// with at least one non-empty value. Deliberately does not go through GetDisplayText, whose
    /// language-fallback warning would fire once per achievement.
    /// </summary>
    private static bool IsNonEmpty(JsonElement? element)
    {
        if (element == null)
            return false;

        return element.Value.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrEmpty(element.Value.GetString()),
            JsonValueKind.Object => element.Value.EnumerateObject().Any(
                p => p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(p.Value.GetString())),
            _ => false
        };
    }

    /// <summary>
    /// Parses the game's steam_settings/achievements.json (array of definitions).
    /// </summary>
    public static List<AchievementDefinition> ParseDefinitions(string json)
    {
        return JsonSerializer.Deserialize<List<AchievementDefinition>>(json, JsonOptions)
               ?? new List<AchievementDefinition>();
    }

    /// <summary>
    /// The language keys a game's achievement text is actually available in — the property names of
    /// its multi-language displayName/description objects. The settings dialog offers these instead
    /// of Steam's full language list, so the choice is limited to values that will resolve rather
    /// than fall back. A schema whose text is plain strings contributes nothing: it is
    /// single-language, and there is nothing to choose between.
    /// </summary>
    public static IReadOnlyCollection<string> CollectLanguages(IEnumerable<AchievementDefinition> definitions) =>
        CollectLanguages(definitions.Select(d => (d.DisplayName, d.Description)));

    /// <summary>
    /// The same, for a self-describing unlock file. Such a game has no schema at all, so the only
    /// record of which languages it can display is the unlock file itself — leaving it out would
    /// make those languages invisible to a user whose games are all tracked this way.
    /// </summary>
    public static IReadOnlyCollection<string> CollectLanguages(IEnumerable<AchievementUnlockState> states) =>
        CollectLanguages(states.Select(s => (s.DisplayName, s.Description)));

    private static IReadOnlyCollection<string> CollectLanguages(IEnumerable<(JsonElement? DisplayName, JsonElement? Description)> texts)
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (displayName, description) in texts)
        {
            AddObjectKeys(languages, displayName);
            AddObjectKeys(languages, description);
        }
        return languages;
    }

    /// <summary>
    /// Steam's localization token (e.g. "NEW_ACHIEVEMENT_1_0_NAME") sits in the same object as the
    /// real languages, but selecting it would show that raw string as the achievement's name.
    /// </summary>
    private const string TokenKey = "token";

    private static void AddObjectKeys(HashSet<string> into, JsonElement? element)
    {
        if (element?.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in element.Value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String
                && !property.NameEquals(TokenKey))
                into.Add(property.Name);
        }
    }

    /// <summary>
    /// Resolves display text from a JsonElement that may be a plain string or a
    /// multi-language object. Falls back to english, then first available value.
    /// </summary>
    public static string GetDisplayText(JsonElement? element, string language)
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Undefined
                           || element.Value.ValueKind == JsonValueKind.Null)
            return "";

        if (element.Value.ValueKind == JsonValueKind.String)
            return element.Value.GetString() ?? "";

        if (element.Value.ValueKind == JsonValueKind.Object)
        {
            // Try requested language first
            if (element.Value.TryGetProperty(language, out var langValue)
                && langValue.ValueKind == JsonValueKind.String)
                return langValue.GetString() ?? "";

            // Schemas disagree on case for the same language (one game ships "LATAM", another
            // "latam"), and one value in config has to serve every game, so retry ignoring case
            // before treating it as unavailable.
            foreach (var prop in element.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String
                    && string.Equals(prop.Name, language, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetString() ?? "";
            }

            // Fallback to english
            Logger.Warn($"Language '{language}' not available, falling back to english");
            if (language != "english"
                && element.Value.TryGetProperty("english", out var engValue)
                && engValue.ValueKind == JsonValueKind.String)
                return engValue.GetString() ?? "";

            // Fallback to first available value
            foreach (var prop in element.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    return prop.Value.GetString() ?? "";
            }
        }

        return "";
    }

    /// <summary>
    /// Finds a definition by achievement internal name (case-insensitive).
    /// </summary>
    public static AchievementDefinition? FindDefinition(
        IEnumerable<AchievementDefinition> definitions, string achievementName)
    {
        return definitions.FirstOrDefault(
            d => string.Equals(d.Name, achievementName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Default subfolder GBE falls back to when an icon path is not found verbatim.
    /// </summary>
    private const string DefaultImageDir = "achievement_images";

    /// <summary>
    /// Resolves the icon file path for an achievement, relative to the game's
    /// steam_settings/ directory. Mirrors GBE's lookup (steam_user_stats_achievements.cpp):
    /// try the icon path verbatim first, then fall back to the achievement_images/ subfolder.
    /// Configs from other tools often store a bare filename ("ACH01.jpg") that only
    /// resolves via the fallback.
    /// </summary>
    public static string? ResolveIconPath(AchievementDefinition definition, string metadataDir)
    {
        var iconName = definition.Icon;
        if (string.IsNullOrEmpty(iconName))
            return null;

        // Resolve and validate path stays within metadata directory (prevent path traversal)
        var metaDirFull = Path.GetFullPath(metadataDir) + Path.DirectorySeparatorChar;

        // Icon paths in the schema are relative to steam_settings/ (e.g. "img/abc123.jpg")
        return TryResolve(Path.Combine(metadataDir, iconName), metaDirFull)
               // Fall back to the achievement_images/ subfolder, as GBE does
               ?? TryResolve(Path.Combine(metadataDir, DefaultImageDir, iconName), metaDirFull);
    }

    /// <summary>
    /// Returns <paramref name="candidate"/> (or a variant with a common image extension)
    /// if it exists and stays within <paramref name="metaDirFull"/>; otherwise null.
    /// </summary>
    private static string? TryResolve(string candidate, string metaDirFull)
    {
        var fullPath = Path.GetFullPath(candidate);
        if (fullPath.StartsWith(metaDirFull, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            return fullPath;

        // Try with common extensions
        foreach (var ext in new[] { ".jpg", ".png", ".bmp", ".ico" })
        {
            var withExt = fullPath + ext;
            if (withExt.StartsWith(metaDirFull, StringComparison.OrdinalIgnoreCase) && File.Exists(withExt))
                return withExt;
        }

        return null;
    }

    /// <summary>
    /// Resolves display name, description, and icon path for an achievement.
    /// Returns null when neither the game's schema nor the unlock entry itself names it.
    /// </summary>
    public static ResolvedAchievement? Resolve(
        GameCache gameCache, string appId, string achievementName, AchievementUnlockState? unlockState, string language)
    {
        // How hard to look for a schema depends on what is at stake. Without inline text a missing
        // schema means no notification at all, so a full rescan per unlock is worth it; with inline
        // text the notification already works and the schema only upgrades it, so one rescan per
        // appid is enough to pick up a config dropped in after startup.
        var game = HasInlineText(unlockState)
            ? gameCache.LookupScanningOnce(appId)
            : gameCache.Lookup(appId);

        var definitions = game != null ? GameCache.LoadDefinitions(game) : null;
        var metadataDir = game != null ? Path.GetDirectoryName(game.MetadataPath)! : "";

        return ResolvePreferringSchema(unlockState, definitions, metadataDir, achievementName, language);
    }

    /// <summary>
    /// Resolves one achievement from the two sources that can describe it: the game's schema (already
    /// loaded, so a caller iterating many achievements parses it once) and the unlock entry's own
    /// inline text. The single place this precedence is decided, so the popup and the Recent-
    /// achievements panel can never disagree about an achievement's text. Null when neither source
    /// names the achievement.
    /// The schema leads because it is the only source with icons and localised text — a self-describing
    /// emulator ships neither. Matching on the achievement name is itself the appid-collision guard
    /// (Ubisoft and Steam id ranges overlap): a schema cached under a colliding id defines other
    /// achievements, so it does not match and the inline text stands.
    /// Field by field rather than source by source, because a schema can name an achievement and still
    /// leave a field blank — Steam redacts hidden achievements' descriptions, and the Add game wizard
    /// writes them empty when no Firecrawl key fills them in. Choosing wholesale would then discard a
    /// description the unlock file did carry.
    /// </summary>
    public static ResolvedAchievement? ResolvePreferringSchema(
        AchievementUnlockState? state, IEnumerable<AchievementDefinition>? definitions,
        string metadataDir, string achievementName, string language)
    {
        var definition = definitions != null ? FindDefinition(definitions, achievementName) : null;
        var inline = HasInlineText(state) ? state : null;
        if (definition == null && inline == null)
            return null;

        return new ResolvedAchievement
        {
            DisplayName = FirstNonEmpty(
                GetDisplayText(definition?.DisplayName, language),
                GetDisplayText(inline?.DisplayName, language),
                achievementName),
            Description = FirstNonEmpty(
                GetDisplayText(definition?.Description, language),
                GetDisplayText(inline?.Description, language)),
            // Only the schema can supply an icon: writers that inline their text ship none, and the
            // GSE Saves folder is never probed for images.
            IconPath = definition != null ? ResolveIconPath(definition, metadataDir) : null
        };
    }

    private static string FirstNonEmpty(params string[] candidates)
        => Array.Find(candidates, c => !string.IsNullOrEmpty(c)) ?? "";
}

public sealed class ResolvedAchievement
{
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public string? IconPath { get; init; }
}
