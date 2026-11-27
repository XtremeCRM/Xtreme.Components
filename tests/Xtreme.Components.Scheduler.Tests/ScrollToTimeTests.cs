using Xtreme.Components.Scheduler.Controls;

namespace Xtreme.Components.Scheduler.Tests;

/// <summary>
/// <see cref="TimeGridView.ScrollOffsetFor"/>: where <c>SchedulerControl.ScrollToTime</c> scrolls the Day/Week
/// body (XtremeCRM XU-1366). The column is centred horizontally; the time sits a quarter of the way down.
/// </summary>
public sealed class ScrollToTimeTests
{
    private const double Ppm = 1.0; // 1px per minute → a 1440px day

    [Fact]
    public void PutsTheTimeAQuarterOfTheWayDownTheViewport()
    {
        var (_, v) = TimeGridView.ScrollOffsetFor(0, 1, 80, TimeSpan.FromHours(10), Ppm, 800, 400, 0, 1040);

        Assert.Equal(600 - 100, v);
    }

    [Fact]
    public void CentresTheColumnWhenColumnsOverflowTheViewport()
    {
        // 20 columns at the 100px minimum in an 800px viewport: column 10 starts at 1000.
        var (h, _) = TimeGridView.ScrollOffsetFor(10, 20, 100, TimeSpan.Zero, Ppm, 800, 400, 1200, 1040);

        Assert.Equal(1000 - 350, h);
    }

    [Fact]
    public void ClampsToTheScrollableRange()
    {
        var early = TimeGridView.ScrollOffsetFor(0, 20, 100, TimeSpan.FromMinutes(30), Ppm, 800, 400, 1200, 1040);
        var late = TimeGridView.ScrollOffsetFor(19, 20, 100, TimeSpan.FromHours(23), Ppm, 800, 400, 1200, 1040);

        Assert.Equal((0d, 0d), early);
        Assert.Equal((1200d, 1040d), late);
    }

    [Fact]
    public void StaysPutHorizontallyWhenEveryColumnFits()
    {
        // 4 columns stretch to 200px each in an 800px viewport: nothing to scroll sideways.
        var (h, _) = TimeGridView.ScrollOffsetFor(3, 4, 80, TimeSpan.FromHours(9), Ppm, 800, 400, 0, 1040);

        Assert.Equal(0, h);
    }
}
