using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// Pure geometry for a time-grid column's working hours (unit-tested): which minute ranges are shaded as
/// off-hours, where the thin start/end lines go, and where an override dot sits. Minutes run 0..1440.
/// </summary>
internal static class WorkHoursShading
{
    internal const double DayMinutes = 24 * 60;

    /// <summary>The column's hours plan.</summary>
    /// <param name="Shaded">Off-hours minute ranges to tint.</param>
    /// <param name="Lines">Minutes where a thin hours line is drawn (start, end), empty for the global band.</param>
    /// <param name="Dots">Minutes where an override dot is drawn (the middle of each line, or mid-column when off).</param>
    internal sealed record Plan(
        IReadOnlyList<(double From, double To)> Shaded,
        IReadOnlyList<double> Lines,
        IReadOnlyList<double> Dots);

    /// <summary>
    /// Plan one column. With no <paramref name="day"/> it is the global band (shade only, no lines) as the
    /// control has always drawn it. A day off shades everything. A working day shades outside its window
    /// and draws a line at each end that falls inside the day (a window touching midnight draws none there).
    /// An invalid window (end not after start) falls back to the global band.
    /// </summary>
    internal static Plan For(ResourceWorkDay? day, TimeSpan globalStart, TimeSpan globalEnd)
    {
        if (day is null)
        {
            return new Plan(Outside(globalStart.TotalMinutes, globalEnd.TotalMinutes), Array.Empty<double>(), Array.Empty<double>());
        }

        if (day.IsOff)
        {
            return new Plan(
                new[] { (0d, DayMinutes) },
                Array.Empty<double>(),
                day.IsOverride ? new[] { DayMinutes / 2 } : Array.Empty<double>());
        }

        var start = Math.Clamp(day.Start.TotalMinutes, 0, DayMinutes);
        var end = Math.Clamp(day.End.TotalMinutes, 0, DayMinutes);
        if (end <= start)
        {
            return For(null, globalStart, globalEnd);
        }

        var lines = new List<double>(2);
        if (start > 0) lines.Add(start);
        if (end < DayMinutes) lines.Add(end);
        return new Plan(Outside(start, end), lines, day.IsOverride ? lines.ToArray() : Array.Empty<double>());
    }

    private static IReadOnlyList<(double From, double To)> Outside(double start, double end)
    {
        if (!(end > start && start >= 0 && end <= DayMinutes))
        {
            return Array.Empty<(double, double)>();
        }
        var ranges = new List<(double, double)>(2);
        if (start > 0) ranges.Add((0, start));
        if (end < DayMinutes) ranges.Add((end, DayMinutes));
        return ranges;
    }

    /// <summary>Index the host's rows by (resource, day); a later duplicate wins.</summary>
    internal static Dictionary<(object ResourceId, DateTime Date), ResourceWorkDay> Index(IEnumerable<ResourceWorkDay>? days)
    {
        var map = new Dictionary<(object, DateTime), ResourceWorkDay>();
        if (days is null)
        {
            return map;
        }
        foreach (var d in days)
        {
            if (d?.ResourceId is not null)
            {
                map[(d.ResourceId, d.Date.Date)] = d;
            }
        }
        return map;
    }
}
