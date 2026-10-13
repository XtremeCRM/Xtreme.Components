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
    private DayOfWeek _firstDayOfWeek = DayOfWeek.Sunday;
    private PickMode _mode = PickMode.Days;

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
        DaysModeToggle.IsChecked = true;
        Loaded += (_, _) => RenderMonths(force: true);
        ActualThemeChanged += (_, _) => RenderMonths(force: true); // repaint weekend/divider colors on theme switch
    }

    /// <summary>Raised whenever the picked set changes (host updates its summary + Apply enablement).</summary>
    public event EventHandler? SelectionChanged;

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

    /// <summary>The first day of the week for the month grids (matches the scheduler setting).</summary>
    public void SetFirstDayOfWeek(DayOfWeek day)
    {
        if (_firstDayOfWeek == day)
        {
            return;
        }
        _firstDayOfWeek = day;
        RenderMonths(force: true);
    }

    /// <summary>Seed the selection (e.g. the dates currently on screen) and open on the first of them.</summary>
    public void SetSelection(IReadOnlyList<DateTime> dates)
    {
        _selected.Clear();
        foreach (var d in dates)
        {
            _selected.Add(d.Date);
        }
        _anchor = null;
        _pendingRangeAnchor = null;

        var first = _selected.Count > 0 ? _selected.Min() : DateTime.Today;
        _anchorDate = first;
        _pendingAnchorAdjust = true;
        _baseMonth = new DateTime(first.Year, first.Month, 1);
        RenderMonths(force: true);
    }

    /// <summary>The current picked set, sorted ascending.</summary>
    public IReadOnlyList<DateTime> GetSelection() => _selected.OrderBy(d => d).ToArray();

    /// <summary>Empty the selection (the Clear button).</summary>
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
        var selectToday = SingleDateMode;
        if (selectToday)
        {
            _selected.Clear();
            _selected.Add(DateTime.Today);
            _anchor = DateTime.Today;
        }

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
            var dow = (DayOfWeek)(((int)_firstDayOfWeek + c) % 7);
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
        var date = raw.Date;
        var mods = e.KeyModifiers;
        var ctrl = mods.HasFlag(VirtualKeyModifiers.Control);
        var shift = mods.HasFlag(VirtualKeyModifiers.Shift);
        e.Handled = true;

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
        // while staying obviously live rather than greyed-out-disabled.
        var outOfScopeFill = palette.OutsideMonthFill;

        foreach (var (date, cell, outOfScope) in _cells)
        {
            var selected = _selected.Contains(date);
            var isToday = date == DateTime.Today;
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            cell.Background = selected ? accent : outOfScope ? outOfScopeFill : transparent;
            cell.BorderBrush = isToday ? accent : transparent;
            cell.BorderThickness = new Thickness(isToday ? 1.5 : 0);

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
        var diff = (7 + (int)date.DayOfWeek - (int)_firstDayOfWeek) % 7;
        return date.Date.AddDays(-diff);
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
