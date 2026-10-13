using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Xtreme.Components.Scheduler.Contracts;
using Xtreme.Components.Scheduler.Interaction;
using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Rendering;
using Xtreme.Components.Scheduler.Utilities;

namespace Xtreme.Components.Scheduler.Controls;

/// <summary>
/// Resource timeline: every resource is a horizontal lane (rows), and one continuous time axis
/// runs left→right across the whole selected week. Events are bars positioned by their absolute
/// time; overlapping events within a lane stack as sub-rows. The time header (top) and resource
/// gutter (left) are frozen and scroll-synced to the 2D-scrolling body. Read-only for now —
/// horizontal drag/resize is the planned follow-up.
/// </summary>
public sealed partial class PlannerView : UserControl
{
    private const double GutterWidth = 160;
    private const double PixelsPerMinute = 0.6;   // 1h = 36px, 1 day = 864px
    private const double RowHeight = 30;           // per overlap sub-row
    private const double HourWidth = 60 * PixelsPerMinute;
    private const double DayWidth = 1440 * PixelsPerMinute;

    private DateTime _anchor = DateTime.Today;
    private IReadOnlyList<AppointmentModel> _appointments = Array.Empty<AppointmentModel>();
    private IReadOnlyList<ResourceModel> _resources = Array.Empty<ResourceModel>();
    private SchedulerPalette _palette = SchedulerPalette.For(ElementTheme.Default);
    private AppointmentTemplating _templating = AppointmentTemplating.None;
    private SchedulerGroupMode _groupMode = SchedulerGroupMode.ByResource;
    private DayOfWeek _firstDayOfWeek = DayOfWeek.Sunday;
    private TimeIncrementConfig _increment = TimeIncrementConfig.Default;
    private bool _isReadOnly;

    private PlannerTimelineInteraction? _interaction;

    public event EventHandler<AppointmentCreateArgs>? CreateRequested;
    public event EventHandler<AppointmentMoveArgs>? MoveRequested;
    public event EventHandler<AppointmentResizeArgs>? ResizeRequested;
    public event EventHandler<ExternalDropArgs>? ExternalDropRequested;
    public event EventHandler<AppointmentContextRequestedArgs>? AppointmentContextMenuRequested;

    public PlannerView()
    {
        InitializeComponent();
    }

    internal void SetData(
        DateTime anchor,
        IReadOnlyList<AppointmentModel> appointments,
        IReadOnlyList<ResourceModel> resources,
        TimeIncrementConfig increment,
        SchedulerPalette palette,
        SchedulerGroupMode groupMode,
        DayOfWeek firstDayOfWeek,
        bool isReadOnly,
        AppointmentTemplating templating)
    {
        _anchor = anchor;
        _appointments = appointments;
        _resources = resources;
        _palette = palette;
        _groupMode = groupMode;
        _firstDayOfWeek = firstDayOfWeek;
        _increment = increment; // horizontal scale stays fixed; MinorTick is reused as the drag snap step
        _isReadOnly = isReadOnly;
        _templating = templating;
        Render();
    }

    private readonly record struct Lane(ResourceModel? Resource, DateTime? DayHeader, IReadOnlyList<EventLayout> Layout, int RowCount);

