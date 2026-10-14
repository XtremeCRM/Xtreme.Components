using Windows.Foundation;
using Xtreme.Components.Scheduler.Controls;

namespace Xtreme.Components.Scheduler.Tests;

/// <summary>
/// <see cref="SchedulerToolbar.PlacePicker"/>: where the date-range picker opens and how big it can be
/// (XU-930). The presenter adds 13px of border + padding on every side of the panel and keeps 8px clear
/// of the window edge.
/// </summary>
public sealed class PickerPlacementTests
{
    private static readonly Size Min = new(280, 300);
    private static readonly Size Window = new(1400, 900);

    [Fact]
    public void OpensBelowTheButtonFromItsLeftEdgeWhenItFits()
    {
        var (origin, panel) = SchedulerToolbar.PlacePicker(new Rect(100, 40, 170, 35), Window, new Size(560, 460), Min);

        Assert.Equal(new Point(100, 79), origin);
        Assert.Equal(new Size(560, 460), panel);
    }

    [Fact]
    public void OpensAboveTheButtonWhenOnlyAboveHasRoom()
    {
        var (origin, panel) = SchedulerToolbar.PlacePicker(new Rect(100, 700, 170, 35), Window, new Size(560, 460), Min);

        Assert.Equal(700 - 4 - 486, origin.Y);
        Assert.Equal(new Size(560, 460), panel);
    }

    [Fact]
    public void ShiftsLeftRatherThanCrossTheRightEdge()
    {
        var (origin, panel) = SchedulerToolbar.PlacePicker(new Rect(1200, 40, 170, 35), Window, new Size(560, 460), Min);

        Assert.Equal(1400 - 8 - 586, origin.X);
        Assert.Equal(560, panel.Width);
    }

    [Fact]
    public void ASavedSizeBiggerThanTheWindowIsShrunkToFitWithTheTopOnScreen()
    {
        // Saved on a bigger monitor (or by another instance sharing the settings), opened in a small window.
        var small = new Size(1010, 603);
        var (origin, panel) = SchedulerToolbar.PlacePicker(new Rect(630, 427, 174, 35), small, new Size(1100, 900), Min);

        Assert.True(origin.X >= 8 && origin.Y >= 8);
        Assert.True(origin.X + panel.Width + 26 <= small.Width - 8);
        Assert.True(origin.Y + panel.Height + 26 <= small.Height - 8);
        Assert.Equal(new Point(8, 8), origin);
        Assert.Equal(new Size(1010 - 16 - 26, 603 - 16 - 26), panel);
    }

    [Fact]
    public void NeverShrinksBelowTheMinimum()
    {
        var tiny = new Size(300, 250);
        var (_, panel) = SchedulerToolbar.PlacePicker(new Rect(10, 10, 170, 35), tiny, new Size(560, 460), Min);

        Assert.Equal(Min, panel);
    }
}
