using Xtreme.Components.Scheduler.Contracts;
using Xtreme.Components.Scheduler.Interaction;
using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Rendering;

namespace Xtreme.Components.Scheduler.Controls;

/// <summary>
/// Calendar month grid: a weekday header plus a 6×7 grid of day cells. Each cell shows its
/// date and up to a few event chips with a "+N more" overflow. Days outside the anchor month
/// are dimmed. Chips raise click (select), double-click (edit request) and right-click (context);
/// an empty part of a day cell raises an all-day empty-slot click, and a double-click there an all-day
/// create (suppressed when read-only).
/// </summary>
internal sealed partial class MonthView : UserControl, ISelectionView
{
    private const int MaxChipsPerCell = 3;

    private DateTime _anchor = DateTime.Today;
    private IReadOnlyList<AppointmentModel> _appointments = Array.Empty<AppointmentModel>();
    private SchedulerPalette _palette = SchedulerPalette.For(ElementTheme.Default);
    private AppointmentTemplating _templating = AppointmentTemplating.None;
    private bool _isReadOnly;
    private IReadOnlyCollection<AppointmentModel> _selected = Array.Empty<AppointmentModel>();
    private readonly List<SelectionFrame> _frames = new();
    private TapInteraction _taps = new();

    public event EventHandler<AppointmentClickArgs>? AppointmentClicked;
    public event EventHandler<AppointmentModel>? AppointmentDoubleClicked;
    public event EventHandler<AppointmentContextRequestedArgs>? AppointmentContextMenuRequested;
    public event EventHandler<EmptySlotSelectedArgs>? EmptySlotClicked;
    public event EventHandler<EmptySlotSelectedArgs>? EmptySlotDoubleClicked;

    public MonthView()
    {
        InitializeComponent();
    }

    /// <summary>Supply the anchor month (any date within it), appointments, and style.</summary>
    internal void SetData(
        DateTime anchor,
        IReadOnlyList<AppointmentModel> appointments,
        SchedulerPalette palette,
        AppointmentTemplating templating,
        bool isReadOnly = false,
        IReadOnlyCollection<AppointmentModel>? selected = null)
    {
        _anchor = anchor;
        _appointments = appointments;
        _palette = palette;
        _templating = templating;
        _isReadOnly = isReadOnly;
        _selected = selected ?? Array.Empty<AppointmentModel>();
        Render();
    }

    /// <summary>Outline the selected chips without a re-render.</summary>
    public void ApplySelection(IReadOnlyCollection<AppointmentModel>? selected)
    {
        _selected = selected ?? Array.Empty<AppointmentModel>();
        SelectionFrame.Apply(_frames, _selected);
    }

    private void Render()
    {
        Root.Children.Clear();
        Root.RowDefinitions.Clear();
        Root.ColumnDefinitions.Clear();
        _frames.Clear();
        _taps = new TapInteraction
        {
            Click = (a, ctrl) => AppointmentClicked?.Invoke(this, new AppointmentClickArgs { Appointment = a, WithCtrl = ctrl }),
            DoubleClick = a => AppointmentDoubleClicked?.Invoke(this, a),
            Context = (a, pos) => AppointmentContextMenuRequested?.Invoke(this, new AppointmentContextRequestedArgs { Appointment = a, Position = pos }),
            EmptyClick = _isReadOnly ? null : e => EmptySlotClicked?.Invoke(this, e),
            EmptyDoubleClick = _isReadOnly ? null : e => EmptySlotDoubleClicked?.Invoke(this, e),
        };

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

        SelectionFrame.Apply(_frames, _selected);
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
            var frame = new SelectionFrame(chip, a, 3);
            _taps.AttachAppointment(frame, a);
            _frames.Add(frame);
            stack.Children.Add(frame);
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

        var cell = new Border
        {
            BorderBrush = _palette.MinorLine,
            BorderThickness = new Thickness(0, 0, 1, 1),
            // Transparent (not null) so the empty part of the cell is hit-testable for clicks.
            Background = isToday ? _palette.TodayHighlight : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = stack,
        };
        _taps.AttachEmptyDay(cell, date, resourceId: null);
        return cell;
    }

    private static bool OnDate(AppointmentModel a, DateTime date) =>
        a.Start.Date <= date.Date && date.Date <= AllDayBannerRenderer.LastCoveredDate(a);

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return date.Date.AddDays(-diff);
    }
}
