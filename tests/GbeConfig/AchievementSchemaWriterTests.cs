using System.Text.Json;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public class AchievementSchemaWriterTests
{
    private static SteamSchemaAchievement Ach(string name, int hidden = 0, string desc = "desc") =>
        new(name, $"Display {name}", hidden, desc, $"https://x/{name}.jpg", $"https://x/{name}_g.jpg");

    [Fact]
    public void Build_MapsFieldsAndIconPaths()
    {
        var result = AchievementSchemaWriter.Build(new[] { Ach("ACH_1") });
        var a = Assert.Single(result);

        Assert.Equal("ACH_1", a.Name);
        Assert.Equal("Display ACH_1", a.DisplayName);
        Assert.Equal("desc", a.Description);
        Assert.Equal("0", a.Hidden);
        Assert.Equal("achievement_images/ACH_1.jpg", a.Icon);
        Assert.Equal("achievement_images/ACH_1_gray.jpg", a.IconGray);
    }

    [Fact]
    public void Build_HiddenFlagSerializedAsString()
    {
        var result = AchievementSchemaWriter.Build(new[] { Ach("H", hidden: 1) });
        Assert.Equal("1", result[0].Hidden);
    }

    [Fact]
    public void ApplyDescriptions_PatchesByApiName()
    {
        var list = AchievementSchemaWriter.Build(new[] { Ach("H", hidden: 1, desc: "No description available") });
        var patched = AchievementSchemaWriter.ApplyDescriptions(list,
            new Dictionary<string, string> { ["H"] = "The real hidden text" });

        Assert.Equal(1, patched);
        Assert.Equal("The real hidden text", list[0].Description);
        Assert.Equal("1", list[0].Hidden); // stays hidden
    }

    [Fact]
    public void ApplyDescriptions_NoMatch_LeavesUnchanged()
    {
        var list = AchievementSchemaWriter.Build(new[] { Ach("A", desc: "orig") });
        var patched = AchievementSchemaWriter.ApplyDescriptions(list,
            new Dictionary<string, string> { ["OTHER"] = "x" });

        Assert.Equal(0, patched);
        Assert.Equal("orig", list[0].Description);
    }

    [Fact]
    public void Serialize_ProducesGbeShapeWithStringHidden()
    {
        var json = AchievementSchemaWriter.Serialize(AchievementSchemaWriter.Build(new[] { Ach("A", hidden: 1) }));
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement[0];

        Assert.Equal(JsonValueKind.String, entry.GetProperty("hidden").ValueKind);
        Assert.Equal("1", entry.GetProperty("hidden").GetString());
        Assert.Equal("achievement_images/A.jpg", entry.GetProperty("icon").GetString());
        Assert.Equal("achievement_images/A_gray.jpg", entry.GetProperty("icongray").GetString());
    }

    [Fact]
    public void Serialize_DoesNotEscapeNonAscii()
    {
        var ach = new SteamSchemaAchievement("A", "Café — déjà", 0, "Über naïve", "i", "g");
        var json = AchievementSchemaWriter.Serialize(AchievementSchemaWriter.Build(new[] { ach }));
        Assert.Contains("Café — déjà", json);
        Assert.Contains("Über naïve", json);
    }
}
