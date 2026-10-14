using System.Globalization;
using Windows.System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Xtreme.Components.Primitives;

/// <summary>
/// A resizable multi-month date navigator — our own control, replacing WinUI's <c>CalendarView</c>
/// (which locks to a rolling week-strip with no month boundaries and no range gesture). Mirrors the
/// Legacy DevExpress DateNavigator: a grid of month tiles that reflows to show as many months as fit
/// the surface, with click / Ctrl+click / Shift+click selection plus an on-screen Days/Range mode for
/// touch. Owns only the pick; the host reads <see cref="GetSelection"/> on Apply and listens to
/// <see cref="SelectionChanged"/> for live summary/enablement.
///
/// Keyboard: the day grid is one tab stop. Arrows move a focus cell (Up/Down by a week), PageUp /
/// PageDown by a month, Home / End to the week's ends; Enter or Space picks the focus day exactly as
/// a click would, including Ctrl and Shift.
/// </summary>
public sealed partial class MultiMonthDateNavigator : UserControl
{
    private enum PickMode { Days, Range }

    // Month-tile metrics (px) used to decide how many tiles fit the current surface.
    private const double TileWidth = 214;
    private const double TileHeight = 206;
    private const double TileGap = 8;
    private const double CellWidth = 30;
    private const double CellHeight = 28;

    private readonly HashSet<DateTime> _selected = new();
    private readonly List<(DateTime Date, Border Cell, bool OutOfScope)> _cells = new();

    // The first (top-left) month currently shown. Reflow renders forward from here.
    private DateTime _baseMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private PickMode _mode = PickMode.Days;

    // Keyboard state: the day the arrows move, whether the grid holds keyboard focus (the focus cell
    // is drawn only then), and the modifier keys, tracked from KeyDown/KeyUp.
    private DateTime? _focusDate;
    private bool _keyboardFocused;
    private bool _ctrlDown;
    private bool _shiftDown;

    // The date the navigator should open on (today, or the earliest seeded selection). When more than one
    // month is visible AND this date falls in the first displayed week of its month, RenderMonths backs
    // _baseMonth up one month so the tail of the previous month shows for context. Applied once per (re)seed;
    // the visible-month count is only known after layout, so the decision is deferred to RenderMonths.
    private DateTime _anchorDate = DateTime.Today;
    private bool _pendingAnchorAdjust = true;

    // Shift-range / mode anchors. _anchor is the last plain/ctrl pick (Shift ranges from it);
    // _pendingRangeAnchor is the first tap of a two-tap Range-mode range (null = awaiting the first).
    private DateTime? _anchor;
    private DateTime? _pendingRangeAnchor;

    private int _cols = -1;
    private int _rows = -1;

    public MultiMonthDateNavigator()
    {
        InitializeComponent();
        // The culture's first day as a local value, so XAML / a binding / SetFirstDayOfWeek replace it.
        FirstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        DaysModeToggle.IsChecked = true;
        Loaded += (_, _) => RenderMonths(force: true);
        ActualThemeChanged += (_, _) => RenderMonths(force: true); // repaint weekend/divider colors on theme switch
    }

    /// <summary>
    /// Raised whenever the picked set changes, whoever changed it: a click or key, Today in
    /// <see cref="SingleDateMode"/>, <see cref="SetSelection"/> or <see cref="ClearSelection"/>.
    /// Not raised when a call leaves the set as it was.
    /// </summary>
    public event EventHandler? SelectionChanged;

    public static readonly DependencyProperty FirstDayOfWeekProperty = DependencyProperty.Register(
        nameof(FirstDayOfWeek), typeof(DayOfWeek), typeof(MultiMonthDateNavigator),
        new PropertyMetadata(DayOfWeek.Sunday, OnFirstDayOfWeekChanged));

