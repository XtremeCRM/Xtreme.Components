using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Rendering;

namespace Xtreme.Components.Scheduler.Controls;

/// <summary>
/// Calendar month grid: a weekday header plus a 6×7 grid of day cells. Each cell shows its
/// date and up to a few event chips with a "+N more" overflow. Days outside the anchor month
/// are dimmed. Read-only rendering for Phase 2.
/// </summary>
public sealed partial class MonthView : UserControl
{
    private const int MaxChipsPerCell = 3;

    private DateTime _anchor = DateTime.Today;
    private IReadOnlyList<AppointmentModel> _appointments = Array.Empty<AppointmentModel>();
    private SchedulerPalette _palette = SchedulerPalette.For(ElementTheme.Default);
    private AppointmentTemplating _templating = AppointmentTemplating.None;

    public MonthView()
    {
        InitializeComponent();
    }

    /// <summary>Supply the anchor month (any date within it), appointments, and style.</summary>
    internal void SetData(DateTime anchor, IReadOnlyList<AppointmentModel> appointments, SchedulerPalette palette, AppointmentTemplating templating)
    {
        _anchor = anchor;
        _appointments = appointments;
        _palette = palette;
        _templating = templating;
        Render();
    }

    private void Render()
    {
        Root.Children.Clear();
        Root.RowDefinitions.Clear();
        Root.ColumnDefinitions.Clear();

        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // weekday header
        for (var r = 0; r < 6; r++)
        {
            Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }
        for (var c = 0; c < 7; c++)
        {
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var monthStart = new DateTime(_anchor.Year, _anchor.Month, 1);
        var gridStart = StartOfWeek(monthStart);

        // Weekday header (Monday-first, derived from the grid's first row).
        for (var c = 0; c < 7; c++)
        {
            var name = gridStart.AddDays(c).ToString("ddd", System.Globalization.CultureInfo.CurrentCulture);
            var header = new Border
            {
                Background = _palette.HeaderBackground,
                BorderBrush = _palette.MajorLine,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock
                {
                    Text = name,
                    FontSize = 12,
                    Foreground = _palette.SecondaryForeground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 4),
                },
            };
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, c);
            Root.Children.Add(header);
        }

        // Day cells.
        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                var date = gridStart.AddDays(r * 7 + c);
                var cell = BuildCell(date);
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c);
                Root.Children.Add(cell);
            }
        }
    }

    private Border BuildCell(DateTime date)
    {
        var inMonth = date.Month == _anchor.Month && date.Year == _anchor.Year;
        var isToday = date.Date == DateTime.Today;

        var numberBrush = !inMonth
            ? _palette.OutsideMonthForeground
            : isToday ? _palette.AccentForeground : _palette.HeaderForeground;

        var stack = new StackPanel { Padding = new Thickness(4, 2, 4, 2), Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = date.Day.ToString(),
            FontSize = 12,
            FontWeight = isToday ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = numberBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        var dayEvents = _appointments
            .Where(a => OnDate(a, date))
            .OrderBy(a => a.IsAllDay ? 0 : 1)
            .ThenBy(a => a.Start)
            .ToList();

        foreach (var a in dayEvents.Take(MaxChipsPerCell))
        {
            var chip = EventBlockFactory.CreateChip(a, _palette, _templating, showTime: !a.IsAllDay);
            chip.Height = 18;
            stack.Children.Add(chip);
        }
        if (dayEvents.Count > MaxChipsPerCell)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"+{dayEvents.Count - MaxChipsPerCell} more",
                FontSize = 10,
                Foreground = _palette.SecondaryForeground,
                Margin = new Thickness(2, 0, 0, 0),
            });
        }

        return new Border
        {
            BorderBrush = _palette.MinorLine,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = isToday ? _palette.TodayHighlight : null,
            Child = stack,
        };
    }

    private static bool OnDate(AppointmentModel a, DateTime date) =>
        a.Start.Date <= date.Date && date.Date <= AllDayBannerRenderer.LastCoveredDate(a);

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return date.Date.AddDays(-diff);
    }
}
