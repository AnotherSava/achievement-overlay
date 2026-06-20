using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public class SteamWebApiTests
{
    [Fact]
    public void ParseSchema_ReadsAchievements()
    {
        var json = """
        {
          "game": {
            "gameName": "Test",
            "availableGameStats": {
              "achievements": [
                {"name": "A1", "defaultvalue": 0, "displayName": "First", "hidden": 0, "description": "Do it", "icon": "https://i/1.jpg", "icongray": "https://i/1g.jpg"},
                {"name": "A2", "displayName": "Second", "hidden": 1, "icon": "https://i/2.jpg", "icongray": "https://i/2g.jpg"}
              ]
            }
          }
        }
        """;

        var result = SteamWebApi.ParseSchema(json);
        Assert.Equal(2, result.Count);
        Assert.Equal("A1", result[0].Name);
        Assert.Equal("First", result[0].DisplayName);
        Assert.Equal(0, result[0].Hidden);
        Assert.Equal("Do it", result[0].Description);
        Assert.Equal("https://i/1g.jpg", result[0].IconGray);

        Assert.Equal(1, result[1].Hidden);
        Assert.Equal("", result[1].Description); // missing → empty
    }

    [Fact]
    public void ParseSchema_NoAchievementsBlock_ReturnsEmpty()
    {
        Assert.Empty(SteamWebApi.ParseSchema("""{"game": {"gameName": "X", "availableGameStats": {}}}"""));
    }

    [Fact]
    public void ParseSchema_UnauthorizedEmptyResponse_ReturnsEmpty()
    {
        Assert.Empty(SteamWebApi.ParseSchema("{}"));
    }
}
