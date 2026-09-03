using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AchievementOverlay;

/// <summary>
/// Everything a diagnostic report is built from, already read off disk. Kept separate from the
/// composing so the shape of the report can be tested without a filesystem.
/// </summary>
public sealed class DiagnosticReportInputs
{
    public required string Version { get; init; }
    public required string GeneratedAt { get; init; }
    public required string AppId { get; init; }
    public string? GameName { get; init; }

    /// <summary>The schema the resolver actually reads, and whether it could be read.</summary>
    public DiagnosticFile Schema { get; init; } = DiagnosticFile.Absent;

    /// <summary>The GSE Saves unlock file for this appid.</summary>
    public DiagnosticFile Unlock { get; init; } = DiagnosticFile.Absent;

    /// <summary>
    /// Every <c>steam_settings</c> folder the game has, deepest first — the first is the one that
    /// supplied <see cref="Schema"/>. A game with two of them holding different text is a real and
    /// invisible failure mode, so the report names all of them rather than only the winner.
    /// </summary>
    public IReadOnlyList<string> SettingsDirs { get; init; } = Array.Empty<string>();

    public DiagnosticFile Config { get; init; } = DiagnosticFile.Absent;
    public string Log { get; init; } = "";

    /// <summary>
    /// The configured <c>gamesPaths</c> and <c>gseSavesPaths</c>, expanded. The report's config section
    /// carries these anyway, so a log line naming one discloses nothing new — but a line naming a
    /// folder <em>inside</em> one is about some other game and is dropped.
    /// </summary>
    public IReadOnlyCollection<string> ConfiguredRoots { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Folders belonging to the game being reported, plus the app's own. Anything under these is
    /// already disclosed by this report, so log lines mentioning them are kept.
    /// </summary>
    public IReadOnlyCollection<string> GameFolders { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A file the report carries: where it was, whether it was there, and its contents. Status is an
/// explicit field rather than a null <see cref="Content"/>, because "no such file", "could not be
/// parsed" and "empty" are three different answers to a bug report and must not read alike.
/// </summary>
public sealed class DiagnosticFile
{
    public static readonly DiagnosticFile Absent = new() { Status = "not configured" };

    public string? Path { get; init; }

    /// <summary>One of: <c>ok</c>, <c>missing</c>, <c>unreadable</c>, <c>unparsable</c>, <c>not configured</c>.</summary>
    public required string Status { get; init; }

    public string? Error { get; init; }

    /// <summary>The file's text, present when it was read whether or not it parsed.</summary>
    public string? Content { get; init; }

    public static DiagnosticFile Read(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return Absent;
        if (!File.Exists(path))
            return new DiagnosticFile { Path = path, Status = "missing" };

        try
        {
            return new DiagnosticFile { Path = path, Status = "ok", Content = File.ReadAllText(path) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DiagnosticFile { Path = path, Status = "unreadable", Error = ex.Message };
        }
    }
}

/// <summary>
/// Which parts of the report the user chose to include. Everything is in by default; a part left out
/// is marked as such in the report rather than silently missing, so a maintainer reading it can tell
/// "they did not send this" from "the app could not find it".
/// </summary>
public sealed class DiagnosticSections
{
    public static readonly DiagnosticSections All = new();

    public bool Config { get; init; } = true;
    public bool Log { get; init; } = true;
    public bool Schema { get; init; } = true;
    public bool Unlock { get; init; } = true;
}

/// <summary>
/// Builds the per-game report a user attaches to an issue. Composing is pure and the reading is not,
/// so the report's shape and its redaction can be tested without touching a disk.
/// </summary>
public static class DiagnosticReport
{
    /// <summary>
    /// How the report is written, and how the review window renders each part, so the two agree. The
    /// relaxed encoder matters: the default one escapes for HTML safety, which rendered the '+' in
    /// the version as a numeric escape and left the build unreadable in the pane the user is meant to
    /// read. The report is a file, not a fragment to embed in a page.
    /// </summary>
    public static readonly JsonSerializerOptions ReportJson =
        new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Status of a part the user unticked. Distinct from <c>missing</c>, which is the app failing to find it.</summary>
    public const string NotIncluded = "not included";

    private static JsonNode Excluded() => new JsonObject { ["status"] = NotIncluded };

    /// <summary>What a redacted credential is replaced with — deliberately obvious in a review pane.</summary>
    public const string Redacted = "xxxxxx";

    /// <summary>How much of an unparsable file is quoted, so a malformed config stays diagnosable without pasting a whole game's schema.</summary>
    private const int ExcerptLength = 2000;

    /// <summary>
    /// How many runs of the app the report carries. The log itself keeps far more, but a report is
    /// about something that just happened: enough to cover "I hit it, restarted, tried again", and
    /// few enough that the log stays a part of the report someone will actually read.
    /// </summary>
    public const int ReportedSessions = 5;

    /// <summary>
    /// Whether a config key holds a credential. Matched on the key's name rather than against a list
    /// of the two that exist today, so a credential added later is redacted by being named like one;
    /// the two known properties are named through <c>nameof</c> as well, so renaming one breaks the
    /// build instead of silently publishing it.
    /// </summary>
    public static bool IsSecretSetting(string name)
    {
        if (string.Equals(name, nameof(SettingsData.SteamWebApiKey), StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, nameof(SettingsData.FirecrawlApiKey), StringComparison.OrdinalIgnoreCase))
            return true;

        // "apikey" rather than "key", so a setting like recentAchievementsShortcut — or a hotkey by
        // any other name — is not mistaken for a credential and blanked out of the report.
        foreach (var marker in new[] { "apikey", "token", "secret", "password" })
        {
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The config with every credential's value replaced by <see cref="Redacted"/>. An absent or
    /// empty value is left exactly as it is: "no API key configured" is itself a useful answer, and
    /// replacing it would claim a key exists.
    /// </summary>
    public static JsonNode RedactConfig(JsonObject config)
    {
        foreach (var (key, value) in config.ToList())
        {
            if (IsSecretSetting(key) && value is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text))
                config[key] = JsonValue.Create(Redacted);
        }

        return config;
    }

    /// <summary>Renders the report a user reviews and attaches. Pure: everything it needs is in <paramref name="inputs"/>.</summary>
    public static string Compose(DiagnosticReportInputs inputs, DiagnosticSections? sections = null)
    {
        sections ??= DiagnosticSections.All;
        // Ordered for the person reviewing it, smallest and most sensitive first. Everything that
        // reveals anything about this machine — the paths, the config, the log — fits in the first
        // screen or two; the game's schema is the bulk of the file and is the publisher's text, so
        // it goes last rather than burying the parts worth reading under several hundred lines.
        var report = new JsonObject
        {
            ["app"] = new JsonObject
            {
                ["version"] = inputs.Version,
                ["generated"] = inputs.GeneratedAt
            },
            ["config"] = sections.Config ? Describe(inputs.Config, redactConfig: true) : Excluded(),
            ["log"] = sections.Log
                ? DescribeLog(BuildLog(inputs.Log, ReportedSessions, inputs.AppId, inputs.ConfiguredRoots, inputs.GameFolders))
                : Excluded(),
            // Each part is a top-level key so the document matches the parts the review window opens
            // one at a time: nesting the schema under the game identity put the bulk of the file
            // inside the pane meant to show a handful of lines.
            ["game"] = new JsonObject
            {
                ["appId"] = inputs.AppId,
                ["name"] = inputs.GameName,
                ["settingsDirs"] = new JsonArray(inputs.SettingsDirs.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray())
            },
            ["unlockFile"] = sections.Unlock ? Describe(inputs.Unlock, redactConfig: false) : Excluded(),
            ["schema"] = sections.Schema ? Describe(inputs.Schema, redactConfig: false) : Excluded()
        };

        // One pass over the finished document rather than a call at each place a path can appear.
        // Paths turn up in more fields than are obvious — a custom sound file, a games root under the
        // user profile, a third-party schema's absolute icon path — and a per-site call is a list that
        // has to be remembered every time a field is added. This cannot miss one.
        CollapseKnownFolders(report);
        return report.ToJsonString(ReportJson);
    }

    /// <summary>One run of the app within the reported log.</summary>
    public sealed class LogSession
    {
        /// <summary>The session banner verbatim, carrying both the time and the version. Null for a log written before banners existed.</summary>
        public string? Banner { get; init; }

        public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Set when this run logged exactly what the run before it logged, ignoring the timestamps.
        /// Its <see cref="Lines"/> are then left empty rather than repeated.
        /// </summary>
        public bool IdenticalToPrevious { get; init; }
    }

    /// <summary>The tail of a log, and what was left out of it.</summary>
    public sealed class LogExcerpt
    {
        public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
        public IReadOnlyList<LogSession> Sessions { get; init; } = Array.Empty<LogSession>();
        public int SessionsIncluded { get; init; }
        public int SessionsOmitted { get; init; }
        public int LinesOmitted { get; init; }

        /// <summary>Lines dropped because they were about a different game.</summary>
        public int LinesAboutOtherGames { get; init; }
    }

    /// <summary>The <c>[yyyy-MM-dd HH:mm:ss] </c> stamp every line but the banner opens with.</summary>
    private static readonly Regex LineTimestamp =
        new(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] ", RegexOptions.Compiled);

    /// <summary>
    /// Groups lines into runs and blanks out any run that repeated the one before it. Most restarts
    /// log exactly the same startup sequence, so a five-session excerpt is mostly the same forty lines
    /// copied five times — which is noise in a document whose whole purpose is being read before it is
    /// sent. The banner is always kept, so the reader still sees that the run happened and when.
    /// <para>
    /// Comparison ignores the leading timestamp, since that is the one thing guaranteed to differ, and
    /// it is against the previous run only: a run that matches an older one but not its neighbour is
    /// shown in full rather than referred backwards through an index the reader has to resolve.
    /// </para>
    /// </summary>
    public static IReadOnlyList<LogSession> SplitSessions(IReadOnlyList<string> lines)
    {
        var sessions = new List<LogSession>();
        string? banner = null;
        var body = new List<string>();
        IReadOnlyList<string>? previousBody = null;

        void Flush()
        {
            if (banner == null && body.Count == 0)
                return;

            var identical = previousBody != null && body.Select(Normalize).SequenceEqual(previousBody.Select(Normalize));
            sessions.Add(new LogSession
            {
                Banner = banner,
                Lines = identical ? Array.Empty<string>() : body.ToList(),
                IdenticalToPrevious = identical
            });
            previousBody = body.ToList();
            body = new List<string>();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith(Logger.SessionBannerPrefix, StringComparison.Ordinal))
            {
                Flush();
                banner = line;
                continue;
            }
            body.Add(line);
        }
        Flush();

        return sessions;
    }

    private static string Normalize(string line) => LineTimestamp.Replace(line, "");

    /// <summary>Any appid a log line refers to, in either spelling the app writes (<c>appid=812140</c>, <c>appid 812140</c>).</summary>
    private static readonly Regex AppIdReference =
        new(@"appid[= ](\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// A drive-lettered path in a log message. Stops at a quote because most messages wrap the path in
    /// them; where one does not, the match runs on into the prose after it, which the boundary rules in
    /// <see cref="IsDisclosedPath"/> tolerate.
    /// </summary>
    private static readonly Regex AbsolutePath =
        new(@"[A-Za-z]:\\[^'""]*", RegexOptions.Compiled);

    /// <summary>
    /// Drops log lines that are about a different game. A report about one game otherwise publishes the
    /// user's whole library: every installed game's name and folder, including folder names they may
    /// well not want on a public issue. None of it helps diagnose the game being reported.
    /// <para>
    /// A line survives only when everything identifying in it is already being disclosed anyway — every
    /// appid it names is this game's, and every path it contains is either one of the configured roots
    /// (which the report's config section carries regardless) or sits inside this game's own folders.
    /// Both tests must pass, because plenty of lines carry a path and no appid at all: the folder
    /// scanner's <c>Error processing</c>, the watcher's file errors, every per-game overlay ini warning.
    /// </para>
    /// <para>
    /// Lines identifying no game — the session banner, the config, an unavailable language, a hotkey
    /// that would not register — are kept: those describe the app, not somebody's library. Where the
    /// rules are unsure the line is dropped, since a missing line costs a follow-up question and a
    /// leaked one cannot be taken back.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> KeepLinesForGame(
        IReadOnlyList<string> lines, string appId,
        IReadOnlyCollection<string> configuredRoots, IReadOnlyCollection<string> gameFolders)
    {
        var kept = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            if (AppIdReference.Matches(line).Any(m => m.Groups[1].Value != appId))
                continue;
            if (AbsolutePath.Matches(line).Any(m => !IsDisclosedPath(m.Value, configuredRoots, gameFolders)))
                continue;
            kept.Add(line);
        }
        return kept;
    }

    /// <summary>
    /// Whether a path found in a log line is one the report already discloses. A configured root
    /// matches only as itself — <c>C:\Games</c> is in the config section, while <c>C:\Games\Someone
    /// Else</c> is a different game — whereas this game's own folders match anything beneath them.
    /// </summary>
    private static bool IsDisclosedPath(string candidate, IReadOnlyCollection<string> configuredRoots, IReadOnlyCollection<string> gameFolders)
    {
        foreach (var folder in gameFolders)
        {
            if (candidate.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var root in configuredRoots)
        {
            // The root itself, optionally followed by prose from the message rather than a subfolder.
            if (candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && (candidate.Length == root.Length || candidate[root.Length] is not ('\\' or '/')))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The last <paramref name="maxSessions"/> runs of the app, sliced on the session banner. A log
    /// that has been appended to for months is not something anyone reviews before attaching it, and
    /// a report is about a problem that just happened — so it carries the recent runs and says how
    /// many it left behind, rather than either shipping everything or truncating quietly.
    /// A log written before banners existed has none, and is carried whole.
    /// </summary>
    public static LogExcerpt TakeRecentSessions(string log, int maxSessions)
    {
        var lines = SplitLines(log);
        var starts = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(Logger.SessionBannerPrefix, StringComparison.Ordinal))
                starts.Add(i);
        }

        if (starts.Count <= maxSessions)
            return new LogExcerpt { Lines = lines, SessionsIncluded = starts.Count };

        var from = starts[^maxSessions];
        return new LogExcerpt
        {
            Lines = lines.Skip(from).ToList(),
            SessionsIncluded = maxSessions,
            SessionsOmitted = starts.Count - maxSessions,
            LinesOmitted = from
        };
    }

    /// <summary>The log as the report carries it: the recent sessions, narrowed to this game.</summary>
    private static LogExcerpt BuildLog(
        string log, int maxSessions, string appId,
        IReadOnlyCollection<string> configuredRoots, IReadOnlyCollection<string> gameFolders)
    {
        var recent = TakeRecentSessions(log, maxSessions);

        // Left expanded here on purpose: the path rules match what the log actually wrote, and the
        // whole document is collapsed once at the end of Compose.
        var kept = KeepLinesForGame(recent.Lines, appId, configuredRoots, gameFolders);

        return new LogExcerpt
        {
            Lines = kept,
            Sessions = SplitSessions(kept),
            SessionsIncluded = recent.SessionsIncluded,
            SessionsOmitted = recent.SessionsOmitted,
            LinesOmitted = recent.LinesOmitted,
            LinesAboutOtherGames = recent.Lines.Count - kept.Count
        };
    }

    private static JsonNode DescribeLog(LogExcerpt excerpt) => new JsonObject
    {
        ["sessionsIncluded"] = excerpt.SessionsIncluded,
        ["sessionsOmitted"] = excerpt.SessionsOmitted,
        ["linesOmitted"] = excerpt.LinesOmitted,
        ["linesAboutOtherGamesRemoved"] = excerpt.LinesAboutOtherGames,
        ["sessions"] = new JsonArray(excerpt.Sessions.Select(DescribeSession).ToArray())
    };

    private static JsonNode DescribeSession(LogSession session)
    {
        var node = new JsonObject { ["banner"] = session.Banner };
        if (session.IdenticalToPrevious)
            node["identicalToPrevious"] = true;
        else
            node["lines"] = new JsonArray(session.Lines.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray());
        return node;
    }

    /// <summary>
    /// Rewrites every string in the report into the portable path form, so no value anywhere carries
    /// the Windows account name. Runs on the finished document, after the log has been filtered
    /// against the expanded paths it was written with.
    /// </summary>
    private static void CollapseKnownFolders(JsonNode? node)
    {
        // Only a string is replaced. Assigning a node back into the slot it already occupies throws
        // — a JsonNode may have one parent — so anything that is not a string is recursed into
        // rather than reassigned.
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(property => property.Key).ToList())
                {
                    if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text))
                        obj[key] = JsonValue.Create(AppConfig.CollapseEnvironmentVariablesInText(text));
                    else
                        CollapseKnownFolders(obj[key]);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                        array[i] = JsonValue.Create(AppConfig.CollapseEnvironmentVariablesInText(text));
                    else
                        CollapseKnownFolders(array[i]);
                }
                break;
        }
    }

