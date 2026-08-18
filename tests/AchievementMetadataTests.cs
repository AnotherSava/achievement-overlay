using System.IO;
using System.Text.Json;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class AchievementMetadataTests : IDisposable
{
    private readonly string _tempDir;

    public AchievementMetadataTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AchMetaTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // --- ParseUnlockStates tests ---

    [Fact]
    public void ParseUnlockStates_ValidJson_ReturnsAllEntries()
    {
        var json = """
        {
            "ACH01": {"earned": true, "earned_time": 1774855788},
            "ACH02": {"earned": false, "earned_time": 0},
            "ACH03": {"earned": true, "earned_time": 1774855800}
        }
        """;

        var states = AchievementMetadata.ParseUnlockStates(json);

        Assert.Equal(3, states.Count);
        Assert.True(states["ACH01"].Earned);
        Assert.Equal(1774855788L, states["ACH01"].EarnedTime);
        Assert.False(states["ACH02"].Earned);
        Assert.Equal(0L, states["ACH02"].EarnedTime);
        Assert.True(states["ACH03"].Earned);
    }

    [Fact]
    public void ParseUnlockStates_EmptyObject_ReturnsEmpty()
    {
        var states = AchievementMetadata.ParseUnlockStates("{}");
        Assert.Empty(states);
    }

    // --- ParseDefinitions tests ---

    [Fact]
    public void ParseDefinitions_ValidArray_ReturnsAllEntries()
    {
        var json = """
        [
            {
                "name": "ACH01",
                "displayName": "First Blood",
                "description": "Get your first kill",
                "icon": "ach01.png",
                "icon_gray": "ach01_gray.png",
                "hidden": 0
            },
            {
                "name": "ACH02",
                "displayName": "Master",
                "description": "Complete all levels",
                "icon": "ach02.png",
                "hidden": 1
            }
        ]
        """;

        var defs = AchievementMetadata.ParseDefinitions(json);

        Assert.Equal(2, defs.Count);
        Assert.Equal("ACH01", defs[0].Name);
        Assert.Equal("ACH02", defs[1].Name);
    }

    [Fact]
    public void ParseDefinitions_EmptyArray_ReturnsEmpty()
    {
        var defs = AchievementMetadata.ParseDefinitions("[]");
        Assert.Empty(defs);
    }

    [Fact]
    public void ParseDefinitions_AphelionStyleStringQuotedHidden_DoesNotThrow()
    {
        // Aphelion ships achievements.json with `"hidden": "1"` (string) instead of integer.
        // We don't consume the field, but earlier versions declared it as int and threw on parse.
        var json = """
        [
            {"name": "ACH01", "displayName": "Augmented Reality", "hidden": "1", "icon": "ach.jpg"}
        ]
        """;

        var defs = AchievementMetadata.ParseDefinitions(json);

        Assert.Single(defs);
        Assert.Equal("ACH01", defs[0].Name);
        Assert.Equal("ach.jpg", defs[0].Icon);
    }

    // --- CollectLanguages tests ---

    [Fact]
    public void CollectLanguages_UnionsKeysAcrossBothTextFields()
    {
        var json = """
        [
            {"name": "A", "displayName": {"english": "One", "german": "Eins"}, "description": {"english": "d", "french": "f"}},
            {"name": "B", "displayName": {"english": "Two", "russian": "Два"}}
        ]
        """;

        var languages = AchievementMetadata.CollectLanguages(AchievementMetadata.ParseDefinitions(json));

        Assert.Equal(new[] { "english", "french", "german", "russian" }, languages.OrderBy(l => l).ToArray());
    }

    [Fact]
    public void CollectLanguages_SingleLanguageSchema_ReturnsNothing()
    {
        // Plain-string text is single-language: there is nothing for the dialog to offer a choice of.
        var json = """[{"name": "A", "displayName": "One", "description": "d"}]""";

        Assert.Empty(AchievementMetadata.CollectLanguages(AchievementMetadata.ParseDefinitions(json)));
    }

    [Fact]
    public void CollectLanguages_IgnoresNonStringValues()
    {
        // Only a string value is text the overlay can display, so a stray nested object is not a language.
        var json = """[{"name": "A", "displayName": {"english": "One", "meta": {"x": 1}}}]""";

        Assert.Equal(new[] { "english" }, AchievementMetadata.CollectLanguages(AchievementMetadata.ParseDefinitions(json)).ToArray());
    }

    [Fact]
    public void CollectLanguages_ReadsSelfDescribingUnlockFile()
    {
        // A game tracked through a non-GBE emulator has no schema, so the unlock file is the only
        // record of which languages it can display.
        var json = """
        {
            "ACH01": {"earned": 1, "displayName": {"english": "One", "german": "Eins"}, "description": {"french": "d"}},
            "ACH02": {"earned": 0, "displayName": {"english": "Two", "russian": "Два"}}
        }
        """;

        var languages = AchievementMetadata.CollectLanguages(AchievementMetadata.ParseUnlockStates(json).Values);

        Assert.Equal(new[] { "english", "french", "german", "russian" }, languages.OrderBy(l => l).ToArray());
    }

    [Fact]
    public void CollectLanguages_PlainGbeUnlockFile_ReturnsNothing()
    {
        // GBE's own file carries no display text at all, so it has no say in the language list.
        var json = """{"ACH01": {"earned": true, "earned_time": 1774855788}}""";

        Assert.Empty(AchievementMetadata.CollectLanguages(AchievementMetadata.ParseUnlockStates(json).Values));
    }

    [Fact]
    public void CollectLanguages_ExcludesSteamLocalizationToken()
    {
        // Real schemas (e.g. Red Dead Redemption) carry Steam's token beside the languages. Offering
        // it as a choice would put "NEW_ACHIEVEMENT_1_0_NAME" on screen as the achievement's name.
        var json = """[{"name": "A", "displayName": {"token": "NEW_ACHIEVEMENT_1_0_NAME", "english": "One"}}]""";

        Assert.Equal(new[] { "english" }, AchievementMetadata.CollectLanguages(AchievementMetadata.ParseDefinitions(json)).ToArray());
    }

    // --- GetDisplayText tests ---

    [Fact]
    public void GetDisplayText_LanguageKeyDiffersInCase_StillMatches()
    {
        // One game ships "LATAM", another "latam", and a single config value has to serve both.
        var json = """[{"name": "A", "displayName": {"english": "One", "LATAM": "Uno"}}]""";
        var defs = AchievementMetadata.ParseDefinitions(json);

        Assert.Equal("Uno", AchievementMetadata.GetDisplayText(defs[0].DisplayName, "latam"));
    }

    [Fact]
    public void GetDisplayText_PlainString_ReturnsString()
    {
        var element = JsonSerializer.SerializeToElement("First Blood");
        var text = AchievementMetadata.GetDisplayText(element, "english");
        Assert.Equal("First Blood", text);
    }

    [Fact]
    public void GetDisplayText_MultiLanguage_ReturnsRequestedLanguage()
    {
        var obj = new { english = "First Blood", german = "Erstes Blut", french = "Premier Sang" };
        var element = JsonSerializer.SerializeToElement(obj);
        var text = AchievementMetadata.GetDisplayText(element, "german");
        Assert.Equal("Erstes Blut", text);
    }

    [Fact]
    public void GetDisplayText_MultiLanguage_FallsBackToEnglish()
    {
        var obj = new { english = "First Blood", german = "Erstes Blut" };
        var element = JsonSerializer.SerializeToElement(obj);
        var text = AchievementMetadata.GetDisplayText(element, "spanish");
        Assert.Equal("First Blood", text);
    }

    [Fact]
    public void GetDisplayText_MultiLanguage_FallsBackToFirstAvailable()
    {
        var obj = new { german = "Erstes Blut", french = "Premier Sang" };
        var element = JsonSerializer.SerializeToElement(obj);
        var text = AchievementMetadata.GetDisplayText(element, "spanish");
        // No english, no spanish — falls back to first available
        Assert.NotEmpty(text);
        // Should be one of the available values
        Assert.True(text == "Erstes Blut" || text == "Premier Sang");
    }

    [Fact]
    public void GetDisplayText_NullElement_ReturnsEmpty()
    {
        var text = AchievementMetadata.GetDisplayText(null, "english");
        Assert.Equal("", text);
    }

    [Fact]
    public void GetDisplayText_RequestedLanguageIsEnglish_ReturnsEnglish()
    {
        var obj = new { english = "First Blood", german = "Erstes Blut" };
        var element = JsonSerializer.SerializeToElement(obj);
        var text = AchievementMetadata.GetDisplayText(element, "english");
        Assert.Equal("First Blood", text);
    }

    // --- FindDefinition tests ---

    [Fact]
    public void FindDefinition_MatchByName_ReturnsDefinition()
    {
        var json = """
        [
            {"name": "ACH01", "displayName": "First"},
            {"name": "ACH02", "displayName": "Second"}
        ]
        """;
        var defs = AchievementMetadata.ParseDefinitions(json);

        var found = AchievementMetadata.FindDefinition(defs, "ACH02");
        Assert.NotNull(found);
        Assert.Equal("ACH02", found!.Name);
    }

    [Fact]
    public void FindDefinition_CaseInsensitive_ReturnsDefinition()
    {
        var json = """[{"name": "ACH01", "displayName": "First"}]""";
        var defs = AchievementMetadata.ParseDefinitions(json);

        var found = AchievementMetadata.FindDefinition(defs, "ach01");
        Assert.NotNull(found);
    }

    [Fact]
    public void FindDefinition_NotFound_ReturnsNull()
    {
        var json = """[{"name": "ACH01", "displayName": "First"}]""";
        var defs = AchievementMetadata.ParseDefinitions(json);

        var found = AchievementMetadata.FindDefinition(defs, "MISSING");
        Assert.Null(found);
    }

    // --- ResolveIconPath tests ---

    [Fact]
    public void ResolveIconPath_ExactMatch_ReturnsPath()
    {
        var imgDir = Path.Combine(_tempDir, "img");
        Directory.CreateDirectory(imgDir);
        var iconPath = Path.Combine(imgDir, "ach01.png");
        File.WriteAllText(iconPath, "fake image");

        var def = new AchievementDefinition { Name = "ACH01", Icon = "img/ach01.png" };
        var result = AchievementMetadata.ResolveIconPath(def, _tempDir);

        Assert.Equal(iconPath, result);
    }

    [Fact]
    public void ResolveIconPath_WithoutExtension_FindsWithExtension()
    {
        var imgDir = Path.Combine(_tempDir, "img");
        Directory.CreateDirectory(imgDir);
        var iconPath = Path.Combine(imgDir, "ach01.jpg");
        File.WriteAllText(iconPath, "fake image");

        var def = new AchievementDefinition { Name = "ACH01", Icon = "img/ach01" };
        var result = AchievementMetadata.ResolveIconPath(def, _tempDir);

        Assert.Equal(iconPath, result);
    }

    [Fact]
    public void ResolveIconPath_BareFilename_FallsBackToAchievementImagesDir()
    {
        // Configs from other tools (generate_emu_config, Aphelion) store a bare filename
        // with no subfolder. GBE resolves these against achievement_images/ — so must we.
        var imgDir = Path.Combine(_tempDir, "achievement_images");
        Directory.CreateDirectory(imgDir);
        var iconPath = Path.Combine(imgDir, "ach01.jpg");
        File.WriteAllText(iconPath, "fake image");

        var def = new AchievementDefinition { Name = "ACH01", Icon = "ach01.jpg" };
        var result = AchievementMetadata.ResolveIconPath(def, _tempDir);

        Assert.Equal(iconPath, result);
    }

    [Fact]
    public void ResolveIconPath_BareFilenameWithoutExtension_FallsBackToAchievementImagesDir()
    {
        var imgDir = Path.Combine(_tempDir, "achievement_images");
        Directory.CreateDirectory(imgDir);
        var iconPath = Path.Combine(imgDir, "ach01.png");
        File.WriteAllText(iconPath, "fake image");

        var def = new AchievementDefinition { Name = "ACH01", Icon = "ach01" };
        var result = AchievementMetadata.ResolveIconPath(def, _tempDir);

        Assert.Equal(iconPath, result);
    }

    [Fact]
    public void ResolveIconPath_VerbatimPathPreferredOverFallback()
    {
        // A file that exists at the literal path must win over the achievement_images/ fallback.
        var iconPath = Path.Combine(_tempDir, "ach01.jpg");
        File.WriteAllText(iconPath, "verbatim");

        var imgDir = Path.Combine(_tempDir, "achievement_images");
        Directory.CreateDirectory(imgDir);
        File.WriteAllText(Path.Combine(imgDir, "ach01.jpg"), "fallback");

        var def = new AchievementDefinition { Name = "ACH01", Icon = "ach01.jpg" };
        var result = AchievementMetadata.ResolveIconPath(def, _tempDir);

        Assert.Equal(iconPath, result);
    }

    [Fact]
    public void ResolveIconPath_FileNotFound_ReturnsNull()
    {
        var def = new AchievementDefinition { Name = "ACH01", Icon = "img/ach01.png" };
        var result = AchievementMetadata.ResolveIconPath(def, _tempDir);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveIconPath_NullIcon_ReturnsNull()
    {
        var def = new AchievementDefinition { Name = "ACH01", Icon = null };
        var result = AchievementMetadata.ResolveIconPath(def, _tempDir);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveIconPath_EmptyIcon_ReturnsNull()
    {
        var def = new AchievementDefinition { Name = "ACH01", Icon = "" };
        var result = AchievementMetadata.ResolveIconPath(def, _tempDir);
        Assert.Null(result);
    }

    // --- Integration: parse definitions and resolve display text ---

    [Fact]
    public void Integration_ParseAndResolveDisplayText()
    {
        var json = """
        [
            {
                "name": "ACH01",
                "displayName": {"english": "First Blood", "german": "Erstes Blut"},
                "description": {"english": "Get your first kill", "german": "Erster Abschuss"},
                "icon": "ach01.png",
                "hidden": 0
            }
        ]
        """;

        var defs = AchievementMetadata.ParseDefinitions(json);
        var def = AchievementMetadata.FindDefinition(defs, "ACH01");

        Assert.NotNull(def);
        Assert.Equal("First Blood", AchievementMetadata.GetDisplayText(def!.DisplayName, "english"));
        Assert.Equal("Erstes Blut", AchievementMetadata.GetDisplayText(def.DisplayName, "german"));
        Assert.Equal("Get your first kill", AchievementMetadata.GetDisplayText(def.Description, "english"));
    }

    // --- Self-describing unlock files (Goldberg Uplay R2 emulator, issue #5) ---

    /// <summary>The exact payload shape from issue #5, locked and unlocked entries.</summary>
    private const string UplayJson = """
    {
      "AFOP_Ach_7": {
        "earned": 0,
        "description": "Complete the quest Becoming.",
        "displayName": "First Strike"
      },
      "AFOP_Ach_8": {
        "earned": 1,
        "earned_time": 1785988975,
        "description": "Reach the Hometree.",
        "displayName": "Homecoming"
      }
    }
    """;

    [Fact]
    public void ParseUnlockStates_UplayFormat_ParsesNumericEarned()
    {
        var states = AchievementMetadata.ParseUnlockStates(UplayJson);

        Assert.Equal(2, states.Count);
        Assert.False(states["AFOP_Ach_7"].Earned);
        Assert.Equal(0, states["AFOP_Ach_7"].EarnedTime);
        Assert.True(states["AFOP_Ach_8"].Earned);
        Assert.Equal(1785988975L, states["AFOP_Ach_8"].EarnedTime);
    }

    [Fact]
    public void ParseUnlockStates_GbeFormat_StillParses()
    {
        var states = AchievementMetadata.ParseUnlockStates(
            """{"ACH01": {"earned": true, "earned_time": 1774855788}, "ACH02": {"earned": false, "earned_time": 0}}""");

        Assert.True(states["ACH01"].Earned);
        Assert.Equal(1774855788L, states["ACH01"].EarnedTime);
        Assert.False(states["ACH02"].Earned);
    }

    [Fact]
    public void ParseUnlockStates_MixedBoolAndNumber_ParsesBoth()
    {
        var states = AchievementMetadata.ParseUnlockStates(
            """{"A": {"earned": true, "earned_time": 1}, "B": {"earned": 1, "earned_time": 2}}""");

        Assert.True(states["A"].Earned);
        Assert.True(states["B"].Earned);
    }

    [Theory]
    [InlineData("\"true\"", true)]
    [InlineData("\"1\"", true)]
    [InlineData("\"false\"", false)]
    [InlineData("\"0\"", false)]
    [InlineData("null", false)]
    [InlineData("2", true)]
    public void ParseUnlockStates_TolerantEarnedValues(string earned, bool expected)
    {
        var states = AchievementMetadata.ParseUnlockStates("{\"A\": {\"earned\": " + earned + "}}");

        Assert.Equal(expected, states["A"].Earned);
    }

    [Fact]
    public void ParseUnlockStates_QuotedEarnedTime_Parses()
    {
        var states = AchievementMetadata.ParseUnlockStates(
            """{"A": {"earned": 1, "earned_time": "1785988975"}}""");

        Assert.Equal(1785988975L, states["A"].EarnedTime);
    }

    [Fact]
    public void ParseUnlockStates_OneUnreadableEntry_KeepsTheRest()
    {
        var states = AchievementMetadata.ParseUnlockStates(
            """{"good": {"earned": 1, "earned_time": 5}, "bad": {"earned": "banana"}, "alsoGood": {"earned": 0}}""");

        Assert.Equal(2, states.Count);
        Assert.True(states["good"].Earned);
        Assert.False(states["alsoGood"].Earned);
        Assert.DoesNotContain("bad", states.Keys);
    }

    [Fact]
    public void ParseUnlockStates_MalformedDocument_Throws()
    {
        Assert.Throws<JsonException>(() => AchievementMetadata.ParseUnlockStates("not valid json {{{"));
    }

    [Fact]
    public void IsSelfDescribing_UplayFile_True()
    {
        Assert.True(AchievementMetadata.IsSelfDescribing(AchievementMetadata.ParseUnlockStates(UplayJson)));
    }

    [Fact]
    public void IsSelfDescribing_GbeFile_False()
    {
        var states = AchievementMetadata.ParseUnlockStates("""{"ACH01": {"earned": true, "earned_time": 1}}""");

        Assert.False(AchievementMetadata.IsSelfDescribing(states));
    }

    [Fact]
    public void HasInlineText_EmptyStrings_False()
    {
        var states = AchievementMetadata.ParseUnlockStates(
            """{"A": {"earned": 1, "displayName": "", "description": ""}}""");

        Assert.False(AchievementMetadata.HasInlineText(states["A"]));
    }

    [Fact]
    public void ResolvePreferringSchema_NoSchema_UsesInlineText()
    {
        var states = AchievementMetadata.ParseUnlockStates(UplayJson);

        var resolved = Inline(states["AFOP_Ach_8"], "AFOP_Ach_8");

        Assert.NotNull(resolved);
        Assert.Equal("Homecoming", resolved.DisplayName);
        Assert.Equal("Reach the Hometree.", resolved.Description);
        Assert.Null(resolved.IconPath);
    }

    [Fact]
    public void ResolvePreferringSchema_InlineDescriptionOnly_FallsBackToAchievementName()
    {
        var states = AchievementMetadata.ParseUnlockStates("""{"A": {"earned": 1, "description": "Do the thing"}}""");

        var resolved = Inline(states["A"], "A");

        Assert.NotNull(resolved);
        Assert.Equal("A", resolved.DisplayName);
        Assert.Equal("Do the thing", resolved.Description);
    }

    [Fact]
    public void ResolvePreferringSchema_MultiLanguageInlineText_HonoursLanguage()
    {
        var states = AchievementMetadata.ParseUnlockStates(
            """{"A": {"earned": 1, "displayName": {"english": "First Strike", "german": "Erstschlag"}}}""");

        Assert.Equal("Erstschlag", AchievementMetadata.ResolvePreferringSchema(
            states["A"], definitions: null, "", "A", "german")!.DisplayName);
    }

    [Fact]
    public void ResolvePreferringSchema_NoSchemaAndNoInlineText_ReturnsNull()
    {
        var states = AchievementMetadata.ParseUnlockStates("""{"A": {"earned": true, "earned_time": 1}}""");

        Assert.Null(Inline(states["A"], "A"));
        Assert.Null(Inline(null, "A"));
    }

    /// <summary>Resolves with no schema at all, i.e. from the unlock entry's own text.</summary>
    private static ResolvedAchievement? Inline(AchievementUnlockState? state, string achievementName)
        => AchievementMetadata.ResolvePreferringSchema(state, definitions: null, "", achievementName, "english");

    [Fact]
    public void ResolvePreferringSchema_SchemaDefinesAchievement_WinsOverInlineText()
    {
        // A self-describing emulator ships no icons, so a configured game's schema must win.
        File.WriteAllBytes(Path.Combine(_tempDir, "afop8.jpg"), new byte[] { 0xFF, 0xD8 });
        var states = AchievementMetadata.ParseUnlockStates(UplayJson);
        var definitions = AchievementMetadata.ParseDefinitions(
            """[{"name": "AFOP_Ach_8", "displayName": "Schema Name", "description": "Schema description.", "icon": "afop8.jpg"}]""");

        var resolved = AchievementMetadata.ResolvePreferringSchema(
            states["AFOP_Ach_8"], definitions, _tempDir, "AFOP_Ach_8", "english");

        Assert.NotNull(resolved);
        Assert.Equal("Schema Name", resolved.DisplayName);
        Assert.Equal("Schema description.", resolved.Description);
        Assert.Equal(Path.Combine(_tempDir, "afop8.jpg"), resolved.IconPath);
    }

    [Fact]
    public void ResolvePreferringSchema_AchievementAbsentFromSchema_FallsBackToInlineText()
    {
        // The appid-collision case: a schema cached under a colliding id defines other achievements.
        var states = AchievementMetadata.ParseUnlockStates(UplayJson);
        var definitions = AchievementMetadata.ParseDefinitions(
            """[{"name": "ACH01", "displayName": "A different game's achievement"}]""");

        var resolved = AchievementMetadata.ResolvePreferringSchema(
            states["AFOP_Ach_8"], definitions, _tempDir, "AFOP_Ach_8", "english");

        Assert.NotNull(resolved);
        Assert.Equal("Homecoming", resolved.DisplayName);
        Assert.Equal("Reach the Hometree.", resolved.Description);
        Assert.Null(resolved.IconPath);
    }

    [Fact]
    public void ResolvePreferringSchema_EmptySchemaList_ReturnsNullWithoutInlineText()
    {
        var states = AchievementMetadata.ParseUnlockStates("""{"A": {"earned": true, "earned_time": 1}}""");

        Assert.Null(AchievementMetadata.ResolvePreferringSchema(
            states["A"], new List<AchievementDefinition>(), "", "A", "english"));
    }

    [Fact]
    public void ResolvePreferringSchema_SchemaDescriptionEmpty_KeepsInlineDescription()
    {
        // Steam redacts hidden achievements' descriptions, and the Add game wizard writes them empty
        // when no Firecrawl key fills them in — the schema naming the achievement must not blank text
        // the unlock file did carry.
        var states = AchievementMetadata.ParseUnlockStates(UplayJson);
        var definitions = AchievementMetadata.ParseDefinitions(
            """[{"name": "AFOP_Ach_8", "displayName": "Schema Name", "description": ""}]""");

        var resolved = AchievementMetadata.ResolvePreferringSchema(
            states["AFOP_Ach_8"], definitions, _tempDir, "AFOP_Ach_8", "english");

        Assert.NotNull(resolved);
        Assert.Equal("Schema Name", resolved.DisplayName);
        Assert.Equal("Reach the Hometree.", resolved.Description);
    }

    [Fact]
    public void ResolvePreferringSchema_SchemaDisplayNameEmpty_KeepsInlineDisplayName()
    {
        var states = AchievementMetadata.ParseUnlockStates(UplayJson);
        var definitions = AchievementMetadata.ParseDefinitions(
            """[{"name": "AFOP_Ach_8", "description": "Schema description."}]""");

        var resolved = AchievementMetadata.ResolvePreferringSchema(
            states["AFOP_Ach_8"], definitions, _tempDir, "AFOP_Ach_8", "english");

        Assert.NotNull(resolved);
        Assert.Equal("Homecoming", resolved.DisplayName);
        Assert.Equal("Schema description.", resolved.Description);
    }

    // --- Resolve: how hard the schema is looked for ---

    /// <summary>Writes a GBE-shaped config for <paramref name="appId"/> under the games root.</summary>
    private string CreateConfiguredGame(string appId, string achievementsJson)
    {
        var gamesDir = Path.Combine(_tempDir, "games");
        var gameDir = Path.Combine(gamesDir, "Game" + appId);
        var settingsDir = Path.Combine(gameDir, "steam_settings");
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(Path.Combine(gameDir, "steam_appid.txt"), appId);
        File.WriteAllText(Path.Combine(settingsDir, "achievements.json"), achievementsJson);
        return gamesDir;
    }

    private const string SchemaJson = """[{"name": "AFOP_Ach_8", "displayName": "Schema Name", "description": "Schema description."}]""";

    [Fact]
    public void Resolve_InlineText_ConfigAddedAfterScan_IsPickedUp()
    {
        var gamesDir = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(gamesDir);
        var cache = new GameCache(new[] { gamesDir });
        cache.ScanAll();

        CreateConfiguredGame("2840770", SchemaJson);
        var state = AchievementMetadata.ParseUnlockStates(UplayJson)["AFOP_Ach_8"];

        var resolved = AchievementMetadata.Resolve(cache, "2840770", "AFOP_Ach_8", state, "english");

        Assert.Equal("Schema Name", resolved!.DisplayName);
    }

    [Fact]
    public void Resolve_InlineText_RescansOncePerAppId()
    {
        var gamesDir = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(gamesDir);
        var cache = new GameCache(new[] { gamesDir });
        cache.ScanAll();
        var state = AchievementMetadata.ParseUnlockStates(UplayJson)["AFOP_Ach_8"];

        // Spends this appid's one rescan while nothing is configured; the inline text carries it.
        Assert.Equal("Homecoming", AchievementMetadata.Resolve(cache, "2840770", "AFOP_Ach_8", state, "english")!.DisplayName);

        CreateConfiguredGame("2840770", SchemaJson);

        // No second rescan: a notification already works, so the schema is not chased per unlock.
        Assert.Equal("Homecoming", AchievementMetadata.Resolve(cache, "2840770", "AFOP_Ach_8", state, "english")!.DisplayName);
    }

    [Fact]
    public void Resolve_NoInlineText_RescansOnEveryMiss()
    {
        var gamesDir = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(gamesDir);
        var cache = new GameCache(new[] { gamesDir });
        cache.ScanAll();
        var state = AchievementMetadata.ParseUnlockStates("""{"AFOP_Ach_8": {"earned": true, "earned_time": 1}}""")["AFOP_Ach_8"];

        // Without inline text a missing schema means no notification at all, so every miss rescans.
        Assert.Null(AchievementMetadata.Resolve(cache, "2840770", "AFOP_Ach_8", state, "english"));

        CreateConfiguredGame("2840770", SchemaJson);

        Assert.Equal("Schema Name", AchievementMetadata.Resolve(cache, "2840770", "AFOP_Ach_8", state, "english")!.DisplayName);
    }
}
