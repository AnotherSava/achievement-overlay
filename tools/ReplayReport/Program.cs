using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AchievementOverlay.ReplayReport;

/// <summary>
/// Replays a diagnostic report — the file <b>Report a problem…</b> produces — through the app's own
/// resolver, and prints what the reporter would have seen for every achievement: the display name,
/// the description, and which of the two sources supplied each, under which language.
/// <para>
/// It calls <see cref="AchievementMetadata"/> rather than carrying a copy of it. A reimplementation
/// answers "what does my copy do", never "what did they see", and it would drift from the resolver
/// with nothing to say so — which is how a replay comes to clear a build that is actually broken.
/// </para>
/// <para>
/// Nothing in <c>src/</c> exists for this tool: the three entry points it needs are already public
/// because the app itself calls them, and text resolution is a pure function of four things the
/// report already carries — the unlock state, the schema, the achievement's name, and the
/// configured language.
/// </para>
/// </summary>
internal static class Program
{
    private const string Usage = """
        Replays a diagnostic report through this build's resolver.

          replay-report <report.json> [--language <name>] [--out <file>]

          --language <name>  Resolve as if "Achievement text" were set to this, rather than to
                             whatever the report's own config says.
          --out <file>       Write UTF-8 to this file instead of to stdout. Worth using from
                             PowerShell, whose redirection rewrites the encoding.
        """;

    private static int Main(string[] args)
    {
        // Achievement text is localised, so Cyrillic and CJK are routine — and the console's default
        // codepage replaces every character it cannot map, which would hollow out the output of
        // exactly the reports most worth replaying.
        Console.OutputEncoding = Encoding.UTF8;

        string? reportPath = null;
        string? language = null;
        string? outPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-h" or "--help")
            {
                Console.WriteLine(Usage);
                return 0;
            }

            if (arg is "--language" or "--out")
            {
                if (i + 1 >= args.Length)
                    return Fail($"'{arg}' needs a value.");
                if (arg == "--language")
                    language = args[++i];
                else
                    outPath = args[++i];
                continue;
            }

            if (arg.StartsWith('-') || reportPath != null)
                return Fail($"Unrecognised argument '{arg}'.");

            reportPath = arg;
        }

        if (reportPath == null)
            return Fail("Name the diagnostic report to replay.");

        string output;
        bool replayed;

