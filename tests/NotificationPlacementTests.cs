using System.Windows;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

/// <summary>
/// The placement rules shared by the unlock popup, the recent achievements panel (Ctrl+Shift+H) and
/// the settings window's Show me preview. Real unlocks never stack — NotificationQueue dispatches them
/// one at a time — so the stacking facts guard the latter two.
/// </summary>
public class NotificationPlacementTests
{
    /// <summary>A 1080p display with a docked taskbar, which is where the pinned literals come from.</summary>
    private static readonly Rect Area = new(0, 0, 1920, 1040);

    private const double Margin = 20.8;        // min(1920, 1040) × 0.02
    private const double Slide = 15.6;         // 1040 × 0.015
    private const double Width = NotificationScale.DesignWidth;

    private static readonly NotificationAnchor[] AllAnchors =
    {
        NotificationAnchor.BottomRight, NotificationAnchor.BottomCenter, NotificationAnchor.BottomLeft,
        NotificationAnchor.TopRight, NotificationAnchor.TopCenter, NotificationAnchor.TopLeft
    };

    [Fact]
    public void Place_BottomRight_ReproducesTheArithmeticItReplaced()
    {
        // The pin that makes the extraction verifiable: these are the values the old inline
        // expressions in NotificationWindow.SizeAndPosition produced, written out.
        const double height = 95;
        var placement = NotificationPlacement.Place(NotificationAnchor.BottomRight, Area, Width, height);

        Assert.Equal(1920 - Width - Margin, placement.Left, 6);
        Assert.Equal(1040 - height - Margin - Slide, placement.Top, 6);
        Assert.Equal(Slide, placement.SlideOffset, 6);
    }

    [Fact]
    public void FlushEdge_GivesTheFooterItsOwnRestingRule()
    {
        // The recent panel's footer rests at the margin with no slide term, one slide distance closer
        // to the edge than an unlock popup. Both rules have to be expressible or one surface moves.
        const double height = 40;
        var footerTop = NotificationPlacement.TopFor(
            NotificationAnchor.BottomRight,
            NotificationPlacement.FlushEdge(NotificationAnchor.BottomRight, Area),
            height);

        Assert.Equal(1040 - height - Margin, footerTop, 6);
        Assert.Equal(Slide, footerTop - NotificationPlacement.Place(NotificationAnchor.BottomRight, Area, Width, height).Top, 6);
    }

    [Theory]
    [InlineData(NotificationAnchor.TopLeft, Margin)]
    [InlineData(NotificationAnchor.BottomLeft, Margin)]
    [InlineData(NotificationAnchor.TopCenter, (1920 - Width) / 2)]
    [InlineData(NotificationAnchor.BottomCenter, (1920 - Width) / 2)]
    [InlineData(NotificationAnchor.TopRight, 1920 - Width - Margin)]
    [InlineData(NotificationAnchor.BottomRight, 1920 - Width - Margin)]
    public void LeftFor_AlignsLeftCentredAndRight(NotificationAnchor anchor, double expected)
    {
        Assert.Equal(expected, NotificationPlacement.LeftFor(anchor, Area, Width), 6);
    }

    [Fact]
    public void Place_KeepsThePopupInsideTheArea()
    {
        const double height = 95;
        foreach (var anchor in AllAnchors)
        {
            var placement = NotificationPlacement.Place(anchor, Area, Width, height);
            Assert.True(placement.Left >= Area.Left, $"{anchor} overflows the left edge");
            Assert.True(placement.Left + Width <= Area.Right, $"{anchor} overflows the right edge");
            Assert.True(placement.Top >= Area.Top, $"{anchor} overflows the top edge");
            Assert.True(placement.Top + height <= Area.Bottom, $"{anchor} overflows the bottom edge");
        }
    }

