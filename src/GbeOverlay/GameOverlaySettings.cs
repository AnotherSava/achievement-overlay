namespace AchievementOverlay.GbeOverlay;

/// <summary>
/// A game's own overlay settings, resolved against disk: every path here exists, so the popup and the
/// sound player can use them without probing again. Null means the game said nothing about that one
/// and the app's own setting stands.
/// </summary>
/// <param name="SourceDescription">
/// The folders the values were folded from, for the log. <see cref="GameCache"/> keys on appid and
/// the last scan wins, so when two installs claim one appid an unlock can be answered with the wrong
/// game's settings — the log is then the only thing that says which folders were read.
/// </param>
public sealed record GameOverlaySettings(
    double? AchievementDurationSeconds,
    string? SoundFilePath,
    string? FontFilePath,
    string SourceDescription)
{
    public bool IsEmpty => AchievementDurationSeconds == null && SoundFilePath == null && FontFilePath == null;

    public override string ToString()
    {
        var parts = new List<string>();
        if (AchievementDurationSeconds != null) parts.Add($"duration={AchievementDurationSeconds}s");
        if (SoundFilePath != null) parts.Add($"sound='{SoundFilePath}'");
        if (FontFilePath != null) parts.Add($"font='{FontFilePath}'");
        return $"{string.Join(", ", parts)} (from '{SourceDescription}')";
    }
}
