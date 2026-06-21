namespace AchievementOverlay;

/// <summary>
/// Pure decision logic for the synthetic "Achievement tracking configured" notification.
/// Kept separate from the IO-bound firing code so it can be unit-tested in isolation.
/// </summary>
public static class TrackingConfirmation
{
    /// <summary>
    /// Returns true when the synthetic notification should fire for a game: the game must be
    /// known/configured, not already shown, and have zero earned achievements.
    /// </summary>
    public static bool ShouldNotify(bool gameKnown, bool alreadyShown, int earnedCount)
        => gameKnown && !alreadyShown && earnedCount == 0;
}
