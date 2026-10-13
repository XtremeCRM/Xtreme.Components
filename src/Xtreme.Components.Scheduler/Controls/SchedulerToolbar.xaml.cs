using Microsoft.UI.Xaml.Controls.Primitives;
using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Controls;

/// <summary>
/// The scheduler's navigation bar: previous / today / next, a date-range picker (the period
/// button opens a calendar where a single tap picks a day and a start+end pick a multi-day
/// range), a view switcher, and a Settings flyout (tick spans, density, grouping, hide-idle).
/// Raises events for the host <see cref="SchedulerControl"/>; owns no state beyond its visuals.
/// </summary>
public sealed partial class SchedulerToolbar : UserControl
{
    // Below this control width the segmented view switcher collapses to a dropdown picker.
    private const double NarrowThreshold = 720;

    private readonly ToggleButton[] _toggles;

    // Suppresses change events while we set control state programmatically.
    private bool _suppress;

    // The dates currently shown on screen, reflected as the picker's selection when its flyout opens.
    private IReadOnlyList<DateTime> _currentDates = new[] { DateTime.Today };

    public SchedulerToolbar()
    {
        InitializeComponent();
        _toggles = ViewSegmentPanel.Children.OfType<ToggleButton>().ToArray();
        BuildViewDropdown();
        SizeChanged += OnSizeChanged;
    }

    // Mirror the segmented options into the narrow-screen dropdown so both switchers share one
    // source of truth — the segments declared in XAML. Add a segment there and the dropdown follows.
    private void BuildViewDropdown()
    {
        foreach (var t in _toggles)
        {
            ViewCombo.Items.Add(new ComboBoxItem { Content = t.Content, Tag = t.Tag });
        }
    }

    public event EventHandler? PreviousClicked;
    public event EventHandler? NextClicked;
    public event EventHandler? TodayClicked;
    public event EventHandler<SchedulerToolbarButton>? CustomButtonClicked;
    public event EventHandler<SchedulerView>? ViewSelected;
    public event EventHandler<SchedulerGroupMode>? GroupModeChanged;
    public event EventHandler<bool>? HideIdleChanged;
    public event EventHandler<bool>? ShowTilesChanged;
    public event EventHandler<SchedulerTileOption>? TileOptionToggled;
    public event EventHandler<TimeIncrementConfig>? TimeIncrementChanged;
    public event EventHandler<double>? ColumnMinWidthChanged;
    public event EventHandler<double>? CornerRadiusChanged;
    public event EventHandler<DayOfWeek>? FirstDayOfWeekChanged;
    public event EventHandler<(TimeSpan Start, TimeSpan End)>? WorkHoursChanged;

    /// <summary>Raised when the user commits a date selection (Apply). Carries every picked day — one
    /// entry for a single-day pick, a run for a contiguous range, or a scattered set for multi-date. The
    /// host <see cref="SchedulerControl"/> classifies it (contiguous folds to the anchor/span path;
    /// scattered engages the multi-date columns).</summary>
    public event EventHandler<IReadOnlyList<DateTime>>? DatesSelected;

    /// <summary>Set the period button caption (e.g. "June 2026" or "Jun 8 – 14, 2026").</summary>
    public void SetPeriodLabel(string text) => PeriodButton.Content = text;

    /// <summary>Select <paramref name="view"/> in both the segmented switcher and the dropdown
    /// (whichever is currently shown), clearing the others. No event raised.</summary>
    public void SetActiveView(SchedulerView view)
    {
        var name = view.ToString();
        foreach (var t in _toggles)
        {
            t.IsChecked = (t.Tag as string) == name;
        }

        _suppress = true;
        foreach (var obj in ViewCombo.Items)
        {
            if (obj is ComboBoxItem { Tag: string s } item && s == name)
            {
                ViewCombo.SelectedItem = item;
                break;
            }
        }
        _suppress = false;
    }

    /// <summary>The set of dates currently on screen — reflected as the picker's selection when its
    /// flyout next opens. Sorted + de-duplicated; empty falls back to today.</summary>
    public void SetCurrentDates(IReadOnlyList<DateTime> dates)
    {
        var normalized = dates.Select(d => d.Date).Distinct().OrderBy(d => d).ToArray();
        _currentDates = normalized.Length > 0 ? normalized : new[] { DateTime.Today };
    }

