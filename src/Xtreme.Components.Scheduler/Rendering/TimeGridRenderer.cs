using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// Builds the static chrome of a time-grid view: the horizontal gridlines (as a stack of
/// fixed-height row borders that stretch with the column width — no pixel math, no resize
/// handling) and the left-hand hour-label gutter. Both derive from the same
/// <see cref="TimeIncrementConfig"/> so labels align with the major lines.
/// </summary>
internal static class TimeGridRenderer
{
    public const double GutterWidth = 56;

    /// <summary>A column-width-stretching stack of minor-slot rows; major boundaries are heavier.</summary>
    public static Panel BuildGridlines(TimeIncrementConfig inc, SchedulerPalette palette)
    {
        var minor = inc.MinorTick.TotalMinutes;
        var major = inc.MajorTick.TotalMinutes;
        var ppm = inc.PixelsPerMinute;
        var count = (int)Math.Round(24 * 60 / minor);

        // Each line is a 1px full-width bar placed at its EXACT pixel Y (minutes × PixelsPerMinute) — the
        // identical math DayColumnPanel uses to position blocks — so gridlines and appointments can never
        // drift apart. (The old approach stacked Height-set bordered rows, but Uno's box model can add the
        // per-row border to the row height, drifting the lines ~1px/row below the blocks → appointments
        // rendered progressively high.)
        var host = new Grid { Height = inc.DayHeight };
        for (var i = 0; i <= count; i++)
        {
            var minutes = i * minor;
            var isMajor = major > 0 && Math.Abs(minutes % major) < 0.001;
            host.Children.Add(new Border
            {
                Height = 1,
                Background = isMajor ? palette.MajorLine : palette.MinorLine,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, minutes * ppm, 0, 0),
            });
        }

        return host;
    }

    /// <summary>The left gutter with one hour label per major tick.</summary>
    public static Panel BuildHourGutter(TimeIncrementConfig inc, SchedulerPalette palette)
    {
        var major = inc.MajorTick.TotalMinutes;
        var slotH = inc.MajorSlotHeight;
        var count = (int)Math.Round(24 * 60 / major);

        var sp = new StackPanel { Width = GutterWidth };
        for (var i = 0; i < count; i++)
        {
            var t = TimeSpan.FromMinutes(i * major);
            var cell = new Border { Height = slotH };
            cell.Child = new TextBlock
            {
                Text = FormatHour(t),
                FontSize = 11,
                Foreground = palette.SecondaryForeground,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                // Top-align the label with its hour line so it lines up with appointment tops and the
                // off-hours boundary (which sit exactly on the line); nudge the first down so it isn't clipped.
                Margin = new Thickness(0, i == 0 ? 2 : 1, 6, 0),
            };
            sp.Children.Add(cell);
        }

        return sp;
    }

    private static string FormatHour(TimeSpan t) =>
        DateTime.Today.Add(t).ToString("h tt", System.Globalization.CultureInfo.CurrentCulture);
}
