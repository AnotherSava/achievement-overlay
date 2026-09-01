using System.Windows;

namespace AchievementOverlay;

/// <summary>Where a popup rests, and how far it travels getting there.</summary>
public readonly record struct PopupPlacement(double Left, double Top, double SlideOffset);

/// <summary>
/// Every "which edge" decision in one place: the horizontal alignment, the edge inset, the resting
/// position, the direction a stack grows and the direction a popup slides in from. Pure — it takes a
/// rectangle and returns numbers, with no <see cref="Window"/> and no screen query — so the unlock
/// popup, the recent achievements panel and the settings preview all read the same arithmetic and
/// cannot drift apart, and all of it is unit-tested without a dispatcher.
/// </summary>
public static class NotificationPlacement
{
    /// <summary>Edge inset, as a share of the display's shorter side.</summary>
    private const double MarginFraction = 0.02;

    /// <summary>How far a popup travels on its way in, as a share of the display's height.</summary>
    private const double SlideDistanceFraction = 0.015;

    /// <summary>Vertical gap between stacked popups.</summary>
    public const double StackGap = 6;

    public static bool IsTop(NotificationAnchor anchor) =>
        anchor is NotificationAnchor.TopLeft or NotificationAnchor.TopCenter or NotificationAnchor.TopRight;

    private static double Margin(Rect area) => Math.Min(area.Width, area.Height) * MarginFraction;

    /// <summary>
    /// How far, and which way, a popup moves as it appears — one <em>signed</em> number, because the
    /// resting position and the animation are the same decision. A popup emerges flush with its
    /// anchored edge and settles inward, so the offset is positive at a bottom anchor and negative at
    /// a top one, and flipping one of the two without the other is not expressible.
    /// </summary>
    public static double SlideOffset(NotificationAnchor anchor, Rect area) =>
        IsTop(anchor) ? -(area.Height * SlideDistanceFraction) : area.Height * SlideDistanceFraction;

    public static double LeftFor(NotificationAnchor anchor, Rect area, double width) => anchor switch
    {
        NotificationAnchor.TopLeft or NotificationAnchor.BottomLeft => area.Left + Margin(area),
        NotificationAnchor.TopCenter or NotificationAnchor.BottomCenter => area.Left + (area.Width - width) / 2,
        _ => area.Right - width - Margin(area)
    };

    /// <summary>The anchored edge itself, inset by the margin — where a popup sits flush against it.</summary>
    public static double FlushEdge(NotificationAnchor anchor, Rect area) =>
        IsTop(anchor) ? area.Top + Margin(area) : area.Bottom - Margin(area);

    /// <summary>
    /// Turns a slot's near edge into a window Top. At a top anchor the edge already is the top; at a
    /// bottom anchor the popup hangs above it by its own height.
    /// </summary>
    public static double TopFor(NotificationAnchor anchor, double edge, double height) =>
        IsTop(anchor) ? edge : edge - height;

    /// <summary>Moves the running edge past a popup of the given height, gap included.</summary>
    public static double Advance(NotificationAnchor anchor, double edge, double height) =>
        IsTop(anchor) ? edge + height + StackGap : edge - height - StackGap;

    /// <summary>The inverse of <see cref="TopFor"/>: the near edge of a popup already placed.</summary>
    public static double EdgeOf(NotificationAnchor anchor, double top, double height) =>
        IsTop(anchor) ? top : top + height;

    /// <summary>
    /// Slide distance for a popup joining a stack, which travels a whole slot rather than the short
    /// hop <see cref="SlideOffset"/> gives — and away from the anchor, so it appears to be pushed up
    /// (or down) out of the one below it.
    /// </summary>
    public static double StackSlideOffset(NotificationAnchor anchor, double height) =>
        IsTop(anchor) ? -(height + StackGap) : height + StackGap;

    /// <summary>
    /// Where a single popup rests: aligned horizontally, and one slide distance in from the anchored
    /// edge. The one expression of an unlock popup's position.
    /// </summary>
    public static PopupPlacement Place(NotificationAnchor anchor, Rect area, double width, double height)
    {
        var slide = SlideOffset(anchor, area);
        return new PopupPlacement(LeftFor(anchor, area, width), TopFor(anchor, FlushEdge(anchor, area) - slide, height), slide);
    }
}
