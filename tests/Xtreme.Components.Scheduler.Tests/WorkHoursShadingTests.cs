using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Rendering;

namespace Xtreme.Components.Scheduler.Tests;

/// <summary>
/// <see cref="WorkHoursShading"/>: per-resource working hours in the Day/Week time grid (XtremeCRM XU-1370).
/// Minutes run 0..1440.
/// </summary>
public sealed class WorkHoursShadingTests
{
    private static readonly TimeSpan GlobalStart = TimeSpan.FromHours(8);
    private static readonly TimeSpan GlobalEnd = TimeSpan.FromHours(17);

    private static ResourceWorkDay Day(double startHours, double endHours, bool isOff = false, bool isOverride = false) => new()
    {
        ResourceId = 7,
        Date = new DateTime(2026, 12, 1),
        Start = TimeSpan.FromHours(startHours),
        End = TimeSpan.FromHours(endHours),
        IsOff = isOff,
        IsOverride = isOverride,
    };

    [Fact]
    public void NoRowKeepsTheGlobalBandWithoutLines()
    {
        var plan = WorkHoursShading.For(null, GlobalStart, GlobalEnd);

        Assert.Equal(new[] { (0d, 480d), (1020d, 1440d) }, plan.Shaded);
        Assert.Empty(plan.Lines);
        Assert.Empty(plan.Dots);
    }

    [Fact]
    public void WorkingDayShadesOutsideItsOwnWindowAndDrawsBothLines()
    {
        var plan = WorkHoursShading.For(Day(7, 15.5), GlobalStart, GlobalEnd);

        Assert.Equal(new[] { (0d, 420d), (930d, 1440d) }, plan.Shaded);
        Assert.Equal(new[] { 420d, 930d }, plan.Lines);
        Assert.Empty(plan.Dots);
    }

    [Fact]
    public void OverrideDotsSitOnEachLine()
    {
        var plan = WorkHoursShading.For(Day(9, 13, isOverride: true), GlobalStart, GlobalEnd);

        Assert.Equal(plan.Lines, plan.Dots);
    }

    [Fact]
    public void DayOffShadesTheWholeColumn()
    {
        var plan = WorkHoursShading.For(Day(8, 17, isOff: true), GlobalStart, GlobalEnd);

        Assert.Equal(new[] { (0d, 1440d) }, plan.Shaded);
        Assert.Empty(plan.Lines);
        Assert.Empty(plan.Dots);
    }

    [Fact]
    public void OverriddenDayOffPutsOneDotMidColumn()
    {
        var plan = WorkHoursShading.For(Day(8, 17, isOff: true, isOverride: true), GlobalStart, GlobalEnd);

        Assert.Equal(new[] { 720d }, plan.Dots);
    }

    [Fact]
    public void WindowTouchingMidnightDrawsNoLineThere()
    {
        var plan = WorkHoursShading.For(Day(0, 24), GlobalStart, GlobalEnd);

        Assert.Empty(plan.Shaded);
        Assert.Empty(plan.Lines);
    }

    [Fact]
    public void InvalidWindowFallsBackToTheGlobalBand()
    {
        var plan = WorkHoursShading.For(Day(17, 8), GlobalStart, GlobalEnd);

        Assert.Equal(new[] { (0d, 480d), (1020d, 1440d) }, plan.Shaded);
        Assert.Empty(plan.Lines);
    }

    [Fact]
    public void IndexMatchesBoxedIdsByValueAndIgnoresTheTime()
    {
        var map = WorkHoursShading.Index(new[] { Day(9, 17) });

        Assert.True(map.ContainsKey(((object)7, new DateTime(2026, 12, 1))));
    }
}
