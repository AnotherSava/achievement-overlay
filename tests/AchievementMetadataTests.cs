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
        Assert.Equal(0, defs[0].Hidden);
        Assert.Equal(1, defs[1].Hidden);
    }

    [Fact]
    public void ParseDefinitions_EmptyArray_ReturnsEmpty()
    {
        var defs = AchievementMetadata.ParseDefinitions("[]");
        Assert.Empty(defs);
    }

    // --- GetDisplayText tests ---

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
}