    /// <summary>Reflect the grouping order in the Settings radios (no event raised).</summary>
    public void SetGroupMode(SchedulerGroupMode mode)
    {
        _suppress = true;
        GroupResDateRadio.IsChecked = mode == SchedulerGroupMode.ByResource;
        GroupDateResRadio.IsChecked = mode == SchedulerGroupMode.ByDay;
        _suppress = false;
    }

    /// <summary>Reflect the hide-idle state in the Settings switch (no event raised).</summary>
    public void SetHideIdle(bool hide)
    {
        _suppress = true;
        HideIdleSwitch.IsOn = hide;
        _suppress = false;
    }

    /// <summary>Reflect the show-tiles state in the Settings switch (no event raised).</summary>
    public void SetShowTiles(bool show)
    {
        _suppress = true;
        ShowTilesSwitch.IsOn = show;
        _suppress = false;
    }

    /// <summary>Render the host-supplied per-tile on/off options into the "Visible tiles" menu (cleared +
    /// rebuilt) as one <see cref="ToggleMenuFlyoutItem"/> each. Toggling one raises
    /// <see cref="TileOptionToggled"/>. Pass an empty list to clear them (the Tiles button hides).</summary>
    public void SetTileOptions(IReadOnlyList<SchedulerTileOption> options)
    {
        TileOptionsMenu.Items.Clear();
        foreach (var o in options)
        {
            // Setting IsChecked here doesn't raise Click (only user toggles do), so no suppression needed.
            var item = new ToggleMenuFlyoutItem { Text = o.Label, IsChecked = o.IsOn, Tag = o };
            item.Click += OnTileOptionChanged;
            TileOptionsMenu.Items.Add(item);
        }
        TilesButton.Visibility = options.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTileOptionChanged(object sender, RoutedEventArgs e)
    {
        // ToggleMenuFlyoutItem flips IsChecked before raising Click, so it already holds the new state.
        if (sender is ToggleMenuFlyoutItem { Tag: SchedulerTileOption o } item)
        {
            TileOptionToggled?.Invoke(this, new SchedulerTileOption { Key = o.Key, Label = o.Label, IsOn = item.IsChecked });
        }
    }

    /// <summary>Reflect the current time scale in the Settings controls (no event raised).</summary>
    public void SetTimeIncrement(TimeIncrementConfig increment)
    {
        _suppress = true;
        SelectByTag(MajorTickCombo, (int)increment.MajorTick.TotalMinutes);
        SelectByTag(MinorTickCombo, (int)increment.MinorTick.TotalMinutes);
        ZoomSlider.Value = increment.PixelsPerMinute;
        _suppress = false;
    }

    /// <summary>Reflect the current minimum column width in the Settings slider (no event raised).</summary>
    public void SetColumnMinWidth(double width)
    {
        _suppress = true;
        ColumnWidthSlider.Value = width;
        _suppress = false;
    }

    /// <summary>Reflect the current appointment corner radius in the Settings slider (no event raised).</summary>
    public void SetCornerRadius(double radius)
    {
        _suppress = true;
        CornerRadiusSlider.Value = radius;
        _suppress = false;
    }

    /// <summary>Reflect the current first-day-of-week in the Settings combo (no event raised). Also
    /// remembered for laying out the date-navigator's month grids.</summary>
    public void SetFirstDayOfWeek(DayOfWeek day)
    {
        _firstDay = day;
        _suppress = true;
        SelectByTag(FirstDayCombo, (int)day);
        _suppress = false;
    }

    /// <summary>Reflect the current work hours in the Settings combos (no event raised).</summary>
    public void SetWorkHours(TimeSpan start, TimeSpan end)
    {
        _suppress = true;
        SelectByTag(WorkStartCombo, (int)start.TotalMinutes);
        SelectByTag(WorkEndCombo, (int)end.TotalMinutes);
        _suppress = false;
    }

    /// <summary>Show/hide the grouping + hide-idle section (only the resource-aware views use it).</summary>
    public void SetResourceOptionsVisible(bool visible) =>
        GroupingSection.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Rebuild the host-supplied custom buttons (cleared + re-rendered). Each button shows its
    /// glyph and/or label with its tooltip; clicking runs its command and raises
    /// <see cref="CustomButtonClicked"/>. Pass an empty list to clear them.</summary>
    public void SetCustomButtons(IReadOnlyList<SchedulerToolbarButton> buttons)
    {
        CustomButtonPanel.Children.Clear();
        foreach (var model in buttons)
        {
            CustomButtonPanel.Children.Add(BuildToolbarButton(model, RaiseCustom));
        }

        // Custom buttons usually arrive after the first layout pass; re-evaluate overflow against the
        // current width so a newly-added button that doesn't fit lands in the hamburger immediately.
        ApplyResponsiveLayout(ActualWidth);
    }

    // Run the entry's command, then raise the custom-button click for it.
    private void RaiseCustom(SchedulerToolbarButton model)
    {
        model.Command?.Execute(model.CommandParameter);
        CustomButtonClicked?.Invoke(this, model);
    }

    // Run the entry's command, then raise the adornment click for it.
    private void RaiseAdornment(SchedulerToolbarButton model)
    {
        model.Command?.Execute(model.CommandParameter);
        AdornmentClicked?.Invoke(this, model);
    }

    // One inline toolbar button for a host model (custom or adornment group): icon/label, tooltip, the pressed
    // accent look for a checked toggle, and either a drop-down of MenuItems or a click that runs `raise`.
    private Button BuildToolbarButton(SchedulerToolbarButton model, Action<SchedulerToolbarButton> raise)
    {
        var button = new Button
        {
            Tag = model,
            IsEnabled = model.IsEnabled,
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = BuildButtonContent(model),
        };
        if (!string.IsNullOrEmpty(model.Tooltip))
        {
            ToolTipService.SetToolTip(button, model.Tooltip);
        }
        // A pressed toggle renders in the platform accent style — its ThemeResources follow light/dark and
        // high contrast, so the library stays app-agnostic. Unpressed toggles keep the default look.
        if (model.IsToggle && model.IsChecked
            && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent)
            && accent is Style accentStyle)
        {
            button.Style = accentStyle;
        }
        if (model.MenuItems is { Count: > 0 } items)
        {
            // A menu button: the entries act, the button only opens them.
            var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
            foreach (var entry in items)
            {
                menu.Items.Add(BuildMenuEntry(entry, raise));
            }
            button.Flyout = menu;
        }
        else
        {
            button.Click += (_, _) => raise(model);
        }
        return button;
    }