    [Fact]
    public void Place_KeepsThePopupInsideAnOffOriginArea()
    {
        // A secondary monitor to the right, with a top-docked taskbar. Nothing may be measured from
        // the origin: this is the arithmetic half of the multi-monitor risk, and needs no second
        // monitor to catch.
        var area = new Rect(1920, 40, 2560, 1400);
        const double height = 95;

        foreach (var anchor in AllAnchors)
        {
            var placement = NotificationPlacement.Place(anchor, area, Width, height);
            Assert.True(placement.Left >= area.Left, $"{anchor} overflows the left edge");
            Assert.True(placement.Left + Width <= area.Right, $"{anchor} overflows the right edge");
            Assert.True(placement.Top >= area.Top, $"{anchor} overflows the top edge");
            Assert.True(placement.Top + height <= area.Bottom, $"{anchor} overflows the bottom edge");
        }
    }

    [Fact]
    public void SlideOffset_CarriesTheDirectionAsWellAsTheDistance()
    {
        foreach (var anchor in AllAnchors)
        {
            var slide = NotificationPlacement.SlideOffset(anchor, Area);
            Assert.Equal(Slide, Math.Abs(slide), 6);
            Assert.Equal(NotificationPlacement.IsTop(anchor), slide < 0);
        }
    }

    [Fact]
    public void Place_RestsAwayFromTheAnchoredEdge()
    {
        // Guards the half-flip: moving the animation to a top anchor without moving the resting
        // position leaves the popup sitting one slide distance off the wrong side of its margin.
        const double height = 95;
        foreach (var anchor in AllAnchors)
        {
            var placement = NotificationPlacement.Place(anchor, Area, Width, height);
            var flush = NotificationPlacement.FlushEdge(anchor, Area);
            var restingEdge = NotificationPlacement.EdgeOf(anchor, placement.Top, height);

            if (NotificationPlacement.IsTop(anchor))
                Assert.True(restingEdge > flush, $"{anchor} rests above its own margin");
            else
                Assert.True(restingEdge < flush, $"{anchor} rests below its own margin");

            Assert.Equal(Slide, Math.Abs(restingEdge - flush), 6);
        }
    }

    [Fact]
    public void Stack_KeepsAConstantGapWithUnequalHeights()
    {
        // Each slot's position depends on the height of the popup *in* it. Reaching for the
        // neighbour's height instead puts 95 + 6 - 130 = -29 between two of these, an overlap — and
        // that is what a stack test using one height throughout cannot see.
        double[] heights = { 95, 130, 71, 210 };

        foreach (var anchor in AllAnchors)
        {
            var slots = Walk(anchor, heights);
            for (var i = 1; i < slots.Count; i++)
            {
                var gap = NotificationPlacement.IsTop(anchor)
                    ? slots[i].Top - slots[i - 1].Bottom
                    : slots[i - 1].Top - slots[i].Bottom;
                Assert.Equal(NotificationPlacement.StackGap, gap, 6);
            }
        }
    }

    [Fact]
    public void Stack_GrowsAwayFromTheAnchor()
    {
        double[] heights = { 95, 130, 71, 210 };

        foreach (var anchor in AllAnchors)
        {
            var slots = Walk(anchor, heights);
            for (var i = 1; i < slots.Count; i++)
            {
                if (NotificationPlacement.IsTop(anchor))
                    Assert.True(slots[i].Top > slots[i - 1].Top, $"{anchor} stacks upward from a top edge");
                else
                    Assert.True(slots[i].Top < slots[i - 1].Top, $"{anchor} stacks downward from a bottom edge");
            }
        }
    }

    [Fact]
    public void EdgeOf_InvertsTopFor()
    {
        const double height = 130;
        foreach (var anchor in AllAnchors)
        {
            const double top = 640;
            var edge = NotificationPlacement.EdgeOf(anchor, top, height);
            Assert.Equal(top, NotificationPlacement.TopFor(anchor, edge, height), 6);
        }
    }

    /// <summary>Places popups of the given heights into consecutive slots from the anchored edge.</summary>
    private static List<(double Top, double Bottom)> Walk(NotificationAnchor anchor, double[] heights)
    {
        var edge = NotificationPlacement.FlushEdge(anchor, Area);
        var slots = new List<(double Top, double Bottom)>();

        foreach (var height in heights)
        {
            var top = NotificationPlacement.TopFor(anchor, edge, height);
            slots.Add((top, top + height));
            edge = NotificationPlacement.Advance(anchor, edge, height);
        }

        return slots;
    }
}
