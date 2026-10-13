using Xtreme.Components.Scheduler.Contracts;
using Xtreme.Components.Scheduler.Interaction;
using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Rendering;

namespace Xtreme.Components.Scheduler.Controls;

/// <summary>
/// A flat, date-grouped list of appointments from the anchor date forward. Each date is a
/// section header followed by its events (time range, accent bar, subject, resource names).
/// A row raises click (select), double-click (edit request) and right-click (context); there are no
/// empty slots, so the Agenda never creates.
/// </summary>
internal sealed partial class AgendaView : UserControl, ISelectionView
{
    private readonly List<SelectionFrame> _frames = new();
    private IReadOnlyCollection<AppointmentModel> _selected = Array.Empty<AppointmentModel>();
    private readonly TapInteraction _taps;

    public event EventHandler<AppointmentClickArgs>? AppointmentClicked;
    public event EventHandler<AppointmentModel>? AppointmentDoubleClicked;
    public event EventHandler<AppointmentContextRequestedArgs>? AppointmentContextMenuRequested;

    public AgendaView()
    {
        InitializeComponent();
        _taps = new TapInteraction
        {
            Click = (a, ctrl) => AppointmentClicked?.Invoke(this, new AppointmentClickArgs { Appointment = a, WithCtrl = ctrl }),
            DoubleClick = a => AppointmentDoubleClicked?.Invoke(this, a),
            Context = (a, pos) => AppointmentContextMenuRequested?.Invoke(this, new AppointmentContextRequestedArgs { Appointment = a, Position = pos }),
        };
    }

    /// <summary>Outline the selected rows' chips without a re-render.</summary>
    public void ApplySelection(IReadOnlyCollection<AppointmentModel>? selected)
    {
        _selected = selected ?? Array.Empty<AppointmentModel>();
        SelectionFrame.Apply(_frames, _selected);
    }

    internal void SetData(
        DateTime from,
        IReadOnlyList<AppointmentModel> appointments,
        IReadOnlyList<ResourceModel> resources,
        int days,
        SchedulerPalette palette,
        AppointmentTemplating templating)
    {
        Root.Children.Clear();
        _frames.Clear();

        var names = new Dictionary<object, string>();
        foreach (var r in resources)
        {
            names[r.Id] = r.Name;
        }

        // Span the same selected date range as the Day view (the toolbar's range picker sets the span).
        var windowEnd = from.Date.AddDays(Math.Max(1, days));
        var upcoming = appointments
            .Where(a => AllDayBannerRenderer.LastCoveredDate(a) >= from.Date && a.Start.Date < windowEnd)
            .OrderBy(a => a.Start)
            .ThenBy(a => a.Subject, StringComparer.CurrentCulture)
            .ToList();

        if (upcoming.Count == 0)
        {
            Root.Children.Add(new TextBlock
            {
                Text = "No events in this range",
                Foreground = palette.SecondaryForeground,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0),
            });
            return;
        }

        foreach (var group in upcoming.GroupBy(a => a.Start.Date).OrderBy(g => g.Key))
        {
            Root.Children.Add(BuildDateHeader(group.Key, palette));
            foreach (var appt in group)
            {
                Root.Children.Add(BuildRow(appt, names, palette, templating));
            }
        }
        SelectionFrame.Apply(_frames, _selected);
    }

    /// <summary>
    /// Multi-date variant (XU-138): render one section per explicitly-picked date — the "compare open
    /// slots across the next 4 Saturdays" case. Each selected date gets a header followed by the events
    /// overlapping it (a multi-day event appears under every covered date, mirroring the Day columns), or
    /// a muted "No events" line when the day is empty — an empty slot is itself informative here.
    /// </summary>
    internal void SetData(
        IReadOnlyList<DateTime> dates,
        IReadOnlyList<AppointmentModel> appointments,
        IReadOnlyList<ResourceModel> resources,
        SchedulerPalette palette,
        AppointmentTemplating templating)
    {
        Root.Children.Clear();
        _frames.Clear();

        var names = new Dictionary<object, string>();
        foreach (var r in resources)
        {
            names[r.Id] = r.Name;
        }

        var selected = dates.Select(d => d.Date).Distinct().OrderBy(d => d).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var date in selected)
        {
            Root.Children.Add(BuildDateHeader(date, palette));

            var dayAppts = appointments
                .Where(a => a.Start.Date <= date && AllDayBannerRenderer.LastCoveredDate(a) >= date)
                .OrderBy(a => a.Start)
                .ThenBy(a => a.Subject, StringComparer.CurrentCulture)
                .ToList();

            if (dayAppts.Count == 0)
            {
                Root.Children.Add(new TextBlock
                {
                    Text = "No events",
                    Foreground = palette.SecondaryForeground,
                    FontSize = 12,
                    Margin = new Thickness(4, 2, 0, 8),
                });
                continue;
            }

            foreach (var appt in dayAppts)
            {
                Root.Children.Add(BuildRow(appt, names, palette, templating));
            }
        }
        SelectionFrame.Apply(_frames, _selected);
    }

    private static Border BuildDateHeader(DateTime date, SchedulerPalette palette)
    {
        var isToday = date == DateTime.Today;
        var relative = isToday ? "Today" : date == DateTime.Today.AddDays(1) ? "Tomorrow" : null;
        var text = date.ToString("dddd, MMMM d", System.Globalization.CultureInfo.CurrentCulture);
        if (relative is not null)
        {
            text = $"{relative}  ·  {text}";
        }

        return new Border
        {
            Background = palette.HeaderBackground,
            BorderBrush = palette.AgendaRule,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 8, 16, 8),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = isToday ? palette.AccentForeground : palette.HeaderForeground,
            },
        };
    }

    private Border BuildRow(AppointmentModel appt, IReadOnlyDictionary<object, string> names, SchedulerPalette palette, AppointmentTemplating templating)
    {
        var grid = new Grid { Padding = new Thickness(16, 8, 16, 8), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var timeText = appt.IsAllDay
            ? "All day"
            : $"{appt.Start:h:mm tt} – {appt.End:h:mm tt}";
        var time = new TextBlock
        {
            Text = timeText,
            FontSize = 12,
            Foreground = palette.SecondaryForeground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(time, 0);

        // Render the event with the SAME appointment template as the scheduler blocks (fill color +
        // accent + status indicators), so the Agenda matches Day/Week/Month rather than a plain text row.
        var chip = EventBlockFactory.CreateChip(appt, palette, templating);
        chip.HorizontalAlignment = HorizontalAlignment.Stretch;
        var frame = new SelectionFrame(chip, appt, 3);
        _frames.Add(frame);

        var detail = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        detail.Children.Add(frame);

        var resourceNames = appt.ResourceIds
            .Select(id => names.TryGetValue(id, out var n) ? n : null)
            .Where(n => n is not null)
            .ToList();
        if (resourceNames.Count > 0)
        {
            detail.Children.Add(new TextBlock
            {
                Text = string.Join(", ", resourceNames),
                FontSize = 11,
                Foreground = palette.SecondaryForeground,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, 0, 0, 0),
            });
        }
        Grid.SetColumn(detail, 1);

        grid.Children.Add(time);
        grid.Children.Add(detail);

        // The whole row is the gesture target; Transparent (not null) keeps its empty area hit-testable.
        var row = new Border
        {
            BorderBrush = palette.AgendaRule,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = grid,
            DataContext = appt,
            Tag = appt,
        };
        _taps.AttachAppointment(row, appt);
        return row;
    }
}