    // A host model as a menu item (the narrow adornment drop-down, the overflow menu): a submenu for a
    // MenuItems button, a check-mark item for a toggle, else a plain item. Picking one runs `raise`.
    private MenuFlyoutItemBase BuildMenuItem(SchedulerToolbarButton model, Action<SchedulerToolbarButton> raise)
    {
        var text = string.IsNullOrEmpty(model.Label) ? (model.Tooltip ?? string.Empty) : model.Label;
        if (model.MenuItems is { Count: > 0 } items)
        {
            var sub = new MenuFlyoutSubItem { Text = text, IsEnabled = model.IsEnabled, Icon = BuildMenuIcon(model) };
            foreach (var entry in items)
            {
                sub.Items.Add(BuildMenuEntry(entry, raise));
            }
            return sub;
        }

        var item = model.IsToggle
            ? new ToggleMenuFlyoutItem { IsChecked = model.IsChecked }
            : new MenuFlyoutItem();
        item.Text = text;
        item.IsEnabled = model.IsEnabled;
        item.Tag = model;
        item.Icon = BuildMenuIcon(model);
        item.Click += (_, _) => raise(model);
        return item;
    }

    // One menu entry for a button's MenuItems (inline dropdown or overflow submenu): clicking it runs `raise`
    // with the entry itself (its command, then the click event).
    private MenuFlyoutItem BuildMenuEntry(SchedulerToolbarButton entry, Action<SchedulerToolbarButton> raise)
    {
        var item = new MenuFlyoutItem
        {
            Text = string.IsNullOrEmpty(entry.Label) ? (entry.Tooltip ?? string.Empty) : entry.Label,
            IsEnabled = entry.IsEnabled,
        };
        if (!string.IsNullOrEmpty(entry.Glyph))
        {
            item.Icon = new FontIcon
            {
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = entry.Glyph!,
            };
        }
        item.Click += (_, _) => raise(entry);
        return item;
    }

