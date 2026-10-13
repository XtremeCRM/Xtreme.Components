using System.Collections;
using System.Reflection;
using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Utilities;

namespace Xtreme.Components.Scheduler.Tests;

public sealed class AppointmentModelTests
{
    // Every settable property, set to a non-default value. Reflection drives the check, so a property added
    // to AppointmentModel later fails CloneCopiesEveryProperty until Clone carries it (or it is listed here).
    private static AppointmentModel FullyPopulated() => new()
    {
        Id = 42,
        Subject = "Install",
        Description = "Front door",
        Memo = "Gate code 1234",
        Start = new DateTime(2026, 3, 2, 9, 0, 0),
        End = new DateTime(2026, 3, 2, 11, 30, 0),
        IsAllDay = true,
        ResourceIds = new List<object> { 1, 2 },
        BarColor = Windows.UI.Color.FromArgb(255, 1, 2, 3),
        BackColor = Windows.UI.Color.FromArgb(255, 4, 5, 6),
        RecurrenceRule = "FREQ=DAILY",
        RecurrenceException = "20260303",
        IsLocked = true,
        IsComplete = true,
        IsCancelled = true,
        IsTentative = true,
        IsConfirmed = true,
        Indicators = new List<AppointmentIndicator> { new() { Glyph = "x", Tooltip = "tip" } },
        Details = new List<AppointmentDetail> { new("Customer", "Rivera") },
        Adornments = new List<AppointmentDecoration> { new() { Text = "URGENT", Position = DecorationPosition.Top } },
        Tiles = new List<AppointmentDecoration> { new() { Text = "$10 due", Position = DecorationPosition.Bottom } },
        Tag = new object(),
    };

    [Fact]
    public void CloneCopiesEveryProperty()
    {
        var original = FullyPopulated();
        var copy = original.Clone();

        Assert.NotSame(original, copy);
        foreach (var property in typeof(AppointmentModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var expected = property.GetValue(original);
            var actual = property.GetValue(copy);
            if (expected is IEnumerable expectedItems and not string)
            {
                Assert.Equal(expectedItems.Cast<object>(), ((IEnumerable)actual!).Cast<object>());
            }
            else
            {
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void CloneGivesTheCopyItsOwnLists()
    {
        var original = FullyPopulated();
        var copy = original.Clone();

        Assert.NotSame(original.ResourceIds, copy.ResourceIds);
        Assert.NotSame(original.Indicators, copy.Indicators);
        Assert.NotSame(original.Details, copy.Details);
        Assert.NotSame(original.Adornments, copy.Adornments);
        Assert.NotSame(original.Tiles, copy.Tiles);

        copy.Adornments.Clear();
        copy.ResourceIds.Add(3);
        Assert.Single(original.Adornments);
        Assert.Equal(2, original.ResourceIds.Count);

        // Items and Tag are shared, not deep-copied.
        Assert.Same(original.Tiles[0], copy.Tiles[0]);
        Assert.Same(original.Tag, copy.Tag);
    }

    [Fact]
    public void ZonedCopyShiftsOnlyTheTimes()
    {
        var original = FullyPopulated();
        original.Start = new DateTime(2026, 1, 15, 15, 0, 0); // 15:00 UTC = 10:00 in New York (EST)
        original.End = new DateTime(2026, 1, 15, 16, 0, 0);

        var shown = TimeZoneConverter.ToZone(original, "America/New_York");

        Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0), shown.Start);
        Assert.Equal(new DateTime(2026, 1, 15, 11, 0, 0), shown.End);
        Assert.Equal(original.Subject, shown.Subject);
        Assert.Equal(original.Tiles, shown.Tiles);
        Assert.Equal(original.Adornments, shown.Adornments);
        Assert.Same(original.Tag, shown.Tag);
        Assert.True(shown.IsLocked);

        // The original is untouched.
        Assert.Equal(new DateTime(2026, 1, 15, 15, 0, 0), original.Start);
    }

    [Fact]
    public void ZonedCopyWithoutZoneIsAnUnshiftedCopy()
    {
        var original = FullyPopulated();
        var shown = TimeZoneConverter.ToZone(original, null);

        Assert.NotSame(original, shown);
        Assert.Equal(original.Start, shown.Start);
        Assert.Equal(original.End, shown.End);
    }
}
