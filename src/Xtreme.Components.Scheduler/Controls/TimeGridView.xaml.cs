using Microsoft.UI.Xaml.Media;
using Xtreme.Components.Scheduler.Contracts;
using Xtreme.Components.Scheduler.Interaction;
using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Rendering;

namespace Xtreme.Components.Scheduler.Controls;

/// <summary>
/// The Day and Week workhorse: a vertical time grid over one or more consecutive days. When
/// resources are supplied it splits each day into one column per resource (a two-tier header),
/// ordered by <see cref="SchedulerGroupMode"/>; with none it falls back to one column per date.
/// The hour gutter (left) and the column header (top, incl. the all-day row) are frozen and
/// scroll-synced to a 2D-scrolling body, so dense column sets (many resources × days) stay
/// readable: columns never shrink below <c>ColumnMinWidth</c> and the body scrolls horizontally
/// when they don't all fit.
/// </summary>
internal sealed partial class TimeGridView : UserControl, ISelectionView
{
    private IReadOnlyList<DateTime> _dates = Array.Empty<DateTime>();
    private IReadOnlyList<AppointmentModel> _appointments = Array.Empty<AppointmentModel>();
    private IReadOnlyList<ResourceModel> _resources = Array.Empty<ResourceModel>();
    private SchedulerGroupMode _groupMode = SchedulerGroupMode.ByResource;
    private bool _hideIdleResources;
    private double _columnMinWidth = 80;
    private TimeSpan _workStart = TimeSpan.FromHours(8);
    private TimeSpan _workEnd = TimeSpan.FromHours(17);
    private TimeIncrementConfig _increment = TimeIncrementConfig.Default;
    private SchedulerPalette _palette = SchedulerPalette.For(ElementTheme.Default);
    private AppointmentTemplating _templating = AppointmentTemplating.None;
    private bool _isReadOnly;
    private IReadOnlyList<AppointmentModel> _selectedSet = Array.Empty<AppointmentModel>();

    // Scroll offset preserved across re-renders (e.g. a live appointment upsert) so the grid doesn't
    // snap back to business hours. The host sets these before SetData and tracks ScrollOffsetChanged.
    internal double? PendingVerticalOffset;
    internal double? PendingHorizontalOffset;

    // Timed block elements built this render, so selection can be applied without a re-render.
    private readonly List<FrameworkElement> _timedBlocks = new();

    // Adorned blocks built this render + the shared top layer their adornment overlays are LIFTED into so
    // the pills paint above every column (a nested overlay is clipped/painted-over by the neighbour column).
    private readonly List<FrameworkElement> _adornedBlocks = new();
    private Canvas? _adornmentLayer;

    // Faux drop-shadows placed behind selected blocks (ThemeShadow doesn't render under Skia).
    private readonly List<Border> _selectionShadows = new();

    // The pointer overlay covers the blocks, so hover popups are driven from it (not per-block).
    private Canvas? _overlay;
    private HoverCard? _hover;

    // All-day row chips, wrapped so selection can outline them without a re-render.
    private readonly List<SelectionFrame> _allDayFrames = new();

    private TimeGridInteraction? _interaction;

    // Every grid whose N columns must resize together (header tiers, all-day, body).
    private readonly List<Grid> _columnGrids = new();
    private int _columnCount;

    public event EventHandler<AppointmentCreateArgs>? CreateRequested;
    public event EventHandler<EmptySlotSelectedArgs>? EmptySlotClicked;
    public event EventHandler<AppointmentMoveArgs>? MoveRequested;
    public event EventHandler<AppointmentResizeArgs>? ResizeRequested;
    public event EventHandler<AppointmentsMoveArgs>? MoveManyRequested;
    public event EventHandler<AppointmentsResizeArgs>? ResizeManyRequested;
    public event EventHandler<ExternalDropArgs>? ExternalDropRequested;
    public event EventHandler<AppointmentClickArgs>? AppointmentClicked;
    public event EventHandler<AppointmentModel>? AppointmentDoubleClicked;
    public event EventHandler<EmptySlotSelectedArgs>? EmptySlotDoubleClicked;
    public event EventHandler<AppointmentContextRequestedArgs>? AppointmentContextMenuRequested;
    public event EventHandler<(double V, double H)>? ScrollOffsetChanged;