    // Icon/label content for a toolbar button: an image (e.g. an adornment template's icon) beats a glyph,
    // glyph+label render side by side, otherwise the plain label. Shared by custom + adornment buttons.
    private static object BuildButtonContent(SchedulerToolbarButton model)
    {
        var hasGlyph = !string.IsNullOrEmpty(model.Glyph);
        var hasLabel = !string.IsNullOrEmpty(model.Label);

        if (model.Image is { Length: > 0 } bytes)
        {
            return BuildImageIcon(bytes);
        }
        if (hasGlyph && hasLabel)
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"], Glyph = model.Glyph! },
                    new TextBlock { Text = model.Label, VerticalAlignment = VerticalAlignment.Center },
                },
            };
        }
        if (hasGlyph)
        {
            return new FontIcon { FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"], Glyph = model.Glyph! };
        }
        return model.Label ?? string.Empty;
    }

    // ---- Adornment toggle group ----------------------------------------------------------------

    private IReadOnlyList<SchedulerToolbarButton> _adornments = System.Array.Empty<SchedulerToolbarButton>();

    /// <summary>Raised when an adornment group entry (wide button or narrow menu item) is clicked.</summary>
    public event EventHandler<SchedulerToolbarButton>? AdornmentClicked;

    /// <summary>Render the adornment toggle group: a button row (wide) + a dropdown menu (narrow), one
    /// entry per template. Each entry honors the same <see cref="SchedulerToolbarButton"/> members as the
    /// custom buttons: a checked toggle shows pressed (a check mark in the menu), <c>MenuItems</c> make a
    /// drop-down (a submenu in the menu), and a click runs <c>Command</c> and then raises
    /// <see cref="AdornmentClicked"/> with the clicked entry. Empty hides the group.</summary>
    public void SetAdornmentButtons(IReadOnlyList<SchedulerToolbarButton> buttons)
    {
        _adornments = buttons;
        AdornmentGroupPanel.Children.Clear();
        AdornmentMenu.Items.Clear();

        foreach (var model in buttons)
        {
            AdornmentGroupPanel.Children.Add(BuildToolbarButton(model, RaiseAdornment));
            AdornmentMenu.Items.Add(BuildMenuItem(model, RaiseAdornment));
        }

        ApplyResponsiveLayout(ActualWidth);
    }

    // A menu item's icon: the model's image as an ImageIcon, else its glyph, else none.
    private static IconElement? BuildMenuIcon(SchedulerToolbarButton model)
    {
        if (model.Image is { Length: > 0 } bytes)
        {
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            _ = LoadIconAsync(bmp, bytes);
            return new ImageIcon { Source = bmp };
        }
        if (!string.IsNullOrEmpty(model.Glyph))
        {
            return new FontIcon
            {
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = model.Glyph!,
            };
        }
        return null;
    }

    // A small (16px) image icon from raw bytes for an image-backed custom button (e.g. adornment template).
    private static Microsoft.UI.Xaml.Controls.Image BuildImageIcon(byte[] bytes)
    {
        var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        _ = LoadIconAsync(bmp, bytes);
        return new Microsoft.UI.Xaml.Controls.Image
        {
            Source = bmp,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Width = 16,
            Height = 16,
        };
    }

    private static async Task LoadIconAsync(Microsoft.UI.Xaml.Media.Imaging.BitmapImage bmp, byte[] bytes)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(bytes);
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
        }
        catch
        {
            // A malformed image just renders nothing — never break the toolbar.
        }
    }

    // ---- Navigation ----------------------------------------------------------------------------

    private void OnPrev(object sender, RoutedEventArgs e) => PreviousClicked?.Invoke(this, EventArgs.Empty);

    private void OnNext(object sender, RoutedEventArgs e) => NextClicked?.Invoke(this, EventArgs.Empty);

    private void OnToday(object sender, RoutedEventArgs e) => TodayClicked?.Invoke(this, EventArgs.Empty);

    private void OnViewClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb
            && tb.Tag is string tag
            && Enum.TryParse<SchedulerView>(tag, out var view))
        {
            SetActiveView(view);
            ViewSelected?.Invoke(this, view);
        }
    }

    private void OnViewComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }
        if (ViewCombo.SelectedItem is ComboBoxItem { Tag: string tag }
            && Enum.TryParse<SchedulerView>(tag, out var view))
        {
            SetActiveView(view);
            ViewSelected?.Invoke(this, view);
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout(e.NewSize.Width);

    // Two-stage responsiveness: (1) swap the segmented view switcher for a compact dropdown below
    // NarrowThreshold, then (2) measure what's left and spill the lowest-priority commands into the
    // overflow ("hamburger") menu until the bar fits — so buttons never bleed off the right edge.
    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var narrow = width < NarrowThreshold;
        ViewSegmented.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        ViewCombo.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;

        // The adornment group mirrors the view switcher: a button row when wide, an "Adornments" dropdown
        // when narrow. Both hide when there are no adornments.
        var hasAdornments = _adornments.Count > 0;
        AdornmentGroupPanel.Visibility = hasAdornments && !narrow ? Visibility.Visible : Visibility.Collapsed;
        AdornmentDropdown.Visibility = hasAdornments && narrow ? Visibility.Visible : Visibility.Collapsed;

        UpdateOverflow(width);
    }

    // Pull everything back inline, then — if the clusters still don't fit — reveal the hamburger and
    // move collapsible items into its menu one at a time (lowest priority first) until the bar fits.
    private void UpdateOverflow(double width)
    {
        // 1) Reset: clear the menu and show every collapsible item, so the fit test below measures the
        //    full bar. (The view switcher's own segmented/combo choice was set by the caller.)
        OverflowMenu.Items.Clear();
        TodayButton.Visibility = Visibility.Visible;
        foreach (var child in CustomButtonPanel.Children.OfType<UIElement>())
        {
            child.Visibility = Visibility.Visible;
        }
        OverflowButton.Visibility = Visibility.Collapsed;

        if (Fits(width))
        {
            return;
        }

        // 2) Overflow: the hamburger now counts toward the width, so reveal it before re-measuring.
        OverflowButton.Visibility = Visibility.Visible;

        foreach (var item in CollapseCandidates())
        {
            if (Fits(width))
            {
                break;
            }
            CollapseToMenu(item);
        }
    }

    // Do the nav + right clusters (at their current visibility) fit within the toolbar, leaving room for
    // the Grid padding and a small gap so the two clusters never touch?
    private bool Fits(double toolbarWidth)
    {
        const double padding = 16; // Grid Padding (8 left + 8 right)
        const double minGap = 12;  // keep the nav and right clusters from butting together
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        NavPanel.Measure(inf);
        RightPanel.Measure(inf);
        return NavPanel.DesiredSize.Width + RightPanel.DesiredSize.Width + minGap + padding <= toolbarWidth;
    }

    // The order items spill into the overflow menu: host custom buttons first (the usual culprits),
    // then Today, then — only as a last resort on the very narrowest bars — the view switcher. The nav
    // cluster (prev / date / next) and the Settings gear always stay on the bar.
    private IEnumerable<FrameworkElement> CollapseCandidates()
    {
        foreach (var b in CustomButtonPanel.Children.OfType<Button>())
        {
            yield return b;
        }
        yield return TodayButton;
        // Whichever view switcher is currently shown (segmented when wide, combo when narrow).
        yield return ViewCombo.Visibility == Visibility.Visible ? ViewCombo : (FrameworkElement)ViewSegmented;
    }

    // Hide the inline item and add an equivalent entry to the overflow menu that runs the same action.
    private void CollapseToMenu(FrameworkElement item)
    {
        item.Visibility = Visibility.Collapsed;

        if (ReferenceEquals(item, TodayButton))
        {
            var today = new MenuFlyoutItem { Text = "Today" };
            today.Click += (_, _) => TodayClicked?.Invoke(this, EventArgs.Empty);
            OverflowMenu.Items.Insert(0, today); // Today reads first, above the custom commands
        }
        else if (item is Button { Tag: SchedulerToolbarButton model })
        {
            // A menu button folds into a submenu of the same entries; a toggle keeps its pressed state as a
            // check mark.
            OverflowMenu.Items.Add(BuildMenuItem(model, RaiseCustom));
        }
        else if (ReferenceEquals(item, ViewCombo) || ReferenceEquals(item, ViewSegmented))
        {
            // The view switcher folds into a "View" submenu mirroring the segments (single source: _toggles).
            var sub = new MenuFlyoutSubItem { Text = "View" };
            foreach (var t in _toggles)
            {
                var tag = t.Tag as string ?? string.Empty;
                var mi = new RadioMenuFlyoutItem
                {
                    Text = t.Content?.ToString() ?? tag,
                    GroupName = "OverflowView",
                    IsChecked = t.IsChecked == true,
                };
                mi.Click += (_, _) =>
                {
                    if (Enum.TryParse<SchedulerView>(tag, out var view))
                    {
                        SetActiveView(view);
                        ViewSelected?.Invoke(this, view);
                    }
                };
                sub.Items.Add(mi);
            }
            OverflowMenu.Items.Add(sub);
        }
    }

    // ---- Date-range picker ---------------------------------------------------------------------

    // LocalSettings keys for the persisted picker panel size (library-owned namespace, distinct from the
    // host's "SchedulerNative." keys).
    //
    // These deliberately keep the original "Uno.Scheduler." prefix even though XU-443 renamed the
    // library to Xtreme.Components. A LocalSettings key is a storage contract, not a namespace:
    // renaming it would silently discard the saved panel size for every user who had already resized
    // the picker, buying nothing. Leave them alone.
    private const string PickerWidthKey = "Uno.Scheduler.PickerWidth";
    private const string PickerHeightKey = "Uno.Scheduler.PickerHeight";

    // The first day of week to lay the month grids out from (mirrors the scheduler setting).
    private DayOfWeek _firstDay = DayOfWeek.Sunday;

    private void OnPeriodFlyoutOpening(object? sender, object e)
    {
        RestorePickerSize();
        RangeCalendar.SetFirstDayOfWeek(_firstDay);
        RangeCalendar.SetSelection(_currentDates);
        UpdateRangeSummary();
    }

    private void OnNavigatorSelectionChanged(object? sender, EventArgs e) => UpdateRangeSummary();

    // Reflect the current picked set in the summary line and gate the Apply button on a non-empty pick.
    private void UpdateRangeSummary()
    {
        var dates = RangeCalendar.GetSelection();
        ApplyDatesButton.IsEnabled = dates.Count > 0;

        if (dates.Count == 0)
        {
            RangeSummary.Text = "Tap days to select — pick several (e.g. the next 4 Saturdays), then Apply.";
            return;
        }

        var ci = System.Globalization.CultureInfo.CurrentCulture;
        if (dates.Count == 1)
        {
            RangeSummary.Text = dates[0].ToString("ddd, MMM d, yyyy", ci);
            return;
        }
        RangeSummary.Text = $"{dates.Count} days · {dates[0].ToString("MMM d", ci)} – {dates[^1].ToString("MMM d", ci)}";
    }

    private void OnApplyDates(object sender, RoutedEventArgs e)
    {
        var dates = RangeCalendar.GetSelection();
        if (dates.Count == 0)
        {
            return; // Apply is disabled while empty, but guard anyway
        }
        DatesSelected?.Invoke(this, dates);
        PeriodButton.Flyout?.Hide();
    }

    private void OnClearDates(object sender, RoutedEventArgs e) => RangeCalendar.ClearSelection();

    // ---- Resizable picker panel (size persisted to local user cache) ---------------------------

    // Size snapshot taken at drag start; the cumulative translation is applied against it so a fast drag
    // tracks smoothly instead of summing per-frame deltas against a one-frame-stale ActualWidth.
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    private void OnResizeStarted(object? sender, EventArgs e)
    {
        _resizeStartWidth = PickerContainer.ActualWidth;
        _resizeStartHeight = PickerContainer.ActualHeight;
    }

    private void OnResizeDrag(object? sender, Point cumulative)
    {
        PickerContainer.Width = Math.Clamp(_resizeStartWidth + cumulative.X, PickerContainer.MinWidth, 1100);
        PickerContainer.Height = Math.Clamp(_resizeStartHeight + cumulative.Y, PickerContainer.MinHeight, 900);
    }

    private void OnResizeCompleted(object? sender, EventArgs e) => SavePickerSize();

    private void RestorePickerSize()
    {
        try
        {
            var v = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (v.TryGetValue(PickerWidthKey, out var w) && w is double wd && wd > 0)
            {
                PickerContainer.Width = Math.Clamp(wd, PickerContainer.MinWidth, 1100);
            }
            if (v.TryGetValue(PickerHeightKey, out var h) && h is double hd && hd > 0)
            {
                PickerContainer.Height = Math.Clamp(hd, PickerContainer.MinHeight, 900);
            }
        }
        catch
        {
            // No LocalSettings on this head (or access denied) — just use the XAML default size.
        }
    }

    private void SavePickerSize()
    {
        try
        {
            var v = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            v[PickerWidthKey] = PickerContainer.ActualWidth;
            v[PickerHeightKey] = PickerContainer.ActualHeight;
        }
        catch
        {
            // Persistence is best-effort; a head without LocalSettings just doesn't remember the size.
        }
    }

    // ---- Settings ------------------------------------------------------------------------------

    private void OnTickChanged(object sender, SelectionChangedEventArgs e) => RaiseTimeIncrement();

    private void OnZoomChanged(object sender, RangeBaseValueChangedEventArgs e) => RaiseTimeIncrement();

    private void OnColumnWidthChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_suppress)
        {
            ColumnMinWidthChanged?.Invoke(this, ColumnWidthSlider.Value);
        }
    }

    private void OnCornerRadiusChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_suppress)
        {
            CornerRadiusChanged?.Invoke(this, CornerRadiusSlider.Value);
        }
    }

    private void OnFirstDayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppress)
        {
            FirstDayOfWeekChanged?.Invoke(this, (DayOfWeek)TagMinutes(FirstDayCombo, 0));
        }
    }

    private void OnWorkHoursChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }
        var start = TimeSpan.FromMinutes(TagMinutes(WorkStartCombo, 480));
        var end = TimeSpan.FromMinutes(TagMinutes(WorkEndCombo, 1020));
        WorkHoursChanged?.Invoke(this, (start, end));
    }

    private void RaiseTimeIncrement()
    {
        if (_suppress)
        {
            return;
        }

        var major = TagMinutes(MajorTickCombo, 60);
        var minor = TagMinutes(MinorTickCombo, 30);
        TimeIncrementChanged?.Invoke(this, new TimeIncrementConfig
        {
            MajorTick = TimeSpan.FromMinutes(major),
            MinorTick = TimeSpan.FromMinutes(minor),
            PixelsPerMinute = ZoomSlider.Value,
        });
    }

    private void OnGroupRadioChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }
        GroupModeChanged?.Invoke(this, GroupResDateRadio.IsChecked == true
            ? SchedulerGroupMode.ByResource
            : SchedulerGroupMode.ByDay);
    }

    private void OnHideIdleToggled(object sender, RoutedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }
        HideIdleChanged?.Invoke(this, HideIdleSwitch.IsOn);
    }

    private void OnShowTilesToggled(object sender, RoutedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }
        ShowTilesChanged?.Invoke(this, ShowTilesSwitch.IsOn);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static int TagMinutes(ComboBox combo, int fallback) =>
        combo.SelectedItem is ComboBoxItem { Tag: string s } && int.TryParse(s, out var m) ? m : fallback;

    private static void SelectByTag(ComboBox combo, int minutes)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem { Tag: string s } item && int.TryParse(s, out var m) && m == minutes)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }
}
