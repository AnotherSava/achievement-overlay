using System.Text.Json;
using System.Text.Json.Nodes;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class DiagnosticReportTests
{
    private static DiagnosticReportInputs Inputs(
        DiagnosticFile? config = null, DiagnosticFile? schema = null, DiagnosticFile? unlock = null, string log = "") =>
        new()
        {
            Version = "1.9.1+abc1234",
            GeneratedAt = "2026-09-02T22:35:00+03:00",
            AppId = "812140",
            GameName = "AC Odyssey",
            Config = config ?? DiagnosticFile.Absent,
            Schema = schema ?? DiagnosticFile.Absent,
            Unlock = unlock ?? DiagnosticFile.Absent,
            Log = log
        };

    private static DiagnosticFile Present(string content) => new() { Path = @"C:\x\y.json", Status = "ok", Content = content };

    private static JsonNode Compose(DiagnosticReportInputs inputs) => JsonNode.Parse(DiagnosticReport.Compose(inputs))!;

    // --- Redaction ---

    [Theory]
    [InlineData("steamWebApiKey")]
    [InlineData("firecrawlApiKey")]
    [InlineData("SteamWebApiKey")]
    [InlineData("someFutureApiKey")]
    [InlineData("accessToken")]
    [InlineData("clientSecret")]
    [InlineData("password")]
    public void IsSecretSetting_TrueForCredentials(string name) =>
        Assert.True(DiagnosticReport.IsSecretSetting(name));

    [Theory]
    [InlineData("gamesPaths")]
    [InlineData("gseSavesPaths")]
    [InlineData("language")]
    [InlineData("recentAchievementsShortcut")]
    [InlineData("recentAchievementsHotkey")]
    [InlineData("trackingConfigured")]
    public void IsSecretSetting_FalseForOrdinarySettings(string name) =>
        Assert.False(DiagnosticReport.IsSecretSetting(name));

    [Fact]
    public void Compose_ReplacesApiKeyValues()
    {
        var report = Compose(Inputs(config: Present("""{"language":"russian","steamWebApiKey":"0123456789ABCDEF","firecrawlApiKey":"fc-secret"}""")));

        var config = report["config"]!["content"]!;
        Assert.Equal(DiagnosticReport.Redacted, (string?)config["steamWebApiKey"]);
        Assert.Equal(DiagnosticReport.Redacted, (string?)config["firecrawlApiKey"]);
        Assert.Equal("russian", (string?)config["language"]);
    }

    [Fact]
    public void Compose_LeavesAnEmptyApiKeyEmpty()
    {
        // "no key configured" is itself an answer — whether hidden descriptions were ever fetchable.
        // Redacting it would claim a key exists.
        var report = Compose(Inputs(config: Present("""{"steamWebApiKey":"","firecrawlApiKey":null}""")));

        var config = report["config"]!["content"]!;
        Assert.Equal("", (string?)config["steamWebApiKey"]);
        Assert.Null((string?)config["firecrawlApiKey"]);
    }

    [Fact]
    public void Compose_DoesNotRedactAKeyLikeNameInsideTheSchema()
    {
        // Redaction is for the config only: an achievement legitimately named "Token" is not a secret.
        var report = Compose(Inputs(schema: Present("""[{"name":"apiKey","displayName":"Token Collector"}]""")));

        var first = report["schema"]!["content"]![0]!;
        Assert.Equal("apiKey", (string?)first["name"]);
        Assert.Equal("Token Collector", (string?)first["displayName"]);
    }

    // --- File status ---

    [Fact]
    public void Compose_MarksAMissingFileRatherThanOmittingIt()
    {
        var report = Compose(Inputs(schema: new DiagnosticFile { Path = @"C:\gone.json", Status = "missing" }));

        var schema = report["schema"]!;
        Assert.Equal("missing", (string?)schema["status"]);
        Assert.Equal(@"C:\gone.json", (string?)schema["path"]);
        Assert.Null(schema["content"]);
    }

    [Fact]
    public void Compose_QuotesAnUnparsableFileSoTheDamageIsVisible()
    {
        var report = Compose(Inputs(unlock: Present("""{"1": {"earned": tru""")));

        var unlock = report["unlockFile"]!;
        Assert.Equal("unparsable", (string?)unlock["status"]);
        Assert.NotNull((string?)unlock["error"]);
        Assert.Contains("earned", (string?)unlock["excerpt"]);
    }

    [Fact]
    public void Compose_KeepsSelfDescribingUnlockTextIntact()
    {
        var report = Compose(Inputs(unlock: Present("""{"1":{"earned":1,"displayName":"This is Sparta!","description":"Complete the Battle of 300."}}""")));

        var entry = report["unlockFile"]!["content"]!["1"]!;
        Assert.Equal("This is Sparta!", (string?)entry["displayName"]);
        Assert.Equal(1, (int?)entry["earned"]);
    }

    // --- Shape ---

    [Fact]
    public void Compose_CarriesTheVersionWithItsCommitSuffix()
    {
        // A report naming only "1.9.1" cannot say which build produced it.
        var report = Compose(Inputs());

        Assert.Equal("1.9.1+abc1234", (string?)report["app"]!["version"]);
        Assert.Equal("2026-09-02T22:35:00+03:00", (string?)report["app"]!["generated"]);
    }

    [Fact]
    public void Compose_ListsEverySettingsFolderNotOnlyTheOneThatWon()
    {
        var inputs = new DiagnosticReportInputs
        {
            Version = "1.0.0",
            GeneratedAt = "2026-09-02T22:35:00+03:00",
            AppId = "1349230",
            GameName = "Atomfall",
            SettingsDirs = new[] { @"C:\Games\Atomfall\bin\coldclient\steam_settings", @"C:\Games\Atomfall\steam_settings" }
        };

        var dirs = Compose(inputs)["game"]!["settingsDirs"]!.AsArray();
        Assert.Equal(2, dirs.Count);
        Assert.Equal(@"C:\Games\Atomfall\bin\coldclient\steam_settings", (string?)dirs[0]);
    }

    [Fact]
    public void Compose_EmitsTheLogAsLines()
    {
        var report = Compose(Inputs(log: "first\r\nsecond\r\n"));

        var log = report["log"]!["sessions"]![0]!["lines"]!.AsArray();
        Assert.Equal(2, log.Count);
        Assert.Equal("first", (string?)log[0]);
        Assert.Equal("second", (string?)log[1]);
    }

    // --- Log excerpting ---

    private static string Sessions(int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"{Logger.SessionBannerPrefix} {i} =====\nline {i}a\nline {i}b\n"));

    [Fact]
    public void TakeRecentSessions_KeepsEverythingWhenThereAreFewEnough()
    {
        var excerpt = DiagnosticReport.TakeRecentSessions(Sessions(3), 5);

        Assert.Equal(3, excerpt.SessionsIncluded);
        Assert.Equal(0, excerpt.SessionsOmitted);
        Assert.Equal(9, excerpt.Lines.Count);
    }

    [Fact]
    public void TakeRecentSessions_KeepsTheMostRecentAndSaysWhatItDropped()
    {
        var excerpt = DiagnosticReport.TakeRecentSessions(Sessions(20), 5);

        Assert.Equal(5, excerpt.SessionsIncluded);
        Assert.Equal(15, excerpt.SessionsOmitted);
        Assert.Equal(45, excerpt.LinesOmitted);
        Assert.StartsWith($"{Logger.SessionBannerPrefix} 16", excerpt.Lines[0]);
        Assert.Equal("line 20b", excerpt.Lines[^1]);
    }

    [Fact]
    public void TakeRecentSessions_CarriesABannerlessLogWhole()
    {
        // A log written before session banners existed has none; dropping it would lose the one
        // report where the user has not restarted since upgrading.
        var excerpt = DiagnosticReport.TakeRecentSessions("old line\nanother\n", 5);

        Assert.Equal(2, excerpt.Lines.Count);
        Assert.Equal(0, excerpt.SessionsIncluded);
        Assert.Equal(0, excerpt.SessionsOmitted);
    }

    [Fact]
    public void Compose_ReportsWhatTheLogLeftOutRatherThanTruncatingQuietly()
    {
        var log = Compose(Inputs(log: Sessions(20)))["log"]!;

        Assert.Equal(DiagnosticReport.ReportedSessions, (int?)log["sessionsIncluded"]);
        Assert.Equal(15, (int?)log["sessionsOmitted"]);
        Assert.Equal(45, (int?)log["linesOmitted"]);
    }

    // --- Leaving parts out ---

    [Fact]
    public void Compose_MarksAPartTheUserLeftOutRatherThanDroppingItSilently()
    {
        // "they did not send this" and "the app could not find it" are different answers to a bug
        // report, so an excluded part is stated rather than absent.
        var inputs = Inputs(
            config: Present("""{"language":"russian"}"""),
            schema: Present("""[{"name":"001"}]"""),
            unlock: Present("""{"1":{"earned":1}}"""),
            log: "a line");

        var report = Compose2(inputs, new DiagnosticSections { Config = false, Log = false, Schema = false, Unlock = false });

        Assert.Equal(DiagnosticReport.NotIncluded, (string?)report["config"]!["status"]);
        Assert.Equal(DiagnosticReport.NotIncluded, (string?)report["log"]!["status"]);
        Assert.Equal(DiagnosticReport.NotIncluded, (string?)report["schema"]!["status"]);
        Assert.Equal(DiagnosticReport.NotIncluded, (string?)report["unlockFile"]!["status"]);
    }

    [Fact]
    public void Compose_LeavingOnePartOutKeepsTheOthers()
    {
        var inputs = Inputs(config: Present("""{"language":"russian"}"""), schema: Present("""[{"name":"001"}]"""));

        var report = Compose2(inputs, new DiagnosticSections { Schema = false });

        Assert.Equal("russian", (string?)report["config"]!["content"]!["language"]);
        Assert.Equal(DiagnosticReport.NotIncluded, (string?)report["schema"]!["status"]);
    }

    [Fact]
    public void Compose_AlwaysKeepsTheGameIdentity()
    {
        // The appid and the settings folders are what make the rest interpretable; there is no
        // switch that removes them.
        var report = Compose2(Inputs(), new DiagnosticSections { Config = false, Log = false, Schema = false, Unlock = false });

        Assert.Equal("812140", (string?)report["game"]!["appId"]);
        Assert.Equal("1.9.1+abc1234", (string?)report["app"]!["version"]);
    }

    private static JsonNode Compose2(DiagnosticReportInputs inputs, DiagnosticSections sections) =>
        JsonNode.Parse(DiagnosticReport.Compose(inputs, sections))!;

    // --- Repeated sessions ---

    private static string Run(string time, params string[] body) =>
        $"{Logger.SessionBannerPrefix} {time}, 1.9.1+abc =====\n"
        + string.Concat(body.Select(b => $"[2026-09-02 {time}] [INFO] {b}\n"));

    [Fact]
    public void SplitSessions_BlanksARunThatRepeatedTheOneBeforeIt()
    {
        // Restarting logs the same startup sequence every time; five copies of it is noise in a
        // document whose whole point is being read before it is sent.
        var lines = DiagnosticReport.SplitLines(Run("10:00:00", "scan", "cached") + Run("11:00:00", "scan", "cached"));

        var sessions = DiagnosticReport.SplitSessions(lines);

        Assert.Equal(2, sessions.Count);
        Assert.False(sessions[0].IdenticalToPrevious);
        Assert.Equal(2, sessions[0].Lines.Count);
        Assert.True(sessions[1].IdenticalToPrevious);
        Assert.Empty(sessions[1].Lines);
    }

    [Fact]
    public void SplitSessions_KeepsEveryBannerSoTheRunIsStillVisible()
    {
        var lines = DiagnosticReport.SplitLines(Run("10:00:00", "scan") + Run("11:00:00", "scan"));

        var sessions = DiagnosticReport.SplitSessions(lines);

        Assert.All(sessions, s => Assert.NotNull(s.Banner));
        Assert.Contains("11:00:00", sessions[1].Banner);
    }

    [Fact]
    public void SplitSessions_ShowsARunThatActuallyDiffers()
    {
        var lines = DiagnosticReport.SplitLines(Run("10:00:00", "scan") + Run("11:00:00", "scan", "something went wrong"));

        var sessions = DiagnosticReport.SplitSessions(lines);

        Assert.False(sessions[1].IdenticalToPrevious);
        Assert.Equal(2, sessions[1].Lines.Count);
    }

    [Fact]
    public void SplitSessions_ComparesAgainstTheNeighbourNotAnyEarlierRun()
    {
        // A run matching an older one but not its neighbour is shown in full, rather than pointing the
        // reader backwards through an index they have to resolve themselves.
        var lines = DiagnosticReport.SplitLines(Run("10:00:00", "scan") + Run("11:00:00", "different") + Run("12:00:00", "scan"));

        var sessions = DiagnosticReport.SplitSessions(lines);

        Assert.False(sessions[2].IdenticalToPrevious);
        Assert.Single(sessions[2].Lines);
    }

    [Fact]
    public void SplitSessions_HandlesALogWithNoBanners()
    {
        var sessions = DiagnosticReport.SplitSessions(new[] { "old line", "another" });

        Assert.Single(sessions);
        Assert.Null(sessions[0].Banner);
        Assert.Equal(2, sessions[0].Lines.Count);
    }

    [Fact]
    public void Compose_OmitsLinesForARepeatedRunButKeepsItsBanner()
    {
        var log = Compose(Inputs(log: Run("10:00:00", "scan") + Run("11:00:00", "scan")))["log"]!;

        var sessions = log["sessions"]!.AsArray();
        Assert.Equal(2, sessions.Count);
        Assert.True((bool?)sessions[1]!["identicalToPrevious"]);
        Assert.Null(sessions[1]!["lines"]);
        Assert.NotNull((string?)sessions[1]!["banner"]);
    }

    // --- Keeping other games out of it ---

    private static readonly string[] Roots = { @"C:\Games", @"C:\Users\Sam\AppData\Roaming\GSE Saves" };
    private static readonly string[] OursOnly = { @"C:\Games\Odyssey\steam_settings", @"C:\Users\Sam\AppData\Roaming\GSE Saves\812140", @"C:\Programs\achievement-overlay" };

    private static IReadOnlyList<string> Keep(params string[] lines) =>
        DiagnosticReport.KeepLinesForGame(lines, "812140", Roots, OursOnly);

    [Fact]
    public void KeepLinesForGame_DropsAnotherGamesLine() =>
        Assert.Empty(Keep(@"  Cached: appid=1687950, game=Persona 5 Royal, path='C:\Games\Persona 5 Royal\steam_settings\achievements.json'"));

    [Fact]
    public void KeepLinesForGame_DropsAPathOnlyLineWithNoAppIdAtAll()
    {
        // The scanner's 'Error processing' and the watcher's file errors name a folder and no appid.
        // An appid-only rule would publish 'C:\Games\Persona 5 Royal\_crack' from a report about
        // a different game entirely.
        Assert.Empty(Keep(@"  Error processing 'C:\Games\Persona 5 Royal\_crack\steam_appid.txt': Access denied"));
        Assert.Empty(Keep(@"  Skipped: appid=1687950 at 'C:\Games\Persona 5 Royal\_crack' (no 'achievements.json')"));
    }

    [Fact]
    public void KeepLinesForGame_KeepsOurOwnGame() =>
        Assert.Single(Keep(@"  Cached: appid=812140, game=Odyssey, path='C:\Games\Odyssey\steam_settings\achievements.json'"));

    [Fact]
    public void KeepLinesForGame_KeepsLinesThatIdentifyNoGame()
    {
        // These describe the app, not somebody's library.
        var kept = Keep(
            "===== session started 2026-09-02 22:41:49, 1.9.1+abc =====",
            "[WARN] Language 'russian' not available, falling back to english",
            "[WARN] Could not register hotkey 'Ctrl+Shift+H' — use the tray menu instead");

        Assert.Equal(3, kept.Count);
    }

    [Fact]
    public void KeepLinesForGame_KeepsAConfiguredRootButNotWhatIsInsideIt()
    {
        // The config section carries the roots anyway, so naming one discloses nothing new.
        Assert.Single(Keep(@"Config: gamesPaths='C:\Games', gseSavesPaths='C:\Users\Sam\AppData\Roaming\GSE Saves', language=russian"));
        Assert.Empty(Keep(@"  Watching for achievements in 'C:\Games\Some Other Game'"));
    }

    [Fact]
    public void KeepLinesForGame_KeepsAnUnquotedPathFollowedByProse() =>
        Assert.Single(Keep(@"Font_Override 'x.ttf' does not resolve to a file under C:\Games\Odyssey\steam_settings; ignoring it."));

    [Fact]
    public void KeepLinesForGame_DropsALineNamingTwoGamesRatherThanKeepingItForTheHalfThatMatches() =>
        Assert.Empty(Keep("Compared appid=812140 against appid=1687950"));

    [Fact]
    public void Compose_CountsTheLinesItRemovedForOtherGames()
    {
        var log = $"{Logger.SessionBannerPrefix} 1 =====\n"
                + "[INFO] Starting game cache scan...\n"
                + @"  Cached: appid=1687950, game=Other, path='C:\Games\Other\steam_settings\achievements.json'" + "\n"
                + @"  Cached: appid=812140, game=Odyssey, path='C:\Games\Odyssey\steam_settings\achievements.json'" + "\n";

        var inputs = new DiagnosticReportInputs
        {
            Version = "1.0.0", GeneratedAt = "2026-09-02T22:35:00+03:00", AppId = "812140",
            Log = log, ConfiguredRoots = Roots, GameFolders = OursOnly
        };

        var reported = Compose(inputs)["log"]!;
        Assert.Equal(1, (int?)reported["linesAboutOtherGamesRemoved"]);
        Assert.Equal(2, reported["sessions"]![0]!["lines"]!.AsArray().Count);
    }

    [Fact]
    public void Compose_PutsWhatNeedsReviewingBeforeTheBulk()
    {
        // The review is the only thing between the user and publishing their paths, so the config and
        // the log must not sit below several hundred lines of the publisher's achievement text.
        var top = Compose(Inputs()).AsObject().Select(p => p.Key).ToList();

        Assert.True(top.IndexOf("config") < top.IndexOf("schema"));
        Assert.True(top.IndexOf("log") < top.IndexOf("schema"));
        Assert.True(top.IndexOf("game") < top.IndexOf("schema"));
        // The schema is the bulk of the file, so it trails everything worth reading.
        Assert.Equal("schema", top[^1]);
    }

    [Fact]
    public void Compose_ProducesValidJsonForAnEmptyReport() =>
        Assert.NotNull(JsonNode.Parse(DiagnosticReport.Compose(Inputs())));

    [Theory]
    [InlineData("", 0)]
    [InlineData("one", 1)]
    [InlineData("one\n", 1)]
    [InlineData("one\ntwo\n", 2)]
    public void SplitLines_DoesNotInventATrailingEmptyLine(string text, int expected) =>
        Assert.Equal(expected, DiagnosticReport.SplitLines(text).Count);

    // --- File name ---

    [Fact]
    public void SuggestedFileName_UsesTheGameNameWhenThereIsOne() =>
        Assert.Equal("achievement-overlay-AC Odyssey-812140.json", DiagnosticReport.SuggestedFileName("812140", "AC Odyssey"));

    [Fact]
    public void SuggestedFileName_FallsBackToTheAppId() =>
        Assert.Equal("achievement-overlay-812140.json", DiagnosticReport.SuggestedFileName("812140", null));

    [Fact]
    public void SuggestedFileName_StripsCharactersAFileNameCannotHold() =>
        Assert.DoesNotContain(':', DiagnosticReport.SuggestedFileName("812140", "Trine 2: Complete"));

    // --- Keeping the Windows account name out of every field ---

    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string AppDataDir => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static string JsonEscaped(string path) => path.Replace(@"\", @"\\");

    [Fact]
    public void Compose_CollapsesAPathHidingInAConfigValue()
    {
        // soundPath is an absolute path the user picks from a file dialog, and gamesPaths can sit
        // under the profile too. Collapsing only the fields that obviously hold a path missed both,
        // which is why the whole document is swept instead.
        var config = $$"""{"soundPath":"{{JsonEscaped(Profile)}}\\Music\\ding.wav","language":"english"}""";

        var report = Compose(Inputs(config: Present(config)));

        Assert.Equal(@"%userprofile%\Music\ding.wav", (string?)report["config"]!["content"]!["soundPath"]);
    }

    [Fact]
    public void Compose_CollapsesInsideNestedContentAndArrays()
    {
        var inputs = new DiagnosticReportInputs
        {
            Version = "1.0.0",
            GeneratedAt = "2026-09-03T00:00:00+00:00",
            AppId = "1",
            SettingsDirs = new[] { Profile + @"\Games\X\steam_settings" },
            Unlock = new DiagnosticFile { Path = AppDataDir + @"\GSE Saves\1\achievements.json", Status = "ok", Content = "{}" }
        };

        var json = DiagnosticReport.Compose(inputs);

        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%userprofile%\\Games", json);
        Assert.Contains(@"%appdata%\\GSE Saves", json);
    }

    [Fact]
    public void Compose_CollapsesInsideALogLine()
    {
        // The root has to be disclosed for the line to survive the other-games filter at all — that
        // filter runs on the expanded paths the log was written with, which is why collapsing happens
        // afterwards, over the whole document.
        var savesRoot = AppDataDir + @"\GSE Saves";
        var inputs = new DiagnosticReportInputs
        {
            Version = "1.0.0",
            GeneratedAt = "2026-09-03T00:00:00+00:00",
            AppId = "812140",
            ConfiguredRoots = new[] { savesRoot },
            Log = $"{Logger.SessionBannerPrefix} 1 =====\n[INFO] Watching for achievements in '{savesRoot}'\n"
        };

        var json = DiagnosticReport.Compose(inputs);

        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%appdata%\\GSE Saves", json);
    }

    [Fact]
    public void Compose_CollapsesTheForwardSlashSpellingToo()
    {
        // Windows accepts it, so a hand-edited config can carry it even though nothing here writes it.
        var config = $$"""{"gamesPaths":"{{Profile.Replace('\\', '/')}}/Games"}""";

        var report = Compose(Inputs(config: Present(config)));

        Assert.Equal("%userprofile%/Games", (string?)report["config"]!["content"]!["gamesPaths"]);
    }

    [Fact]
    public void Compose_LeavesAPathOutsideTheProfileAlone() =>
        Assert.Equal(@"C:\Games\Odyssey",
            (string?)Compose(Inputs(config: Present("""{"gamesPaths":"C:\\Games\\Odyssey"}""")))["config"]!["content"]!["gamesPaths"]);
}