    private void Render()
    {
        Root.Children.Clear();
        Root.RowDefinitions.Clear();
        Root.ColumnDefinitions.Clear();

        if (_resources.Count == 0)
        {
            Root.Children.Add(new TextBlock
            {
                Text = "No resources to plan",
                Foreground = _palette.SecondaryForeground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return;
        }

        var weekStart = StartOfWeek(_anchor);
        var rangeStart = weekStart;
        var rangeEnd = weekStart.AddDays(7);
        var totalWidth = (rangeEnd - rangeStart).TotalMinutes * PixelsPerMinute;

        // ---- Lanes: one per resource (ByResource), or per (day, resource) with day headers (ByDay) ----
        var lanes = new List<Lane>();
        if (_groupMode == SchedulerGroupMode.ByDay)
        {
            for (var d = 0; d < 7; d++)
            {
                lanes.Add(new Lane(null, weekStart.AddDays(d), Array.Empty<EventLayout>(), 1));
                foreach (var r in _resources)
                {
                    lanes.Add(BuildLane(r, rangeStart, rangeEnd, weekStart.AddDays(d), weekStart.AddDays(d + 1)));
                }
            }
        }
        else
        {
            foreach (var r in _resources)
            {
                lanes.Add(BuildLane(r, rangeStart, rangeEnd, rangeStart, rangeEnd));
            }
        }

        var totalHeight = lanes.Sum(l => LaneHeight(l));

        // ---- Grid skeleton: [corner | time header] / [resource gutter | scrolling body] ----
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var corner = new Border { Width = GutterWidth, Background = _palette.HeaderBackground };
        Grid.SetRow(corner, 0);
        Grid.SetColumn(corner, 0);
        Root.Children.Add(corner);

        var headerScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            Content = BuildTimeHeader(weekStart, totalWidth),
        };
        Grid.SetRow(headerScroll, 0);
        Grid.SetColumn(headerScroll, 1);
        Root.Children.Add(headerScroll);

        var gutterScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Enabled,
            Content = BuildGutter(lanes, totalHeight),
        };
        Grid.SetRow(gutterScroll, 1);
        Grid.SetColumn(gutterScroll, 0);
        Root.Children.Add(gutterScroll);

        var bodyScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Enabled,
            ZoomMode = ZoomMode.Disabled,
            Content = BuildBody(lanes, totalWidth, totalHeight),
        };
        Grid.SetRow(bodyScroll, 1);
        Grid.SetColumn(bodyScroll, 1);
        Root.Children.Add(bodyScroll);

        // Freeze header + gutter to the body's scroll offsets.
        bodyScroll.ViewChanged += (_, _) =>
        {
            headerScroll.ChangeView(bodyScroll.HorizontalOffset, null, null, disableAnimation: true);
            gutterScroll.ChangeView(null, bodyScroll.VerticalOffset, null, disableAnimation: true);
        };

