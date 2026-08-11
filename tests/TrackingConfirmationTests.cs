using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class TrackingConfirmationTests
{
    [Fact]
    public void ShouldNotify_KnownUnshownZeroEarned_ReturnsTrue()
    {
        Assert.True(TrackingConfirmation.ShouldNotify(gameKnown: true, alreadyShown: false, earnedCount: 0));
    }

    [Fact]
    public void ShouldNotify_UnknownGame_ReturnsFalse()
    {
        Assert.False(TrackingConfirmation.ShouldNotify(gameKnown: false, alreadyShown: false, earnedCount: 0));
    }

    [Fact]
    public void ShouldNotify_AlreadyShown_ReturnsFalse()
    {
        Assert.False(TrackingConfirmation.ShouldNotify(gameKnown: true, alreadyShown: true, earnedCount: 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void ShouldNotify_HasEarnedAchievements_ReturnsFalse(int earnedCount)
    {
        Assert.False(TrackingConfirmation.ShouldNotify(gameKnown: true, alreadyShown: false, earnedCount));
    }

    [Fact]
    public void ShouldNotify_UnknownEarnedCount_ReturnsFalse()
    {
        // The unlock file exists but could not be read — that is not "nothing earned yet".
        Assert.False(TrackingConfirmation.ShouldNotify(gameKnown: true, alreadyShown: false, earnedCount: null));
    }
}