    /// <summary>Splits a log into lines without leaving a trailing empty entry for the final newline.</summary>
    public static IReadOnlyList<string> SplitLines(string text) =>
        string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    /// <summary>
    /// One file as it appears in the report: its path and status always, its parsed contents when it
    /// parsed, and an excerpt when it did not — a file that fails to parse is the bug in some reports,
    /// so the report has to carry enough of it to see why.
    /// </summary>
    private static JsonNode Describe(DiagnosticFile file, bool redactConfig)
    {
        var node = new JsonObject { ["path"] = file.Path, ["status"] = file.Status };
        if (file.Error != null)
            node["error"] = file.Error;

        if (file.Content == null)
            return node;

        try
        {
            var parsed = JsonNode.Parse(file.Content);
            node["content"] = redactConfig && parsed is JsonObject obj ? RedactConfig(obj) : parsed;
        }
        catch (JsonException ex)
        {
            node["status"] = "unparsable";
            node["error"] = ex.Message;
            node["excerpt"] = file.Content.Length > ExcerptLength ? file.Content[..ExcerptLength] : file.Content;
        }

        return node;
    }

    // --- Reading (the only part that touches disk) ---

    /// <summary>
    /// Gathers a report for one game: its schema, its unlock file, the app's config and the whole
    /// log. The log is taken whole rather than filtered to the game — the lines that matter most
    /// name no game at all (an unavailable language, a refused schema match), and a session's log
    /// measures a few KB, so filtering would drop evidence to save nothing.
    /// </summary>
    public static DiagnosticReportInputs Collect(
        string appId, GameInfo? game, IReadOnlyCollection<string> gseSavesPaths, IReadOnlyCollection<string> gamesPaths)
    {
        var unlockPath = FindUnlockFile(appId, gseSavesPaths);
        var gameFolders = new List<string>(game?.SettingsDirs ?? Array.Empty<string>());
        if (unlockPath != null)
            gameFolders.Add(Path.GetDirectoryName(unlockPath)!);
        // The app's own folder holds config.json and the log, both of which this report already carries.
        gameFolders.Add(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));