        // Default the horizontal view to ~8 AM of the first day.
        var targetX = 8 * 60 * PixelsPerMinute;
        void OnLayoutUpdated(object? s, object e)
        {
            if (bodyScroll.ScrollableWidth <= 0)
            {
                return;
            }
            bodyScroll.LayoutUpdated -= OnLayoutUpdated;
            bodyScroll.ChangeView(Math.Min(targetX, bodyScroll.ScrollableWidth), null, null, disableAnimation: true);
        }
        bodyScroll.LayoutUpdated += OnLayoutUpdated;
    }

    private Lane BuildLane(ResourceModel r, DateTime rangeStart, DateTime rangeEnd, DateTime clipStart, DateTime clipEnd)
    {
        var tz = r.TimeZoneId;
        var laneEvents = _appointments
            .Where(a => !AllDayBannerRenderer.IsBannerEvent(a) && a.ResourceIds.Any(id => Equals(id, r.Id)))
            .Select(a => string.IsNullOrEmpty(tz) ? a : ShiftToZone(a, tz))
            .Where(a => a.End > clipStart && a.Start < clipEnd);

        var layout = EventBlockLayout.LayoutRange(rangeStart, rangeEnd, laneEvents);
        var rowCount = layout.Count == 0 ? 1 : layout.Max(el => el.ColumnCount);
        return new Lane(r, null, layout, rowCount);
    }

    private static double LaneHeight(Lane lane) =>
        lane.DayHeader is not null ? 26 : Math.Max(1, lane.RowCount) * RowHeight;

    private FrameworkElement BuildTimeHeader(DateTime weekStart, double totalWidth)
    {
        var stack = new StackPanel { Width = totalWidth };

        // Tier 1 — day labels.
        var dayRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (var d = 0; d < 7; d++)
        {
            var day = weekStart.AddDays(d);
            var isToday = day.Date == DateTime.Today;
            dayRow.Children.Add(new Border
            {
                Width = DayWidth,
                Background = _palette.HeaderBackground,
                BorderBrush = _palette.MajorLine,
                BorderThickness = new Thickness(d == 0 ? 0 : 1, 0, 0, 1),
                Child = new TextBlock
                {
                    Text = day.ToString("ddd, MMM d", System.Globalization.CultureInfo.CurrentCulture),
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = isToday ? _palette.AccentForeground : _palette.HeaderForeground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 4),
                },
            });
        }
        stack.Children.Add(dayRow);

        // Tier 2 — six-hour marks per day.
        var hourRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (var d = 0; d < 7; d++)
        {
            for (var h = 0; h < 24; h += 6)
            {
                var isDayStart = h == 0;
                hourRow.Children.Add(new Border
                {
                    Width = HourWidth * 6,
                    Background = _palette.HeaderBackground,
                    BorderBrush = isDayStart ? _palette.MajorLine : _palette.MinorLine,
                    BorderThickness = new Thickness(d == 0 && h == 0 ? 0 : 1, 0, 0, 1),
                    Child = new TextBlock
                    {
                        Text = DateTime.Today.AddHours(h).ToString("h tt", System.Globalization.CultureInfo.CurrentCulture),
                        FontSize = 10,
                        Foreground = _palette.SecondaryForeground,
                        Margin = new Thickness(4, 2, 0, 2),
                    },
                });
            }
        }
        stack.Children.Add(hourRow);

        return stack;
    }

    private FrameworkElement BuildGutter(IReadOnlyList<Lane> lanes, double totalHeight)
    {
        // Explicit per-lane RowDefinitions so the gutter rows are pixel-identical to the body rows.
        var grid = new Grid { Width = GutterWidth, Height = totalHeight };
        foreach (var lane in lanes)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LaneHeight(lane)) });
        }

        for (var i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            Border cell;
            if (lane.DayHeader is DateTime day)
            {
                cell = new Border
                {
                    Background = _palette.HeaderBackground,
                    BorderBrush = _palette.MajorLine,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Child = new TextBlock
                    {
                        Text = day.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = _palette.HeaderForeground,
                        Margin = new Thickness(10, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
            }
            else
            {
                var chip = new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = lane.Resource!.Color is Color c ? new SolidColorBrush(c) : _palette.EventDefaultBar,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var name = new TextBlock
                {
                    Text = lane.Resource.Name,
                    FontSize = 12,
                    Foreground = _palette.HeaderForeground,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(chip);
                row.Children.Add(name);

                cell = new Border
                {
                    BorderBrush = _palette.MinorLine,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Child = row,
                };
            }

            Grid.SetRow(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private FrameworkElement BuildBody(IReadOnlyList<Lane> lanes, double totalWidth, double totalHeight)
    {
        // Same explicit RowDefinitions as the gutter → guaranteed row-for-row alignment.
        var grid = new Grid { Width = totalWidth, Height = totalHeight };
        foreach (var lane in lanes)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LaneHeight(lane)) });
        }

        // Background — day/hour vertical gridlines spanning every lane row.
        var gridlines = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 7 * 24; i++)
        {
            var isDayStart = i % 24 == 0;
            gridlines.Children.Add(new Border
            {
                Width = HourWidth,
                BorderBrush = isDayStart ? _palette.MajorLine : _palette.MinorLine,
                BorderThickness = new Thickness(i == 0 ? 0 : (isDayStart ? 1 : 0.5), 0, 0, 0),
            });
        }
        Grid.SetRow(gridlines, 0);
        Grid.SetRowSpan(gridlines, lanes.Count);
        grid.Children.Add(gridlines);

        // Accumulate each resource lane's absolute Y band so the interaction can map a pointer Y
        // back to a lane (lane heights are non-uniform — one row per overlap depth).
        var bands = new List<PlannerLaneBand>(lanes.Count);
        var laneTop = 0d;
        for (var i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            var laneHeight = LaneHeight(lane);
            Border laneElement;
            if (lane.DayHeader is not null)
            {
                laneElement = new Border
                {
                    Background = _palette.HeaderBackground,
                    BorderBrush = _palette.MajorLine,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                };
            }
            else
            {
                var panel = new TimelineLanePanel
                {
                    PixelsPerMinute = PixelsPerMinute,
                    RowHeight = RowHeight,
                    RowCount = lane.RowCount,
                };
                foreach (var el in lane.Layout)
                {
                    var bar = EventBlockFactory.CreateBar(el.Appointment, _palette, _templating);
                    TimelineLanePanel.SetStartMinutes(bar, el.StartMinutes);
                    TimelineLanePanel.SetDurationMinutes(bar, el.EndMinutes - el.StartMinutes);
                    TimelineLanePanel.SetSubRow(bar, el.ColumnIndex);
                    panel.Children.Add(bar);
                }
                laneElement = new Border
                {
                    BorderBrush = _palette.MinorLine,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = panel,
                };
                bands.Add(new PlannerLaneBand(laneTop, laneHeight, lane.RowCount, lane.Resource!.Id, lane.Layout));
            }

            Grid.SetRow(laneElement, i);
            grid.Children.Add(laneElement);
            laneTop += laneHeight;
        }

        // A full-covering, hit-testable overlay is the single pointer surface (and ghost host) for
        // the whole timeline body — see PlannerTimelineInteraction for why bubbling from bars is
        // unused. Wired for ByResource only; ByDay (per-day-clipped lanes) interaction is a follow-up.
        if (!_isReadOnly && _groupMode == SchedulerGroupMode.ByResource && bands.Count > 0)
        {
            var overlay = new Canvas { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            Grid.SetRow(overlay, 0);
            Grid.SetRowSpan(overlay, lanes.Count);
            grid.Children.Add(overlay);

            _interaction = new PlannerTimelineInteraction(
                overlay,
                bands,
                PixelsPerMinute,
                RowHeight,
                totalWidth / PixelsPerMinute,
                _increment.MinorTick.TotalMinutes,
                _palette,
                _isReadOnly,
                onMove: (appt, fromId, toId, startMin, endMin, isCopy, withShift) => MoveRequested?.Invoke(this, new AppointmentMoveArgs
                {
                    Appointment = appt,
                    NewStart = ToDateTime(toId, startMin),
                    NewEnd = ToDateTime(toId, endMin),
                    FromResourceId = fromId,
                    ToResourceId = toId,
                    IsCopy = isCopy,
                    WithShift = withShift,
                }),
                onResize: (appt, resourceId, startMin, endMin, withShift) => ResizeRequested?.Invoke(this, new AppointmentResizeArgs
                {
                    Appointment = appt,
                    NewStart = ToDateTime(resourceId, startMin),
                    NewEnd = ToDateTime(resourceId, endMin),
                    WithShift = withShift,
                }),
                onCreate: (resourceId, startMin, endMin) => CreateRequested?.Invoke(this, new AppointmentCreateArgs
                {
                    Start = ToDateTime(resourceId, startMin),
                    End = ToDateTime(resourceId, endMin),
                    ResourceId = resourceId,
                }),
                onExternalDrop: (item, resourceId, startMin, endMin) => ExternalDropRequested?.Invoke(this, new ExternalDropArgs
                {
                    Item = item,
                    Start = ToDateTime(resourceId, startMin),
                    End = ToDateTime(resourceId, endMin),
                    ResourceId = resourceId,
                }),
                onContext: (a, pos) => AppointmentContextMenuRequested?.Invoke(this, new AppointmentContextRequestedArgs { Appointment = a, Position = pos }));
        }

        return grid;
    }

    /// <summary>
    /// Convert minutes-from-range-origin (the timeline's display axis) to a stored DateTime for
    /// <paramref name="resourceId"/>. The display axis is the resource's local wall-clock, so for a
    /// per-resource-timezone lane the result is shifted back out of that zone to the UTC convention
    /// the appointment is stored in (the inverse of the render-time <see cref="TimeZoneConverter.ToZone"/>).
    /// </summary>
    private DateTime ToDateTime(object? resourceId, double minutes)
    {
        var display = StartOfWeek(_anchor).AddMinutes(minutes);
        var tz = _resources.FirstOrDefault(r => Equals(r.Id, resourceId))?.TimeZoneId;
        return string.IsNullOrEmpty(tz) ? display : TimeZoneConverter.FromZone(display, tz);
    }

    private static AppointmentModel ShiftToZone(AppointmentModel a, string tz) => new()
    {
        Id = a.Id,
        Subject = a.Subject,
        Description = a.Description,
        Start = TimeZoneConverter.ToZone(a.Start, tz),
        End = TimeZoneConverter.ToZone(a.End, tz),
        IsAllDay = a.IsAllDay,
        ResourceIds = a.ResourceIds,
        BarColor = a.BarColor,
        BackColor = a.BackColor,
        RecurrenceRule = a.RecurrenceRule,
        RecurrenceException = a.RecurrenceException,
    };

    private DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (int)date.DayOfWeek - (int)_firstDayOfWeek) % 7;
        return date.Date.AddDays(-diff);
    }
}
