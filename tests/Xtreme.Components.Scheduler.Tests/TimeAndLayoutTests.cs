using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Rendering;
using Xtreme.Components.Scheduler.Utilities;

namespace Xtreme.Components.Scheduler.Tests;

public sealed class TimeZoneConverterTests
{
    [Theory]
    [InlineData("America/New_York")]
    [InlineData("Europe/London")]
    [InlineData("Australia/Sydney")]
    public void FromZoneUndoesToZone(string zone)
    {
        var utc = new DateTime(2026, 7, 4, 18, 30, 0);
        Assert.Equal(utc, TimeZoneConverter.FromZone(TimeZoneConverter.ToZone(utc, zone), zone));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/A_Zone")]
    public void MissingOrUnknownZoneLeavesTheTimeAlone(string? zone)
    {
        var value = new DateTime(2026, 7, 4, 18, 30, 0);
        Assert.Equal(value, TimeZoneConverter.ToZone(value, zone));
        Assert.Equal(value, TimeZoneConverter.FromZone(value, zone));
    }

    [Fact]
    public void ShortLabelIsTheLastSegment()
    {
        Assert.Equal("New York", TimeZoneConverter.ShortLabel("America/New_York"));
        Assert.Null(TimeZoneConverter.ShortLabel(null));
    }
}

public sealed class TimeIncrementConfigTests
{
    [Fact]
    public void DefaultIsAFreshInstanceEachTime()
    {
        var a = TimeIncrementConfig.Default;
        a.PixelsPerMinute = 3;
        Assert.Equal(0.8, TimeIncrementConfig.Default.PixelsPerMinute);
        Assert.Equal(TimeSpan.FromMinutes(30), TimeIncrementConfig.Default.MinorTick);
    }
}

public sealed class TileLayoutTests
{
    [Theory]
    [InlineData(DecorationPosition.Left, true, TileSide.Left)]
    [InlineData(DecorationPosition.FloatLeft, true, TileSide.Left)]
    [InlineData(DecorationPosition.Right, true, TileSide.Right)]
    [InlineData(DecorationPosition.FloatRight, true, TileSide.Right)]
    [InlineData(DecorationPosition.Top, true, TileSide.Top)]
    [InlineData(DecorationPosition.Bottom, true, TileSide.Bottom)]
    [InlineData(DecorationPosition.FloatCenter, true, TileSide.Bottom)]
    [InlineData(DecorationPosition.Top, false, TileSide.Right)]
    [InlineData(DecorationPosition.Bottom, false, TileSide.Right)]
    [InlineData(DecorationPosition.Left, false, TileSide.Left)]
    public void SideOfMapsEveryPosition(DecorationPosition position, bool multiLine, object expected)
        => Assert.Equal((TileSide)expected, TileLayout.SideOf(position, multiLine)); // TileSide is internal: passed as object

    [Fact]
    public void OnlyTimedGuttersStackVertically()
    {
        Assert.True(TileLayout.IsVertical(TileSide.Right, multiLine: true));
        Assert.True(TileLayout.IsVertical(TileSide.Left, multiLine: true));
        Assert.False(TileLayout.IsVertical(TileSide.Bottom, multiLine: true));
        Assert.False(TileLayout.IsVertical(TileSide.Right, multiLine: false));
    }

    [Fact]
    public void GroupsKeepHostOrderWithinEachAlignment()
    {
        var far1 = new AppointmentDecoration { Text = "far1", Position = DecorationPosition.Right, Alignment = DecorationAlignment.Far };
        var near1 = new AppointmentDecoration { Text = "near1", Position = DecorationPosition.Right };
        var far2 = new AppointmentDecoration { Text = "far2", Position = DecorationPosition.Right, Alignment = DecorationAlignment.Far };
        var left = new AppointmentDecoration { Text = "left", Position = DecorationPosition.Left };

        var groups = TileLayout.Groups(new[] { far1, near1, far2, left }, TileSide.Right, multiLine: true);

        Assert.Equal(2, groups.Count);
        Assert.Equal(DecorationAlignment.Near, groups[0].Alignment);
        Assert.Equal(new[] { near1 }, groups[0].Tiles);
        Assert.Equal(DecorationAlignment.Far, groups[1].Alignment);
        Assert.Equal(new[] { far1, far2 }, groups[1].Tiles);
    }
}

public sealed class EventBlockLayoutTests
{
    [Fact]
    public void OverlappingAppointmentsShareTheClusterSideBySide()
    {
        var day = new DateTime(2026, 3, 2);
        var a = new AppointmentModel { Start = day.AddHours(9), End = day.AddHours(11) };
        var b = new AppointmentModel { Start = day.AddHours(10), End = day.AddHours(12) };
        var c = new AppointmentModel { Start = day.AddHours(13), End = day.AddHours(14) };

        var layout = EventBlockLayout.Layout(day, new[] { a, b, c });

        var la = layout.Single(l => l.Appointment == a);
        var lb = layout.Single(l => l.Appointment == b);
        var lc = layout.Single(l => l.Appointment == c);
        Assert.Equal(2, la.ColumnCount);
        Assert.NotEqual(la.ColumnIndex, lb.ColumnIndex);
        Assert.Equal(1, lc.ColumnCount);
        Assert.Equal(9 * 60, la.StartMinutes);
    }
}