    public TimeGridView()
    {
        InitializeComponent();
    }

    /// <summary>One body column: the day it represents and (when resource-grouped) its resource.</summary>
    private readonly record struct GridColumn(DateTime Date, ResourceModel? Resource);

    /// <summary>Supply the data + style and (re)build the grid.</summary>
    internal void SetData(
        IReadOnlyList<DateTime> dates,
        IReadOnlyList<AppointmentModel> appointments,
        IReadOnlyList<ResourceModel> resources,
        SchedulerGroupMode groupMode,
        bool hideIdleResources,
        double columnMinWidth,
        TimeSpan workDayStart,
        TimeSpan workDayEnd,
        TimeIncrementConfig increment,
        SchedulerPalette palette,
        bool isReadOnly,
        AppointmentTemplating templating,
        IReadOnlyList<AppointmentModel>? selected)
    {
        _dates = dates;
        _appointments = appointments;
        _resources = resources;
        _groupMode = groupMode;
        _hideIdleResources = hideIdleResources;
        _columnMinWidth = Math.Max(1, columnMinWidth);
        _workStart = workDayStart;
        _workEnd = workDayEnd;
        _increment = increment;
        _palette = palette;
        _isReadOnly = isReadOnly;
        _templating = templating;
        _selectedSet = selected ?? Array.Empty<AppointmentModel>();
        Render();
    }

    private void Render()
    {
        Root.Children.Clear();
        Root.RowDefinitions.Clear();
        Root.ColumnDefinitions.Clear();
        _columnGrids.Clear();
        _timedBlocks.Clear();
        _adornedBlocks.Clear();
        _adornmentLayer = null;
        _selectionShadows.Clear();
        _allDayFrames.Clear();
        _hover?.Hide();
        _hover = null;

        if (_dates.Count == 0)
        {
            return;
        }

        // ---- Column model: resource-grouped when resources are present, else one per date ----
        var rangeStart = _dates[0].Date;
        var rangeEnd = _dates[^1].Date.AddDays(1);

        var resources = _resources;
        if (_hideIdleResources && resources.Count > 0)
        {
            resources = resources.Where(r => HasEventInRange(r, rangeStart, rangeEnd)).ToList();
        }
        var grouped = resources.Count > 0;

        var columns = new List<GridColumn>();
        if (grouped)
        {
            if (_groupMode == SchedulerGroupMode.ByResource) // Resource → Date
            {
                foreach (var r in resources)
                {
                    foreach (var d in _dates)
                    {
                        columns.Add(new GridColumn(d, r));
                    }
                }
            }
            else // ByDay → Date → Resource
            {
                foreach (var d in _dates)
                {
                    foreach (var r in resources)
                    {
                        columns.Add(new GridColumn(d, r));
                    }
                }
            }
        }
        else
        {
            foreach (var d in _dates)
            {
                columns.Add(new GridColumn(d, null));
            }
        }
        _columnCount = columns.Count;

        var dayHeight = _increment.DayHeight;

        // ---- Skeleton: [corner | header] / [gutter | body] ----
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header content = column header (one/two tier) + an optional all-day row, stacked.
        var headerColumns = grouped ? BuildGroupedHeader(columns, resources.Count) : BuildDateHeader(columns);
        _columnGrids.Add(headerColumns);
        var allDayGrid = BuildAllDayColumns(columns);

        var headerContent = new Grid();
        headerContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        headerContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(headerColumns, 0);
        headerContent.Children.Add(headerColumns);
        if (allDayGrid is not null)
        {
            _columnGrids.Add(allDayGrid);
            Grid.SetRow(allDayGrid, 1);
            headerContent.Children.Add(allDayGrid);
        }

        var headerScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = headerContent,
        };
        Grid.SetRow(headerScroll, 0);
        Grid.SetColumn(headerScroll, 1);
        Root.Children.Add(headerScroll);

