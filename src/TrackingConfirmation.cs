namespace AchievementOverlay;

/// <summary>
/// Pure decision logic for the synthetic "Achievement tracking configured" notification.
/// Kept separate from the IO-bound firing code so it can be unit-tested in isolation.
/// </summary>
public static class TrackingConfirmation
{
    /// <summary>Achievement name shown on the synthetic notification and its Recent-panel row.</summary>
    public const string Title = "Gearhead";

    /// <summary>Description shown alongside <see cref="Title"/>, in both places it appears.</summary>
    public static string Description(string gameName) => $"Configure achievement tracking for\n{gameName}";

    /// <summary>
    /// Returns true when the synthetic notification should fire for a game: the game must be
    /// known/configured, not already shown, and have zero earned achievements.
    /// A null <paramref name="earnedCount"/> means the count could not be determined (the unlock
    /// file exists but could not be read) and never notifies — an unknown count must not be
    /// mistaken for "nothing earned yet".
    /// </summary>
    public static bool ShouldNotify(bool gameKnown, bool alreadyShown, int? earnedCount)
        => gameKnown && !alreadyShown && earnedCount == 0;
}