        try
        {
            (output, replayed) = Replay(File.ReadAllText(reportPath), language);
        }
        // ArgumentException among them because two ordinary mistakes raise it rather than an IO or a
        // JSON error: an empty path, which is what an unset variable in `replay-report "$REPORT"`
        // passes, and a report hand-trimmed into a duplicate key, which JsonObject rejects on its
        // first enumeration. Neither deserves a stack trace.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            Console.Error.WriteLine($"Could not replay '{reportPath}': {ex.Message}");
            return 1;
        }

        if (outPath == null)
        {
            Console.Out.Write(output);
        }
        else
        {
            try
            {
                File.WriteAllText(outPath, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            // Named apart from the replay on purpose: the first thing anyone does on "could not
            // replay" is doubt the report they were sent, and a missing output folder says nothing
            // about it. The replay itself succeeded by this point.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"Could not write '{outPath}': {ex.Message}");
                return 1;
            }
        }

        // A report with no achievements in it produces a full page of zeros, which reads exactly like
        // a clean replay of a game with nothing wrong. Failing says which of the two it was to a
        // script diffing a before against an after, where nobody reads the header.
        return replayed ? 0 : 1;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        Console.Error.WriteLine(Usage);
        return 2;
    }

    /// <summary>
    /// A directory that is never created, passed where the resolver wants the schema's folder. Icons
    /// are the one thing a replay cannot reproduce: <see cref="AchievementMetadata.ResolveIconPath"/>
    /// needs the image files themselves, and a report carries the schema's text but not its folder.
    /// A placeholder rather than the reporter's own path, because that path can happen to exist on
    /// this machine — and the tool would then report an icon off the maintainer's disk as theirs.
    /// </summary>
    private static readonly string NoIconDirectory =
        Path.Combine(Path.GetTempPath(), "achievement-overlay-replay-no-icons");

    /// <summary>What <see cref="SourceOf"/> says when the resolved text matches no source it can see.</summary>
    private const string Unclear = "unclear";

    /// <summary>Every attribution, in a fixed order so two runs produce a clean diff.</summary>
    private static readonly string[] SourceOrder = ["schema", "unlock", "both", "name", "blank", Unclear];

    /// <summary>Every match outcome, likewise fixed.</summary>
    private static readonly string[] MatchOrder = ["exact", "padded", "ambiguous", "no match", "no schema"];

    /// <summary>The replay, and whether there was anything in the report to replay.</summary>
    private static (string Output, bool Replayed) Replay(string reportJson, string? languageOverride)
    {
        if (JsonNode.Parse(reportJson) is not JsonObject report)
            throw new JsonException("The report is not a JSON object.");

        var unlock = Section(report, "unlockFile");
        var schema = Section(report, "schema");
        var config = Section(report, "config");

        var (parsedStates, unlockError) = TryParse(unlock, AchievementMetadata.ParseUnlockStates);
        var states = parsedStates ?? new Dictionary<string, AchievementUnlockState>();
        var (definitions, schemaError) = TryParse(schema, AchievementMetadata.ParseDefinitions);

        // The unlock file drives the replay, in the order it lists its entries: those are the names
        // the app resolves, and reading them off the document rather than off the parsed dictionary
        // keeps the order the reporter's file has — and keeps an entry that failed to parse visible
        // instead of silently absent.
        var names = unlock.Content is JsonObject entries ? entries.Select(e => e.Key).ToList() : [];

        var (language, languageNote) = ChooseLanguage(languageOverride, config);
        var unlockReason = Reason(unlock.Status, unlockError);
        var schemaReason = Reason(schema.Status, schemaError);

        var text = new StringBuilder();
        AppendHeader(text, report, states, definitions, names.Count, language, languageNote, unlockReason, schemaReason);
        AppendAchievements(text, names, states, definitions, language, unlockReason, out var stats);
        AppendSummary(text, stats, definitions);
        return (text.ToString(), names.Count > 0);
    }

    /// <summary>How a section is described in the output: its status, and why it could not be used.</summary>
    private static string Reason(string status, string? error) =>
        error == null ? status : $"{status}, but this build could not read it — {error}";

    /// <summary>
    /// Parses one section of the report, failing the way the app fails. A section of the wrong JSON
    /// shape still reaches a report — <see cref="DiagnosticReport"/> marks a file unparsable only on
    /// a syntax error, so an object where an array belongs arrives with status <c>ok</c> — and over
    /// in the app it costs one source rather than the run: <see cref="GameCache.LoadDefinitions"/>
    /// swallows it and the achievement resolves from inline text alone. Letting it end the replay
    /// would answer "what did they see" with nothing, in exactly the case where a wrongly shaped
    /// third-party schema is the thing being diagnosed.
    /// </summary>
    private static (T? Parsed, string? Error) TryParse<T>(ReportSection section, Func<string, T> parse) where T : class
    {
        if (section.Content == null)
            return (null, null);

        try
        {
            return (parse(section.Content.ToJsonString()), null);
        }
        catch (JsonException ex)
        {
            return (null, ex.Message);
        }
    }

    // --- Reading the report ---

    /// <summary>One part of a report as this tool needs it: whether it is there, and its contents.</summary>
    private readonly record struct ReportSection(string Status, JsonNode? Content);

    private static ReportSection Section(JsonObject report, string key) =>
        report[key] is JsonObject section
            ? new ReportSection(Text(section["status"]) ?? "unknown", section["content"])
            : new ReportSection("absent from this report", null);

    /// <summary>A node's string value, or null when it is absent or is not a string.</summary>
    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// The language to resolve under, and where it came from. An empty value is carried through
    /// rather than corrected: that is what the app's own config holds until someone sets it, and a
    /// replay that quietly substituted english would stop reproducing the reporter's run.
    /// </summary>
    private static (string Language, string Note) ChooseLanguage(string? languageOverride, ReportSection config)
    {
        if (languageOverride != null)
            return (languageOverride, "from --language, not from the report");

        if (config.Content is not JsonObject settings)
            return ("english", $"an assumption — the report's config is {config.Status}");

        if (Text(settings["language"]) is not { } configured)
            return ("english", "an assumption — the report's config names no language");

        return configured.Length == 0
            ? (configured, "the report's config leaves it empty, which the app resolves as english")
            : (configured, "from the report's config");
    }

    // --- Output ---

    private static void AppendHeader(
        StringBuilder text, JsonObject report,
        Dictionary<string, AchievementUnlockState> states, List<AchievementDefinition>? definitions,
        int entryCount, string language, string languageNote, string unlockReason, string schemaReason)
    {
        var app = report["app"] as JsonObject;
        var game = report["game"] as JsonObject;

        // A local build carries the SDK's default version, and the suffix is the commit, which does
        // not move while the change being measured is still in the working tree — so a before and an
        // after print the same line here. Saying so is the difference between a measurement and two
        // files nobody can tell apart later.
        var replayedBy = AppUtilities.VersionLabel == "dev version"
            ? $"{AppUtilities.InformationalVersion} — a local build; the suffix is the commit, which does not move for uncommitted edits, so name the output files after what changed"
            : AppUtilities.InformationalVersion;

        text.AppendLine("Replaying a diagnostic report through this build's resolver.");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"  report from   {Text(app?["version"]) ?? "unknown"}, generated {Text(app?["generated"]) ?? "unknown"}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  replayed by   {replayedBy}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  game          {Text(game?["name"]) ?? "(unnamed)"} ({Text(game?["appId"]) ?? "no appid"})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  language      {(language.Length == 0 ? "(empty)" : language)} — {languageNote}");

        if (entryCount == 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  unlock file   {unlockReason} — nothing to resolve from");
        }
        else
        {
            // Said only when true: for an unlock file, "carries no text of its own" is what Languages
            // already reports, and printing both leaves one line saying the same thing twice.
            var selfDescribing = AchievementMetadata.IsSelfDescribing(states) ? "self-describing, " : "";
            text.AppendLine($"  unlock file   {unlockReason} — {entryCount} {(entryCount == 1 ? "entry" : "entries")}, {states.Count} readable, {selfDescribing}"
                            + Languages(AchievementMetadata.CollectLanguages(states.Values), language, TextsOf(states.Values)));
        }

        if (definitions == null)
        {
            // Deliberately about this replay rather than about the reporter's run: a schema left out
            // of the report is one the app may well have had, and only a schema this build could not
            // read is one the app could not read either.
            text.AppendLine(CultureInfo.InvariantCulture, $"  schema        {schemaReason} — every achievement here resolves from the unlock file alone");
        }
        else
        {
            text.AppendLine($"  schema        {schemaReason} — {definitions.Count} definitions, names are "
                            + $"{AchievementMetadata.DescribeNameStyle(definitions.Select(d => d.Name))}, "
                            + Languages(AchievementMetadata.CollectLanguages(definitions), language, TextsOf(definitions)));
        }

        text.AppendLine("  icons         not replayed — a report carries the schema's text, not its folder");
    }

    /// <summary>Both display-text fields of every entry, which is what a source's text shape is read off.</summary>
    private static IEnumerable<JsonElement?> TextsOf(IEnumerable<AchievementUnlockState> states) =>
        states.SelectMany(s => new[] { s.DisplayName, s.Description });

    /// <summary>The same for a schema.</summary>
    private static IEnumerable<JsonElement?> TextsOf(IEnumerable<AchievementDefinition> definitions) =>
        definitions.SelectMany(d => new[] { d.DisplayName, d.Description });

    /// <summary>
    /// How many languages a source offers, and whether the one being resolved under is among them —
    /// the single header fact that explains most "why is this in English" reports. A source whose
    /// text is plain strings offers no choice at all, which is a different answer from offering
    /// several and lacking this one.
    /// <para>
    /// An empty language set does not on its own mean plain strings:
    /// <see cref="AchievementMetadata.CollectLanguages(IEnumerable{AchievementUnlockState})"/>
    /// harvests keys off multi-language objects, so it is equally empty for a
    /// source carrying no text whatsoever — a plain GBE unlock file, which is the common case. The
    /// shape is asked for separately, or the header would assert "text in one language" on the same
    /// line as "no inline text".
    /// </para>
    /// </summary>
    private static string Languages(IReadOnlyCollection<string> available, string language, IEnumerable<JsonElement?> texts)
    {
        if (available.Count == 0)
        {
            return AchievementMetadata.DescribeTextShape(texts) == "none"
                ? "no display text of its own"
                : "text in one language (plain strings, nothing to choose between)";
        }

        if (language.Length == 0)
            return $"text in {available.Count} languages";

        return available.Contains(language, StringComparer.OrdinalIgnoreCase)
            ? $"text in {available.Count} languages, '{language}' among them"
            : $"text in {available.Count} languages, '{language}' NOT among them";
    }

    /// <summary>What a run counted, for the summary.</summary>
    private sealed class Stats
    {
        public Dictionary<string, int> Match { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> DisplayName { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Description { get; } = new(StringComparer.Ordinal);
        public HashSet<string> MatchedDefinitions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Unresolved { get; set; }
        public int Unreadable { get; set; }
    }

    private static void Count(Dictionary<string, int> into, string key) =>
        into[key] = into.GetValueOrDefault(key) + 1;

    private static void AppendAchievements(
        StringBuilder text, List<string> names, Dictionary<string, AchievementUnlockState> states,
        List<AchievementDefinition>? definitions, string language, string unlockReason, out Stats stats)
    {
        stats = new Stats();

        text.AppendLine();

        if (names.Count == 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Nothing to replay — the unlock file is {unlockReason}, and names no achievements.");
            text.AppendLine("Every count below is therefore zero because nothing ran, not because nothing was wrong.");
            return;
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"Achievements — {names.Count}, in the order the unlock file lists them");
        text.AppendLine();

        // Wide enough to line the blocks up, capped so one long identifier does not indent the rest
        // of the file off the screen.
        var nameWidth = Math.Min(names.Count == 0 ? 0 : names.Max(n => n.Length), 32);

        foreach (var name in names)
        {
            states.TryGetValue(name, out var state);

            var matchedExactly = false;
            var definition = definitions != null
                ? AchievementMetadata.FindDefinition(definitions, name, out matchedExactly)
                : null;

            // A refusal and an absence both come back null, and they are different findings: the
            // refusal means the schema holds the achievement twice under names that fold together,
            // where "no match" sends a maintainer looking for an entry that is missing. Which one it
            // was is observed by offering each definition on its own — a name matching two of them is
            // one the resolver declined to choose between — rather than by re-deriving the rule it
            // declined under. That reasoning is also the only record of it: the resolver says so in a
            // warning, and this process never initialises the log.
            var folding = definition == null && definitions != null
                ? definitions.Where(d => AchievementMetadata.FindDefinition([d], name, out _) != null)
                    .Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : [];

            var bucket = definitions == null ? "no schema"
                : definition != null ? matchedExactly ? "exact" : "padded"
                : folding.Count > 1 ? "ambiguous"
                : "no match";
            Count(stats.Match, bucket);
            if (definition != null)
                stats.MatchedDefinitions.Add(definition.Name);

            var match = bucket switch
            {
                "padded" => $"padded → '{definition!.Name}'",
                "ambiguous" => $"ambiguous → {string.Join(" and ", folding.Select(n => $"'{n}'"))}, so the schema was not used",
                _ => bucket
            };

            var earned = state == null ? "[unreadable]" : state.Earned ? "[earned]" : "[locked]";
            text.AppendLine(CultureInfo.InvariantCulture, $"{name.PadRight(nameWidth)}  {earned,-12}  {match}");

            // The app never resolves an entry it could not parse: ParseUnlockStates drops it, and both
            // consumers iterate only what parsed. Resolving it here would put text on the page that
            // was never on screen — so a report whose complaint is "this one never notified" would
            // replay as though it had worked.
            if (state == null)
            {
                stats.Unreadable++;
                text.AppendLine("    the app drops an entry it cannot read — no notification and no Recent entry");
                continue;
            }

            var resolved = AchievementMetadata.ResolvePreferringSchema(
                state, definitions, NoIconDirectory, name, language);

            if (resolved == null)
            {
                stats.Unresolved++;
                text.AppendLine("    neither source names it — the app would show no notification");
                continue;
            }

            // Asking each source directly for the same field the resolver just filled. This is what
            // makes the attribution an observation rather than a second copy of the precedence rule:
            // change the rule and these lines keep reporting what actually happened.
            var schemaName = AchievementMetadata.GetDisplayText(definition?.DisplayName, language);
            var schemaDesc = AchievementMetadata.GetDisplayText(definition?.Description, language);
            var inlineName = AchievementMetadata.GetDisplayText(state.DisplayName, language);
            var inlineDesc = AchievementMetadata.GetDisplayText(state.Description, language);

            var nameSource = SourceOf(resolved.DisplayName, schemaName, inlineName, name);
            var descSource = SourceOf(resolved.Description, schemaDesc, inlineDesc, nameFallback: null);
            Count(stats.DisplayName, nameSource);
            Count(stats.Description, descSource);

            text.AppendLine(CultureInfo.InvariantCulture, $"    name  {nameSource,-7}  \"{resolved.DisplayName}\"");
            text.AppendLine(CultureInfo.InvariantCulture, $"    desc  {descSource,-7}  \"{resolved.Description}\"");
        }
    }

    /// <summary>
    /// Which source supplied a resolved field, worked out by comparing what the resolver returned
    /// against what each source carries — not by repeating the precedence rule, which would make
    /// this tool a second implementation of the thing it is meant to measure. So it keeps telling
    /// the truth when the rule changes, and <see cref="Unclear"/> is a signal that the resolver
    /// produced text neither source holds rather than a wrong answer stated confidently.
    /// </summary>
    private static string SourceOf(string resolved, string schema, string inline, string? nameFallback)
    {
        if (resolved.Length == 0)
            return "blank";

        var fromSchema = schema.Length > 0 && resolved == schema;
        var fromInline = inline.Length > 0 && resolved == inline;

        if (fromSchema && fromInline)
            return "both";
        if (fromSchema)
            return "schema";
        if (fromInline)
            return "unlock";
        if (nameFallback != null && resolved == nameFallback)
            return "name";

        return Unclear;
    }

    /// <summary>How many schema entries are listed by name before the rest are counted instead.</summary>
    private const int ListedUnmatched = 10;

    private static void AppendSummary(StringBuilder text, Stats stats, List<AchievementDefinition>? definitions)
    {
        text.AppendLine();
        text.AppendLine("Summary");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"  matched       {Tally(stats.Match, MatchOrder)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  displayName   {Tally(stats.DisplayName, SourceOrder)}");
        // "name" is the displayName's fall back to the achievement's own internal name; a description
        // has nothing to fall back to, so the column would be a zero that means nothing.
        text.AppendLine(CultureInfo.InvariantCulture, $"  description   {Tally(stats.Description, SourceOrder.Where(s => s != "name"))}");
        // The two ways an achievement reaches no screen at all, kept apart: the app discarded the
        // entry before resolving it, or it resolved to nothing because no source named it.
        text.AppendLine(CultureInfo.InvariantCulture, $"  unreadable    {stats.Unreadable}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  unresolved    {stats.Unresolved}");

        if (definitions != null)
        {
            var unmatched = definitions.Select(d => d.Name).Where(n => !stats.MatchedDefinitions.Contains(n)).ToList();
            text.AppendLine(CultureInfo.InvariantCulture, $"  schema entries no unlock name matched: {unmatched.Count} of {definitions.Count}");
            if (unmatched.Count > 0)
            {
                var listed = string.Join(", ", unmatched.Take(ListedUnmatched).Select(n => $"'{n}'"));
                var more = unmatched.Count > ListedUnmatched ? $", and {unmatched.Count - ListedUnmatched} more" : "";
                text.AppendLine(CultureInfo.InvariantCulture, $"                {listed}{more}");
            }
        }

        if (stats.DisplayName.GetValueOrDefault(Unclear) + stats.Description.GetValueOrDefault(Unclear) > 0)
        {
            text.AppendLine();
            text.AppendLine("  Some fields hold text that neither the schema nor the unlock file carries, so this");
            text.AppendLine("  tool cannot say where it came from. The cause is not identified here — read those");
            text.AppendLine($"  entries, marked '{Unclear}', before trusting the counts above.");
        }
    }

    /// <summary>A count per key in a fixed order, zeros included, so two runs diff line for line.</summary>
    private static string Tally(Dictionary<string, int> counts, IEnumerable<string> order) =>
        string.Join("   ", order.Select(key => $"{key} {counts.GetValueOrDefault(key)}"));
}