        // Frozen top-left corner (carries the "all-day" label, bottom-aligned).
        var corner = new Border
        {
            Width = TimeGridRenderer.GutterWidth,
            Background = _palette.HeaderBackground,
            Child = allDayGrid is null ? null : new TextBlock
            {
                Text = "all-day",
                FontSize = 10,
                Foreground = _palette.SecondaryForeground,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 4),
            },
        };
        Grid.SetRow(corner, 0);
        Grid.SetColumn(corner, 0);
        Root.Children.Add(corner);

        // Frozen hour gutter (vertical scroll only).
        var gutterScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Enabled,
            Content = TimeGridRenderer.BuildHourGutter(_increment, _palette),
        };
        Grid.SetRow(gutterScroll, 1);
        Grid.SetColumn(gutterScroll, 0);
        Root.Children.Add(gutterScroll);

        // ---- Body: one column per GridColumn, 2D-scrolling ----
        var colsGrid = new Grid { Height = dayHeight };
        for (var i = 0; i < columns.Count; i++)
        {
            colsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_columnMinWidth) });
        }
        _columnGrids.Add(colsGrid);

        var nonBanner = _appointments.Where(a => !AllDayBannerRenderer.IsBannerEvent(a)).ToList();
        var timeColumns = new List<TimeColumn>(columns.Count);
        var columnLayouts = new List<IReadOnlyList<EventLayout>>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            // Short events are stretched to the panel's pixel floor here too, so they overlap-pack at the
            // height they're actually drawn (XU-837) — at any zoom.
            var minMinutes = Math.Max(EventBlockLayout.DefaultMinVisibleMinutes, DayColumnPanel.MinBlockHeight / _increment.PixelsPerMinute);
            var layout = EventBlockLayout.Layout(col.Date, FilterForColumn(nonBanner, col), minMinutes);
            columnLayouts.Add(layout);
            timeColumns.Add(new TimeColumn(i, col.Date, col.Resource?.Id));

            var cell = BuildColumnCell(col, i == 0, layout);
            Grid.SetColumn(cell, i);
            colsGrid.Children.Add(cell);
        }

        // Full-covering, hit-testable overlay = the single pointer surface (and ghost host); its
        // ActualWidth / column-count gives the live column width, so the interaction adapts to resize.
        var overlay = new PointerOverlay { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        Grid.SetColumn(overlay, 0);
        Grid.SetColumnSpan(overlay, Math.Max(1, columns.Count));
        colsGrid.Children.Add(overlay);
        Canvas.SetZIndex(overlay, OverlayZ); // stays above any selection-raised column
        _overlay = overlay;

        // Shared adornment layer: a full-width, non-hit-testable Canvas spanning every column, sitting just
        // BELOW the pointer overlay but ABOVE all blocks/selection. Adorned blocks' overlays are lifted into
        // it (below) so a pill overhanging into the next column paints on top of that column instead of being
        // covered by it (nested overlays can't win — z-order doesn't cross the sibling column containers).
        var adornmentLayer = new Canvas { IsHitTestVisible = false };
        Grid.SetColumn(adornmentLayer, 0);
        Grid.SetColumnSpan(adornmentLayer, Math.Max(1, columns.Count));
        colsGrid.Children.Add(adornmentLayer);
        Canvas.SetZIndex(adornmentLayer, OverlayZ - 1);
        _adornmentLayer = adornmentLayer;

        // Lift each adorned block's overlay into the shared layer and KEEP it glued to the block. The lift
        // (reparent) happens once; then every layout pass re-syncs the badge's size + position to the block's
        // rect, so it tracks the block through a window resize / column reflow (a nested overlay would move
        // with the block automatically, but a lifted one lives in a different parent and must be repositioned).
        // Scrolling needs no sync — the layer scrolls inside the same content as the blocks. Stays subscribed
        // for the life of this render; a data change rebuilds the whole view (and this closure with it).
        if (_adornedBlocks.Count > 0)
        {
            // Snapshot into locals so a re-render (which clears the fields) can't cross wires with this
            // render's pass — the handler is bound to THIS colsGrid/layer/blocks.
            var badgeBlocks = _adornedBlocks.ToList();
            var layer = adornmentLayer;
            void SyncAdornments(object? sender, object e)
            {
                foreach (var block in badgeBlocks)
                {
                    // Skip a block that hasn't been arranged yet (zero size) — it gets picked up on a later
                    // layout pass. Guard PER block, not on badgeBlocks[0]: one un-arranged block must not
                    // stall the lift for all the others (which would silently disable always-on-top).
                    if (block.ActualWidth <= 0)
                    {
                        continue;
                    }
                    if (DecorationFactory.GetOverlay(block) is not FrameworkElement badge)
                    {
                        continue;
                    }
                    // Reparent into the shared layer the first time we see it (idempotent thereafter).
                    if (!ReferenceEquals(badge.Parent, layer))
                    {
                        (badge.Parent as Panel)?.Children.Remove(badge);
                        layer.Children.Add(badge);
                    }
                    // Re-sync size + position; guard so identical values don't re-invalidate layout (a set to a
                    // NEW value on a Canvas child triggers another pass — without the guard that could loop).
                    if (badge.Width != block.ActualWidth) badge.Width = block.ActualWidth;
                    if (badge.Height != block.ActualHeight) badge.Height = block.ActualHeight;
                    var origin = block.TransformToVisual(layer).TransformPoint(new Point(0, 0));
                    if (Math.Abs(Canvas.GetLeft(badge) - origin.X) > 0.5) Canvas.SetLeft(badge, origin.X);
                    if (Math.Abs(Canvas.GetTop(badge) - origin.Y) > 0.5) Canvas.SetTop(badge, origin.Y);
                }
            }
            colsGrid.LayoutUpdated += SyncAdornments;
        }

        // Hover popup lives in the overlay (a Canvas) so its offsets are in the same coordinate
        // space as the pointer positions the interaction reports.
        _hover = new HoverCard(overlay, _palette, _templating);

        _interaction = new TimeGridInteraction(
            overlay, timeColumns, columnLayouts, fixedColumnWidth: null, _increment, _palette, _isReadOnly,
            onCreate: a => CreateRequested?.Invoke(this, a),
            onMove: a => MoveRequested?.Invoke(this, a),
            onResize: a => ResizeRequested?.Invoke(this, a),
            onExternalDrop: a => ExternalDropRequested?.Invoke(this, a),
            onClick: (a, ctrl) => AppointmentClicked?.Invoke(this, new AppointmentClickArgs { Appointment = a, WithCtrl = ctrl }),
            onContext: (a, pos) => AppointmentContextMenuRequested?.Invoke(this, new AppointmentContextRequestedArgs { Appointment = a, Position = pos }),
            onHover: (a, pt) => _hover?.Show(a, pt),
            isResizable: IsResizable,
            onCursor: overlay.SetCursorShape,
            getSelection: () => _selectedSet,
            onMoveMany: a => MoveManyRequested?.Invoke(this, a),
            onResizeMany: a => ResizeManyRequested?.Invoke(this, a),
            onEmptyClick: a => EmptySlotClicked?.Invoke(this, a));

        var bodyScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Enabled,
            ZoomMode = ZoomMode.Disabled,
            Content = colsGrid,
        };
        Grid.SetRow(bodyScroll, 1);
        Grid.SetColumn(bodyScroll, 1);
        Root.Children.Add(bodyScroll);
        _hover.Scroll = bodyScroll;

        // Freeze the header + gutter to the body's scroll offsets, and report the offset so the host
        // can restore it after a re-render (live upsert) instead of snapping back to business hours.
        bodyScroll.ViewChanged += (_, _) =>
        {
            headerScroll.ChangeView(bodyScroll.HorizontalOffset, null, null, disableAnimation: true);
            gutterScroll.ChangeView(null, bodyScroll.VerticalOffset, null, disableAnimation: true);
            ScrollOffsetChanged?.Invoke(this, (bodyScroll.VerticalOffset, bodyScroll.HorizontalOffset));
        };

        // Size columns to fill the viewport, but never below the minimum (→ horizontal scroll).
        bodyScroll.SizeChanged += (_, e) =>
            ApplyColumnWidth(bodyScroll.ViewportWidth > 0 ? bodyScroll.ViewportWidth : e.NewSize.Width);

        var widthApplied = false;
        var scrolledToBusiness = false;
        void OnLayoutUpdated(object? s, object e)
        {
            if (!widthApplied && bodyScroll.ViewportWidth > 0)
            {
                widthApplied = true;
                ApplyColumnWidth(bodyScroll.ViewportWidth);
            }
            if (!scrolledToBusiness && bodyScroll.ScrollableHeight > 0)
            {
                scrolledToBusiness = true;
                var targetV = PendingVerticalOffset is double pv
                    ? Math.Min(pv, bodyScroll.ScrollableHeight)
                    : Math.Min(7 * 60 * _increment.PixelsPerMinute, bodyScroll.ScrollableHeight);
                double? targetH = PendingHorizontalOffset is double ph ? Math.Min(ph, bodyScroll.ScrollableWidth) : null;
                bodyScroll.ChangeView(targetH, targetV, null, disableAnimation: true);
            }
            if (widthApplied && scrolledToBusiness)
            {
                bodyScroll.LayoutUpdated -= OnLayoutUpdated;
            }
        }
        bodyScroll.LayoutUpdated += OnLayoutUpdated;

        ApplySelection(_selectedSet);
    }

    /// <summary>Two appointments are the same logical event — the same instance, or the same host Id
    /// (a multi-resource event renders once per lane but shares one Id).</summary>
    internal static bool SameAppointment(AppointmentModel a, AppointmentModel b) => AppointmentIdentity.Same(a, b);

    /// <summary>Resize is allowed only on a selected appointment, so two touching events are never
    /// ambiguous at their shared edge (8a). In a multi-selection any selected block is resizable
    /// (8c applies the edge-delta to the whole group). An edge press on an unselected block falls
    /// through to move/click.</summary>
    private bool IsResizable(AppointmentModel appt) =>
        !appt.IsLocked && _selectedSet.Any(s => SameAppointment(appt, s));

    /// <summary>Apply the "selected" visual (slight enlarge + shadow, raised above neighbours) to every
    /// matching block and clear it from the others — no re-render, so the scroll position is kept.</summary>
    public void ApplySelection(IReadOnlyCollection<AppointmentModel>? selected)
    {
        _selectedSet = selected as IReadOnlyList<AppointmentModel> ?? selected?.ToArray() ?? Array.Empty<AppointmentModel>();
        SelectionFrame.Apply(_allDayFrames, _selectedSet);

        // Drop the previous faux shadows; we rebuild them for the current selection below.
        foreach (var s in _selectionShadows)
        {
            (s.Parent as Panel)?.Children.Remove(s);
        }
        _selectionShadows.Clear();

        // Drop any prior selection's column elevation back to the base layer.
        ResetColumnElevation();

        foreach (var b in _timedBlocks)
        {
            var isSel = b.Tag is AppointmentModel a && _selectedSet.Any(sel => SameAppointment(a, sel));

            if (isSel)
            {
                // Pin the LEFT edge (X origin 0) so the modest enlarge grows rightward only; vertically
                // it stays centred. Raise it above same-column neighbours and lift its whole column above
                // adjacent columns so the block + its red outline sit over neighbouring appointments.
                b.RenderTransformOrigin = new Point(0, 0.5);
                b.RenderTransform = new ScaleTransform { ScaleX = SelectionScale, ScaleY = SelectionScale };
                Canvas.SetZIndex(b, 50);
                RaiseColumn(b);

                if (b.Parent is Panel panel)
                {
                    var border = BuildSelectionBorder(b);
                    panel.Children.Add(border);
                    _selectionShadows.Add(border);
                }
            }
            else
            {
                b.RenderTransform = null;
                Canvas.SetZIndex(b, 0);
            }
        }
    }

    // The selected block's column is lifted above adjacent columns (but below the pointer overlay) so
    // the enlarged block + its red outline sit on top of neighbouring appointments.
    private const int SelectedColumnZ = 5;
    private const int OverlayZ = 1000;

    private void ResetColumnElevation()
    {
        if (_overlay?.Parent is Panel cols)
        {
            foreach (var child in cols.Children)
            {
                if (!ReferenceEquals(child, _overlay))
                {
                    Canvas.SetZIndex(child, 0);
                }
            }
        }
    }

    private static void RaiseColumn(FrameworkElement block)
    {
        // block -> DayColumnPanel -> cell Grid -> column Border (the colsGrid child).
        if (((block.Parent as FrameworkElement)?.Parent as FrameworkElement)?.Parent is UIElement columnBorder)
        {
            Canvas.SetZIndex(columnBorder, SelectedColumnZ);
        }
    }

    private static readonly Color SelectionRed = SelectionFrame.SelectionRed;

    // Selected blocks are left-pinned (grow rightward) and centre-scaled vertically by this modest
    // factor — just enough to lift them off the grid without growing off-screen.
    private const double SelectionScale = 1.06;

    // A red outline around the whole selected block (replaces the old drop shadow). Matches the block's
    // bounds — left-pinned (X origin 0), centred Y, same scale — so it hugs the enlarged block; the
    // corner radius follows the Settings-driven appointment corner radius.
    private Border BuildSelectionBorder(FrameworkElement block)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(SelectionRed),
            BorderThickness = new Thickness(2.5),
            CornerRadius = new CornerRadius(_templating.CornerRadius),
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = new ScaleTransform { ScaleX = SelectionScale, ScaleY = SelectionScale },
        };
        CopyColumnLayout(block, border);
        Canvas.SetZIndex(border, 51); // on top of the block (50)
        return border;
    }

    private static void CopyColumnLayout(FrameworkElement from, FrameworkElement to)
    {
        DayColumnPanel.SetStartMinutes(to, DayColumnPanel.GetStartMinutes(from));
        DayColumnPanel.SetDurationMinutes(to, DayColumnPanel.GetDurationMinutes(from));
        DayColumnPanel.SetColumnIndex(to, DayColumnPanel.GetColumnIndex(from));
        DayColumnPanel.SetColumnCount(to, DayColumnPanel.GetColumnCount(from));
    }

    /// <summary>Resize every column (header tiers, all-day, body) to fill the viewport, min-clamped.</summary>
    private void ApplyColumnWidth(double viewport)
    {
        if (_columnCount <= 0 || viewport <= 0)
        {
            return;
        }
        var colW = Math.Max(_columnMinWidth, viewport / _columnCount);
        foreach (var g in _columnGrids)
        {
            foreach (var cd in g.ColumnDefinitions)
            {
                cd.Width = new GridLength(colW);
            }
        }
    }

    /// <summary>True when <paramref name="r"/> has any appointment intersecting the visible range.</summary>
    private bool HasEventInRange(ResourceModel r, DateTime rangeStart, DateTime rangeEnd) =>
        _appointments.Any(a => a.ResourceIds.Any(id => Equals(id, r.Id)) && a.End > rangeStart && a.Start < rangeEnd);

    /// <summary>Timed events for a column: matching the column's resource (if any) and intersecting its day.</summary>
    private static IEnumerable<AppointmentModel> FilterForColumn(IReadOnlyList<AppointmentModel> nonBanner, GridColumn col)
    {
        var dayStart = col.Date.Date;
        var dayEnd = dayStart.AddDays(1);
        return nonBanner.Where(a =>
            (col.Resource is null || a.ResourceIds.Any(id => Equals(id, col.Resource.Id)))
            && a.End > dayStart && a.Start < dayEnd);
    }

    // ---- Single-tier date header (no resources) -------------------------------------------------

    private Grid BuildDateHeader(IReadOnlyList<GridColumn> columns)
    {
        var single = columns.Count == 1;
        var g = new Grid { Background = _palette.HeaderBackground };
        for (var i = 0; i < columns.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_columnMinWidth) });
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var date = columns[i].Date;
            var isToday = date.Date == DateTime.Today;
            var accent = isToday ? _palette.AccentForeground : _palette.HeaderForeground;

            var sp = new StackPanel { Padding = new Thickness(0, 6, 0, 6) };
            sp.Children.Add(new TextBlock
            {
                Text = date.ToString(single ? "dddd" : "ddd", System.Globalization.CultureInfo.CurrentCulture),
                FontSize = 12,
                Foreground = isToday ? _palette.AccentForeground : _palette.SecondaryForeground,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            sp.Children.Add(new TextBlock
            {
                Text = single ? date.ToString("MMMM d", System.Globalization.CultureInfo.CurrentCulture) : date.Day.ToString(),
                FontSize = single ? 15 : 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            var wrap = new Border
            {
                BorderBrush = _palette.MajorLine,
                BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 1),
                Child = sp,
            };
            Grid.SetColumn(wrap, i);
            g.Children.Add(wrap);
        }

        return g;
    }

    // ---- Two-tier resource header ---------------------------------------------------------------

    private Grid BuildGroupedHeader(IReadOnlyList<GridColumn> columns, int resourceCount)
    {
        var resourceMajor = _groupMode == SchedulerGroupMode.ByResource;
        var groupSize = resourceMajor ? _dates.Count : resourceCount;

        var g = new Grid { Background = _palette.HeaderBackground };
        for (var i = 0; i < columns.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_columnMinWidth) });
        }
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // tier 1 (group)
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // tier 2 (sub-column)

        if (groupSize > 0)
        {
            for (var start = 0; start < columns.Count; start += groupSize)
            {
                var first = columns[start];
                var label = resourceMajor
                    ? first.Resource!.Name
                    : first.Date.ToString("ddd, MMM d", System.Globalization.CultureInfo.CurrentCulture);
                var isToday = !resourceMajor && first.Date.Date == DateTime.Today;

                var cell = new Border
                {
                    BorderBrush = _palette.MajorLine,
                    BorderThickness = new Thickness(start == 0 ? 0 : 1, 0, 0, 1),
                    Child = new TextBlock
                    {
                        Text = label,
                        FontSize = 13,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = isToday ? _palette.AccentForeground : _palette.HeaderForeground,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(4, 4, 4, 4),
                    },
                };
                Grid.SetRow(cell, 0);
                Grid.SetColumn(cell, start);
                Grid.SetColumnSpan(cell, groupSize);
                g.Children.Add(cell);
            }
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            var isGroupStart = groupSize > 0 && i % groupSize == 0;
            var sub = resourceMajor
                ? col.Date.ToString("ddd d", System.Globalization.CultureInfo.CurrentCulture)
                : col.Resource!.Name;
            var isToday = resourceMajor && col.Date.Date == DateTime.Today;

            var cell = new Border
            {
                BorderBrush = isGroupStart ? _palette.MajorLine : _palette.MinorLine,
                BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 1),
                Child = new TextBlock
                {
                    Text = sub,
                    FontSize = 11,
                    Foreground = isToday ? _palette.AccentForeground : _palette.SecondaryForeground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(2, 3, 2, 3),
                },
            };
            Grid.SetRow(cell, 1);
            Grid.SetColumn(cell, i);
            g.Children.Add(cell);
        }

        return g;
    }

    // ---- All-day row (per-column chips) ---------------------------------------------------------

    private Grid? BuildAllDayColumns(IReadOnlyList<GridColumn> columns)
    {
        var bannerEvents = _appointments.Where(AllDayBannerRenderer.IsBannerEvent).ToList();
        if (bannerEvents.Count == 0)
        {
            return null;
        }

        // The all-day row has no pointer overlay: chips and empty cells use tap gestures. A double-tap on an
        // empty cell creates an all-day appointment for that day (and lane).
        var taps = new TapInteraction
        {
            Click = (a, ctrl) => AppointmentClicked?.Invoke(this, new AppointmentClickArgs { Appointment = a, WithCtrl = ctrl }),
            DoubleClick = a => AppointmentDoubleClicked?.Invoke(this, a),
            Context = (a, pos) => AppointmentContextMenuRequested?.Invoke(this, new AppointmentContextRequestedArgs { Appointment = a, Position = pos }),
            EmptyClick = _isReadOnly ? null : e => EmptySlotClicked?.Invoke(this, e),
            EmptyDoubleClick = _isReadOnly ? null : e => EmptySlotDoubleClicked?.Invoke(this, e),
        };

        var grid = new Grid { MaxHeight = 76 };
        for (var i = 0; i < columns.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_columnMinWidth) });
        }

        var any = false;
        for (var i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            var dayStart = col.Date.Date;
            var dayEnd = dayStart.AddDays(1);
            var chips = bannerEvents.Where(a =>
                (col.Resource is null || a.ResourceIds.Any(id => Equals(id, col.Resource.Id)))
                && a.End > dayStart && a.Start < dayEnd).ToList();

            var stack = new StackPanel { Spacing = 1, Padding = new Thickness(2, 2, 2, 2) };
            foreach (var a in chips)
            {
                any = true;
                var frame = new SelectionFrame(EventBlockFactory.CreateChip(a, _palette, _templating, showTime: false), a, 3);
                taps.AttachAppointment(frame, a);
                _allDayFrames.Add(frame);
                stack.Children.Add(frame);
            }

            var cell = new Border
            {
                BorderBrush = _palette.MinorLine,
                BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), // hit-testable empty area
                Child = stack,
            };
            taps.AttachEmptyDay(cell, col.Date, col.Resource?.Id);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return any ? grid : null;
    }

    // ---- Column body ----------------------------------------------------------------------------

    private Border BuildColumnCell(GridColumn col, bool isFirst, IReadOnlyList<EventLayout> layout)
    {
        var cell = new Grid();

        // Off-hours shading (under the today tint + gridlines): the work-hours window stays the
        // lighter base surface; the hours before workStart and after workEnd get a darker tint.
        var workStartMin = _workStart.TotalMinutes;
        var workEndMin = _workEnd.TotalMinutes;
        const double dayEndMin = 24 * 60;
        if (workEndMin > workStartMin && workStartMin >= 0 && workEndMin <= dayEndMin)
        {
            var ppm = _increment.PixelsPerMinute;
            if (workStartMin > 0)
            {
                cell.Children.Add(new Border
                {
                    Background = _palette.OffHoursBackground,
                    VerticalAlignment = VerticalAlignment.Top,
                    Height = workStartMin * ppm,
                    Margin = new Thickness(0),
                });
            }
            if (workEndMin < dayEndMin)
            {
                cell.Children.Add(new Border
                {
                    Background = _palette.OffHoursBackground,
                    VerticalAlignment = VerticalAlignment.Top,
                    Height = (dayEndMin - workEndMin) * ppm,
                    Margin = new Thickness(0, workEndMin * ppm, 0, 0),
                });
            }
        }

        if (col.Date.Date == DateTime.Today)
        {
            cell.Children.Add(new Border { Background = _palette.TodayHighlight });
        }

        cell.Children.Add(TimeGridRenderer.BuildGridlines(_increment, _palette));

        var panel = new DayColumnPanel { Increment = _increment };
        foreach (var el in layout)
        {
            var block = EventBlockFactory.CreateTimed(el.Appointment, _palette, _templating);
            DayColumnPanel.SetStartMinutes(block, el.StartMinutes);
            DayColumnPanel.SetDurationMinutes(block, el.EndMinutes - el.StartMinutes);
            DayColumnPanel.SetColumnIndex(block, el.ColumnIndex);
            DayColumnPanel.SetColumnCount(block, el.ColumnCount);
            panel.Children.Add(block);
            _timedBlocks.Add(block);
            // Adorned blocks carry a liftable overlay (attached by DecorationFactory) — collect them so the
            // build pass can re-host the pills in the shared always-on-top layer.
            if (DecorationFactory.GetOverlay(block) is not null)
            {
                _adornedBlocks.Add(block);
            }
        }
        cell.Children.Add(panel);

        return new Border
        {
            BorderBrush = _palette.MajorLine,
            BorderThickness = new Thickness(isFirst ? 0 : 1, 0, 0, 0),
            Child = cell,
        };
    }
}
