namespace AchievementOverlay;

/// <summary>
/// One game the app knows of, as the Games window and Report a problem list them. Games with no
/// <see cref="GameInfo"/> are listed too: a game tracked through a self-describing unlock file has no
/// <c>steam_settings</c> at all, and a save folder for a game never set up here has none either.
/// </summary>
public sealed class GameChoice
{
    public required string AppId { get; init; }
    public GameInfo? Game { get; init; }

    /// <summary>A save folder for a game that was never set up here, marked wherever it is listed.</summary>
    public bool Unconfigured { get; init; }

    /// <summary>The game's name from <see cref="SteamAppNames.Name"/>, or null when it has none.</summary>
    public string? Name { get; init; }

    /// <summary>The order every list of games is shown in: configured games first, then the ones never set up, each group by name.</summary>
    public static int ListOrder(GameChoice a, GameChoice b) =>
        a.Unconfigured != b.Unconfigured ? a.Unconfigured.CompareTo(b.Unconfigured) : string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCultureIgnoreCase);

    public override string ToString()
    {
        var name = Name;
        var label = string.IsNullOrWhiteSpace(name) ? AppId : $"{name} ({AppId})";
        return Unconfigured ? "* " + label : label;
    }
}