    /// <summary>
    /// First column of each month grid. Defaults to <see cref="CultureInfo.CurrentCulture"/>'s
    /// <c>DateTimeFormat.FirstDayOfWeek</c> at construction (Sunday for en-US, Monday for most of Europe).
    /// </summary>
    public DayOfWeek FirstDayOfWeek
    {
        get => (DayOfWeek)GetValue(FirstDayOfWeekProperty);
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    private static void OnFirstDayOfWeekChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MultiMonthDateNavigator nav)
            nav.RenderMonths(force: true);
    }

    /// <summary>
    /// Single-date mode for use as a date FIELD rather than a scheduler range picker.
    /// Hides the Days/Range switch, makes a tap REPLACE the selection instead of toggling it
    /// (and ignores Ctrl/Shift), and — because one value can't span months — lets the single
    /// visible tile fill its leading/trailing slots with the neighbouring months' days.
    /// </summary>
    public static readonly DependencyProperty SingleDateModeProperty = DependencyProperty.Register(
        nameof(SingleDateMode), typeof(bool), typeof(MultiMonthDateNavigator),
        new PropertyMetadata(false, OnSingleDateModeChanged));

    public bool SingleDateMode
    {
        get => (bool)GetValue(SingleDateModeProperty);
        set => SetValue(SingleDateModeProperty, value);
    }

    private static void OnSingleDateModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MultiMonthDateNavigator nav) return;

        nav.ModeSwitch.Visibility = nav.SingleDateMode ? Visibility.Collapsed : Visibility.Visible;
        if (nav.SingleDateMode)
        {
            nav._mode = PickMode.Days;
            nav._pendingRangeAnchor = null;
            nav.DaysModeToggle.IsChecked = true;
            nav.RangeModeToggle.IsChecked = false;
        }
        nav.RenderMonths(force: true);
    }

    /// <summary>The first day of the week for the month grids (matches the scheduler setting).
    /// Same as setting <see cref="FirstDayOfWeek"/>.</summary>
    public void SetFirstDayOfWeek(DayOfWeek day) => FirstDayOfWeek = day;

    /// <summary>
    /// Seed the selection (e.g. the dates currently on screen) and open on the first of them.
    /// Raises <see cref="SelectionChanged"/> when the set differs from the current one.
    /// </summary>
    public void SetSelection(IReadOnlyList<DateTime> dates)
    {
        var next = new HashSet<DateTime>(dates.Select(d => d.Date));
        var changed = !next.SetEquals(_selected);

        _selected.Clear();
        _selected.UnionWith(next);
        _anchor = null;
        _pendingRangeAnchor = null;

        var first = _selected.Count > 0 ? _selected.Min() : DateTime.Today;
        _anchorDate = first;
        _pendingAnchorAdjust = true;
        _baseMonth = new DateTime(first.Year, first.Month, 1);
        _focusDate = null;
        RenderMonths(force: true);

        if (changed)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The current picked set, sorted ascending.</summary>
    public IReadOnlyList<DateTime> GetSelection() => _selected.OrderBy(d => d).ToArray();

    /// <summary>Empty the selection (the Clear button). Raises <see cref="SelectionChanged"/> when
    /// something was selected.</summary>
    public void ClearSelection()
    {
        if (_selected.Count == 0 && _pendingRangeAnchor is null)
        {
            return;
        }
        _selected.Clear();
        _pendingRangeAnchor = null;
        ApplyHighlight();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Header actions ------------------------------------------------------------------------

    private void OnPrevMonths(object sender, RoutedEventArgs e) => PageMonths(-1);

    private void OnNextMonths(object sender, RoutedEventArgs e) => PageMonths(+1);

    // Page by the number of months currently visible so a full grid scrolls a screenful at a time.
    private void PageMonths(int direction)
    {
        var step = Math.Max(1, _cols * _rows);
        _baseMonth = _baseMonth.AddMonths(direction * step);
        RenderMonths(force: true);
    }

    private void OnTodayJump(object sender, RoutedEventArgs e)
    {
        _anchorDate = DateTime.Today;
        _pendingAnchorAdjust = true;
        _baseMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        // In a date field, "Today" means SET the value to today — paging there without selecting
        // leaves the field on its old value, which reads as the button doing nothing. In range mode
        // it stays a pure navigation jump so it can't disturb a half-built range.
        var selectToday = SingleDateMode && !(_selected.Count == 1 && _selected.Contains(DateTime.Today));
        if (SingleDateMode)
        {
            _selected.Clear();
            _selected.Add(DateTime.Today);
            _anchor = DateTime.Today;
        }
        _focusDate = DateTime.Today;

        RenderMonths(force: true); // re-renders and re-highlights

        if (selectToday)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnModeToggle(object sender, RoutedEventArgs e)
    {
        _mode = ReferenceEquals(sender, RangeModeToggle) ? PickMode.Range : PickMode.Days;
        DaysModeToggle.IsChecked = _mode == PickMode.Days;
        RangeModeToggle.IsChecked = _mode == PickMode.Range;
        _pendingRangeAnchor = null; // switching modes abandons a half-built range
        ApplyHighlight();
    }

    // ---- Reflow --------------------------------------------------------------------------------

    private void OnMonthsHostSizeChanged(object sender, SizeChangedEventArgs e) => RenderMonths(force: false);

    // Build the month grid sized to the current surface. force=true always rebuilds (selection/base/first-day
    // change); otherwise it only rebuilds when the fitted column/row count actually changes (a resize bucket).
    private void RenderMonths(bool force)
    {
        var w = MonthsHost.ActualWidth;
        var h = MonthsHost.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return; // laid out yet? the Loaded/SizeChanged pass will call back with real bounds
        }

        var cols = Math.Max(1, (int)((w + TileGap) / (TileWidth + TileGap)));
        var rows = Math.Max(1, (int)((h + TileGap) / (TileHeight + TileGap)));
        if (!force && cols == _cols && rows == _rows)
        {
            return;
        }
        _cols = cols;
        _rows = rows;

        // Now that we know how many months fit, apply the one-shot open-position rule: when multiple months
        // show and the anchor is in the first week of its month, open one month earlier for context.
        if (_pendingAnchorAdjust)
        {
            _baseMonth = InitialBaseMonth(_anchorDate, cols * rows);
            _pendingAnchorAdjust = false;
        }

        _cells.Clear();
        MonthsHost.Children.Clear();

        // Auto-sized, centered tile grid so months pack tightly (no star-column spread). MonthsHost stays
        // stretched so its ActualWidth/Height still reports the available space for the fit calc above.
        // Between each month column/row is a 1px separator track holding a thin divider line.
        var tileGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var gridCols = cols * 2 - 1;
        var gridRows = rows * 2 - 1;
        for (var c = 0; c < gridCols; c++)
        {
            tileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = c % 2 == 0 ? GridLength.Auto : new GridLength(1) });
        }
        for (var r = 0; r < gridRows; r++)
        {
            tileGrid.RowDefinitions.Add(new RowDefinition { Height = r % 2 == 0 ? GridLength.Auto : new GridLength(1) });
        }

        // XU-413: palette, not Application.Current.Resources. A brush read from app scope resolves
        // against the APPLICATION theme, which stays Light while this control renders Dark. The
        // palette is keyed off ActualTheme (this element), and the ctor already re-renders on
        // ActualThemeChanged, so a theme switch repaints correctly.
        var divider = CalendarPalette.For(ActualTheme).MinorLine;

        var count = cols * rows;

        // Adjacent-month days are only safe to draw when a SINGLE tile is on screen. With two or
        // more tiles the same day would appear twice (once as a real day, once as a neighbour's
        // spill-over) and a selection would highlight in both places.
        var single = count == 1;

        for (var i = 0; i < count; i++)
        {
            var month = _baseMonth.AddMonths(i);
            var tile = BuildMonthTile(month, fillAdjacent: single, showTitle: !single);
            Grid.SetColumn(tile, i % cols * 2);
            Grid.SetRow(tile, i / cols * 2);
            tileGrid.Children.Add(tile);
        }

        // Vertical dividers between month columns (span the full grid height).
        for (var c = 1; c < gridCols; c += 2)
        {
            var line = new Border { Width = 1, Background = divider, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
            Grid.SetColumn(line, c);
            Grid.SetRow(line, 0);
            Grid.SetRowSpan(line, gridRows);
            tileGrid.Children.Add(line);
        }

        // Horizontal dividers between month rows (span the full grid width).
        for (var r = 1; r < gridRows; r += 2)
        {
            var line = new Border { Height = 1, Background = divider, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(line, r);
            Grid.SetColumn(line, 0);
            Grid.SetColumnSpan(line, gridCols);
            tileGrid.Children.Add(line);
        }

        MonthsHost.Children.Add(tileGrid);

        var last = _baseMonth.AddMonths(count - 1);
        // One tile carries its month in the header instead of its own title ("Aug – Aug 2026"
        // reads as a bug, and two month captions stacked is wasted vertical space). Abbreviated
        // month to match the multi-month label — the full name clips a long one ("September")
        // against the arrows and Today button at single-tile width.
        MonthRangeLabel.Text = single
            ? $"{_baseMonth:MMM yyyy}"
            : _baseMonth.Year == last.Year
                ? $"{_baseMonth:MMM} – {last:MMM yyyy}"
                : $"{_baseMonth:MMM yyyy} – {last:MMM yyyy}";

        ApplyHighlight();
    }

    /// <summary>
    /// The height this navigator needs to show one whole month: its header row plus one month tile.
    /// A host that sizes the navigator (a resizable picker panel, say) can keep it at least this tall so
    /// the month grid never ends up empty. Zero until the header has been laid out.
    /// </summary>
    public double OneMonthHeight => HeaderRow.ActualHeight <= 0 ? 0 : HeaderRow.ActualHeight + LayoutRoot.RowSpacing + TileHeight;

    /// <summary>How many month tiles are on screen in full: inside both this control's bounds and the
    /// window. A self-test probe for the hosting picker (a tile clipped away by a too-small host or
    /// the window edge does not count).</summary>
    internal int VisibleMonthCount
    {
        get
        {
            if (XamlRoot?.Content is not UIElement root || MonthsHost.Children.Count == 0 || MonthsHost.Children[0] is not Grid tileGrid)
            {
                return 0;
            }
            var visible = TransformToVisual(root).TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
            visible.Intersect(new Rect(0, 0, XamlRoot.Size.Width, XamlRoot.Size.Height));
            var shown = 0;
            foreach (var tile in tileGrid.Children.OfType<FrameworkElement>().Take(_cols * _rows))
            {
                var r = tile.TransformToVisual(root).TransformBounds(new Rect(0, 0, tile.ActualWidth, tile.ActualHeight));
                var height = r.Height;
                r.Intersect(visible);
                if (!r.IsEmpty && height > 0 && r.Height >= height - 1)
                {
                    shown++;
                }
            }
            return shown;
        }
    }

    // ---- Month tile ----------------------------------------------------------------------------

    private FrameworkElement BuildMonthTile(DateTime month, bool fillAdjacent, bool showTitle)
    {
        var ci = CultureInfo.CurrentCulture;
        var panel = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (showTitle)
        {
            panel.Children.Add(new TextBlock
            {
                Text = month.ToString("MMMM yyyy", ci),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            });
        }

        var grid = new Grid();
        for (var c = 0; c < 7; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) }); // weekday header
        for (var r = 0; r < 6; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHeight) });
        }

        // Weekday header row, ordered from the configured first day.
        var dayNames = ci.DateTimeFormat.AbbreviatedDayNames; // index by DayOfWeek
        for (var c = 0; c < 7; c++)
        {
            var dow = (DayOfWeek)(((int)FirstDayOfWeek + c) % 7);
            var hdr = new TextBlock
            {
                Text = dayNames[(int)dow].Length >= 2 ? dayNames[(int)dow].Substring(0, 2) : dayNames[(int)dow],
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = CalendarPalette.For(ActualTheme).SecondaryForeground,
            };
            Grid.SetColumn(hdr, c);
            Grid.SetRow(hdr, 0);
            grid.Children.Add(hdr);
        }

        // Six weeks starting on the first-day-of-week on/before the 1st. Leading/trailing cells from
        // adjacent months are blank by default, so a day never appears twice across neighbouring tiles
        // (which would double-highlight a selection). When only ONE tile is on screen that collision
        // can't happen, so those slots are filled in — muted, but fully selectable.
        var firstCell = StartOfWeek(new DateTime(month.Year, month.Month, 1));
        for (var i = 0; i < 42; i++)
        {
            var date = firstCell.AddDays(i);
            var outOfScope = date.Month != month.Month;
            if (outOfScope && !fillAdjacent)
            {
                continue; // blank cell for spill-over days
            }
            var cell = BuildDayCell(date, outOfScope);
            Grid.SetColumn(cell, i % 7);
            Grid.SetRow(cell, 1 + i / 7);
            grid.Children.Add(cell);
        }

        panel.Children.Add(grid);
        return new Border { Child = panel, Margin = new Thickness(TileGap / 2) };
    }

    private Border BuildDayCell(DateTime date, bool outOfScope)
    {
        var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        var text = new TextBlock
        {
            Text = date.Day.ToString(CultureInfo.CurrentCulture),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ForegroundFor(isWeekend, selected: false, outOfScope),
        };
        var cell = new Border
        {
            Child = text,
            Width = CellWidth,
            Height = CellHeight,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Tag = date,
        };
        cell.PointerPressed += OnDayPressed;
        _cells.Add((date, cell, outOfScope));
        return cell;
    }

    // ---- Selection gestures --------------------------------------------------------------------

    private void OnDayPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border { Tag: DateTime raw })
        {
            return;
        }
        var mods = e.KeyModifiers;
        e.Handled = true;

        // A click also moves the keyboard focus cell, so arrows carry on from the clicked day.
        _focusDate = raw.Date;
        DayGridFocus.Focus(FocusState.Pointer);

        Pick(raw.Date, mods.HasFlag(VirtualKeyModifiers.Control), mods.HasFlag(VirtualKeyModifiers.Shift));
    }

    // One pick gesture, from a click or from Enter/Space on the focus cell.
    private void Pick(DateTime date, bool ctrl, bool shift)
    {
        if (SingleDateMode)
        {
            // A field holds exactly one value: replace rather than toggle (so tapping the
            // selected day doesn't clear it), and ignore Ctrl/Shift — there is no set to extend.
            _selected.Clear();
            _selected.Add(date);
            _anchor = date;

            // Tapping a neighbouring month's day pages to that month, so the selection never
            // ends up sitting in a spill-over row of the month you're no longer looking at.
            if (date.Year != _baseMonth.Year || date.Month != _baseMonth.Month)
            {
                _baseMonth = new DateTime(date.Year, date.Month, 1);
                RenderMonths(force: true); // re-renders and re-highlights
            }
            else
            {
                ApplyHighlight();
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (shift && _anchor is { } anchor)
        {
            AddRange(anchor, date); // Shift+click always extends a contiguous range from the anchor
        }
        else if (ctrl)
        {
            Toggle(date); // Ctrl+click always toggles a single day in/out
            _anchor = date;
        }
        else if (_mode == PickMode.Range)
        {
            // Touch-friendly two-tap range. The first tap SELECTS its day now (so it's counted and Apply
            // enables immediately — a one-day pick is valid) and arms it as the anchor; the second tap
            // extends the range to [start..end]. A third tap starts a fresh range.
            if (_pendingRangeAnchor is { } start)
            {
                AddRange(start, date);
                _pendingRangeAnchor = null;
                _anchor = date;
            }
            else
            {
                _selected.Add(date);
                _pendingRangeAnchor = date;
                _anchor = date;
            }
        }
        else
        {
            // Days mode plain tap: toggle (build a scattered set by tapping — the "N Saturdays" case).
            Toggle(date);
            _anchor = date;
        }

        ApplyHighlight();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Toggle(DateTime date)
    {
        if (!_selected.Remove(date))
        {
            _selected.Add(date);
        }
    }

    private void AddRange(DateTime a, DateTime b)
    {
        var start = a <= b ? a : b;
        var end = a <= b ? b : a;
        for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
        {
            _selected.Add(d);
        }
    }

    // ---- Visuals -------------------------------------------------------------------------------

    private void ApplyHighlight()
    {
        // XU-413: all of these come from the theme-keyed palette now - see the note in RenderMonths.
        var palette = CalendarPalette.For(ActualTheme);
        var accent = palette.SelectionFill;
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        // Adjacent-month days sit on a faint fill so the current month still reads as a block,
        // while staying obviously live rather than grayed-out-disabled.
        var outOfScopeFill = palette.OutsideMonthFill;

        foreach (var (date, cell, outOfScope) in _cells)
        {
            var selected = _selected.Contains(date);
            var isToday = date == DateTime.Today;
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var isFocus = _keyboardFocused && date == _focusDate;

            cell.Background = selected ? accent : outOfScope ? outOfScopeFill : transparent;
            // The keyboard focus cell outlines in the primary text color; it outranks the today outline.
            cell.BorderBrush = isFocus ? palette.HeaderForeground : isToday ? accent : transparent;
            cell.BorderThickness = new Thickness(isFocus ? 2 : isToday ? 1.5 : 0);

            if (cell.Child is TextBlock tb)
            {
                tb.Foreground = ForegroundFor(isWeekend, selected, outOfScope);
            }
        }
    }

    private Brush ForegroundFor(bool isWeekend, bool selected, bool outOfScope)
    {
        // XU-413: every role resolves from the theme-keyed palette rather than app scope.
        var palette = CalendarPalette.For(ActualTheme);
        if (selected)
        {
            return palette.SelectionForeground;
        }
        if (outOfScope)
        {
            // Muted enough to recede behind the current month, dark enough to still read as clickable.
            return palette.OutsideMonthForeground;
        }
        // Weekends read in a muted red for parity with the Legacy DateNavigator (theme-tuned palette).
        return isWeekend ? palette.WeekendForeground : palette.HeaderForeground;
    }

    private DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (int)date.DayOfWeek - (int)FirstDayOfWeek) % 7;
        return date.Date.AddDays(-diff);
    }

    // ---- Keyboard ------------------------------------------------------------------------------

    private void OnGridGotFocus(object sender, RoutedEventArgs e)
    {
        _keyboardFocused = DayGridFocus.FocusState == FocusState.Keyboard;
        if (_focusDate is null || !IsVisibleMonth(_focusDate.Value))
        {
            // Start on the earliest visible selected day, else today when visible, else the 1st shown.
            var visibleSelected = _selected.Where(IsVisibleMonth).OrderBy(d => d).ToList();
            _focusDate = visibleSelected.Count > 0 ? visibleSelected[0]
                       : IsVisibleMonth(DateTime.Today) ? DateTime.Today
                       : _baseMonth;
        }
        ApplyHighlight();
    }

    private void OnGridLostFocus(object sender, RoutedEventArgs e)
    {
        _keyboardFocused = false;
        _ctrlDown = _shiftDown = false;
        ApplyHighlight();
    }

    private void OnGridKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl) _ctrlDown = false;
        if (e.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift) _shiftDown = false;
    }

    private void OnGridKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl:
                _ctrlDown = true;
                return;
            case VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift:
                _shiftDown = true;
                return;
        }

        var focus = _focusDate ?? DateTime.Today;
        DateTime? next = e.Key switch
        {
            VirtualKey.Left => focus.AddDays(-1),
            VirtualKey.Right => focus.AddDays(1),
            VirtualKey.Up => focus.AddDays(-7),
            VirtualKey.Down => focus.AddDays(7),
            VirtualKey.PageUp => focus.AddMonths(-1),
            VirtualKey.PageDown => focus.AddMonths(1),
            VirtualKey.Home => StartOfWeek(focus),
            VirtualKey.End => StartOfWeek(focus).AddDays(6),
            _ => null,
        };

        if (next is { } moved)
        {
            e.Handled = true;
            _keyboardFocused = true;
            _focusDate = moved;
            if (!IsVisibleMonth(moved))
            {
                // Scroll just far enough: the focus month becomes the first or the last tile.
                var count = Math.Max(1, _cols * _rows);
                var month = new DateTime(moved.Year, moved.Month, 1);
                _baseMonth = moved < _baseMonth ? month : month.AddMonths(-(count - 1));
                RenderMonths(force: true);
            }
            else
            {
                ApplyHighlight();
            }
            return;
        }

        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            e.Handled = true;
            _keyboardFocused = true;
            _focusDate = focus;
            Pick(focus, _ctrlDown, _shiftDown);
        }
    }

    // True when the date's month is one of the tiles on screen.
    private bool IsVisibleMonth(DateTime date)
    {
        var count = Math.Max(1, _cols * _rows);
        var month = new DateTime(date.Year, date.Month, 1);
        return month >= _baseMonth && month < _baseMonth.AddMonths(count);
    }

    // The month to open on: the anchor's month, or — when more than one month is visible AND the anchor sits
    // in the first displayed week of its month — the PREVIOUS month, so the tail end of it shows for context
    // instead of opening on anchor-month..+N (which hides the just-past days at the end of the prior month).
    // With only one month visible there's no room for context, so keep the anchor's month.
    private DateTime InitialBaseMonth(DateTime anchor, int visibleMonths)
    {
        var firstOfMonth = new DateTime(anchor.Year, anchor.Month, 1);
        if (visibleMonths > 1 && anchor.Date <= StartOfWeek(firstOfMonth).AddDays(6))
        {
            return firstOfMonth.AddMonths(-1);
        }
        return firstOfMonth;
    }
}