        return new DiagnosticReportInputs
        {
            Version = AppUtilities.InformationalVersion,
            GeneratedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ssK"),
            AppId = appId,
            GameName = game?.GameName,
            SettingsDirs = game?.SettingsDirs ?? Array.Empty<string>(),
            Schema = DiagnosticFile.Read(game?.MetadataPath),
            Unlock = DiagnosticFile.Read(unlockPath),
            Config = DiagnosticFile.Read(AppConfig.ConfigFilePath),
            Log = Logger.ReadAll(),
            ConfiguredRoots = gseSavesPaths.Concat(gamesPaths).Select(Path.TrimEndingDirectorySeparator).ToList(),
            GameFolders = gameFolders
        };
    }

    /// <summary>The first configured GSE Saves path that has this game's unlock file, or its expected location when none does.</summary>
    private static string? FindUnlockFile(string appId, IEnumerable<string> gseSavesPaths)
    {
        string? firstCandidate = null;

        foreach (var savesPath in gseSavesPaths)
        {
            var candidate = Path.Combine(savesPath, appId, "achievements.json");
            firstCandidate ??= candidate;
            if (File.Exists(candidate))
                return candidate;
        }

        return firstCandidate;
    }

    /// <summary>The default file name to save a report under, naming the game so several are told apart.</summary>
    public static string SuggestedFileName(string appId, string? gameName)
    {
        var label = string.IsNullOrWhiteSpace(gameName) ? appId : $"{gameName}-{appId}";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            label = label.Replace(invalid, '-');
        return $"achievement-overlay-{label}.json";
    }
}
