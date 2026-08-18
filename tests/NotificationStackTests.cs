using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

/// <summary>
/// The stacking rule shared by the recent achievements panel (production, Ctrl+Shift+H) and the
/// settings window's Show me preview. Real unlocks never stack — NotificationQueue dispatches them
/// one at a time — so the panel is what these guard.
/// </summary>
public class NotificationStackTests
{
    [Fact]
    public void SlotHeight_IsThePopupPlusTheGap()
    {
        Assert.Equal(95 + NotificationWindow.StackGap, NotificationWindow.SlotHeight(95));
    }

    [Fact]
    public void EqualPopups_StackAtAConstantPitch()
    {
        // Equal popups must be evenly spaced however many there are. Asserted as a pitch rather than
        // as fixed coordinates: the measured height is fractional, so any hardcoded triple would be
        // inventing precision the real windows don't have.
        const double bottomTop = 1294;
        const double height = 95.33;

        var slot = NotificationWindow.SlotHeight(height);
        var tops = new[] { bottomTop, bottomTop - slot, bottomTop - 2 * slot };

        Assert.Equal(slot, tops[0] - tops[1], 6);
        Assert.Equal(slot, tops[1] - tops[2], 6);
    }

    [Fact]
    public void StackedPopups_NeverOverlap()
    {
        // Each popup's bottom must clear the next one's top, whatever the heights — the property the
        // panel's running bottom edge and the preview's shift both have to preserve.
        double[] heights = { 95, 130, 71, 210 };
        var top = 1000.0;

        for (var i = 0; i < heights.Length; i++)
        {
            var bottom = top + heights[i];
            var nextTop = top - NotificationWindow.SlotHeight(heights[i]);
            Assert.True(nextTop + heights[i] < top, $"popup {i} overlaps the one below it");
            Assert.Equal(NotificationWindow.StackGap, top - (nextTop + heights[i]), 6);
            top = nextTop;
            Assert.True(bottom > top);
        }
    }
}
