using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>The built banner element and how many stacked rows it needs (for height).</summary>
public readonly record struct AllDayBanner(FrameworkElement Element, int RowCount);

/// <summary>
/// Lays out all-day and multi-day-spanning events as horizontal strips across the visible
/// day columns. Strips that don't overlap on the day axis pack onto the same row; the rest
/// stack. The grid uses uniform star columns so it aligns 1:1 with the time grid below it.
/// </summary>
internal static class AllDayBannerRenderer
{
    public const double RowHeight = 24;

    /// <summary>True when an appointment belongs in the banner rather than the time grid.</summary>
    public static bool IsBannerEvent(AppointmentModel a) =>
        a.IsAllDay || LastCoveredDate(a) > a.Start.Date;

    public static AllDayBanner Build(
        IReadOnlyList<DateTime> dates,
        IEnumerable<AppointmentModel> bannerEvents,
        SchedulerPalette palette,
        AppointmentTemplating templating)
    {
        var grid = new Grid();
        for (var c = 0; c < dates.Count; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        if (dates.Count == 0)
        {
            return new AllDayBanner(grid, 0);
        }

        var first = dates[0].Date;
        var last = dates[^1].Date;

        // Project each event to a visible [startCol, span], dropping anything off-window.
        var placed = new List<(AppointmentModel Appt, int Start, int Span)>();
        foreach (var a in bannerEvents)
        {
            var visStart = Max(a.Start.Date, first);
            var visEnd = Min(LastCoveredDate(a), last);
            if (visEnd < visStart)
            {
                continue;
            }

            var startCol = (int)(visStart - first).TotalDays;
            var span = (int)(visEnd - visStart).TotalDays + 1;
            placed.Add((a, startCol, span));
        }

        // Greedy row packing on the day axis.
        placed.Sort((x, y) =>
        {
            var c = x.Start.CompareTo(y.Start);
            return c != 0 ? c : y.Span.CompareTo(x.Span);
        });

        var rowEnds = new List<int>(); // exclusive end column occupied per row
        foreach (var (appt, start, span) in placed)
        {
            var row = -1;
            for (var r = 0; r < rowEnds.Count; r++)
            {
                if (rowEnds[r] <= start)
                {
                    row = r;
                    break;
                }
            }
            if (row < 0)
            {
                row = rowEnds.Count;
                rowEnds.Add(0);
            }
            rowEnds[row] = start + span;

            var chip = EventBlockFactory.CreateChip(appt, palette, templating, showTime: true);
            chip.Margin = new Thickness(1);
            chip.MinHeight = RowHeight - 2;
            Grid.SetColumn(chip, start);
            Grid.SetColumnSpan(chip, span);
            Grid.SetRow(chip, row);
            grid.Children.Add(chip);
        }

        for (var r = 0; r < rowEnds.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        }

        return new AllDayBanner(grid, rowEnds.Count);
    }

    /// <summary>The last calendar date an event covers (inclusive), handling midnight-exclusive ends.</summary>
    internal static DateTime LastCoveredDate(AppointmentModel a)
    {
        var last = a.End.TimeOfDay == TimeSpan.Zero ? a.End.Date.AddDays(-1) : a.End.Date;
        return last < a.Start.Date ? a.Start.Date : last;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
