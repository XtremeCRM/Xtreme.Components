using System.Globalization;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Xtreme.Components.Primitives;

/// <summary>
/// Reusable Date/Time field. Collapsed: a button showing the current value in a
/// width-responsive format. Expanded: a single flyout holding an inline
/// <see cref="CalendarView"/> and an inline time list — both always visible, so the
/// user can move between date and time freely.
/// </summary>
/// <remarks>
/// <para>Supersedes <c>DateTimeFieldControl</c>, which had two defects this control is
/// shaped to avoid (XU-283):</para>
/// <list type="number">
/// <item><b>Nested flyouts.</b> The old control put a <c>CalendarDatePicker</c> (which owns
/// its own calendar flyout) inside the field's flyout. The inner light-dismiss layer
/// consumed clicks aimed at the time box and could leave the picker stuck open, so
/// date→time→date wedged. Here the calendar is inline: one popup, one dismiss layer.</item>
/// <item><b>Dead time affordance.</b> The old time box was an <c>AutoSuggestBox</c> with
/// <c>QueryIcon="Clock"</c> and no <c>QuerySubmitted</c> handler — the clock raised an
/// event nobody listened to and never opened the suggestion list, so the quarter-hour
/// slots were reachable only by typing. Here the slot list is always on screen.</item>
/// </list>
/// <para>Timezone basis: the public surface is <see cref="DateOnly"/>/<see cref="TimeOnly"/>
/// and the calendar takes plain <see cref="DateTime"/>, so no <see cref="DateTimeOffset"/>
/// exists anywhere on the date path. The original XU-283 off-by-one — a UTC-midnight value
/// rendering as the prior day in every negative-UTC timezone — is therefore not merely fixed
/// here but unrepresentable.</para>
/// </remarks>
public sealed partial class DateTimePicker : UserControl
{
    /// <summary>True while pushing property values into the pickers, so their change
    /// events don't echo back and fight the caller.</summary>
    private bool _syncingToPickers;

    /// <summary>
    /// Pending edit, held while the flyout is open. Every gesture inside the flyout writes HERE,
    /// not to <see cref="Date"/>/<see cref="Time"/> — Save is what publishes, Cancel just drops it.
    /// Without this the picker committed live, so merely opening it and clicking around dirtied the
    /// host editor (ValueChanged → MarkDirtyOnVm) with no way to back out.
    /// </summary>
    private DateOnly? _editDate;
    private TimeOnly? _editTime;

    /// <summary>Set by Save, Cancel and Escape so <see cref="OnFlyoutClosing"/> can tell a deliberate
    /// close from a stray click-away.</summary>
    private bool _closingDeliberately;

    /// <summary>True while Save writes Date and Time, so the pair raises one ValueChanged, not two.</summary>
    private bool _publishing;

    /// <summary>Last single-day set pushed into the navigator, so a selection change can be
    /// diffed to find which day the user actually just clicked.</summary>
    private IReadOnlyList<DateTime> _lastCalendarSelection = Array.Empty<DateTime>();

    public DateTimePicker()
    {
        this.InitializeComponent();
        RebuildTimeSlots();
        SyncStepSelection();
        SyncPickersFromEdit();
        UpdateDisplay();

        // handledEventsToo: the time TextBox and the slot ListView both consume keys, and Escape
        // must reach us regardless of where focus sits inside the flyout.
        PART_FlyoutScroll.AddHandler(UIElement.KeyDownEvent,
            new KeyEventHandler(OnFlyoutKeyDown), handledEventsToo: true);
    }

    /// <summary>Below this much room the flyout stacks the time half under the calendar instead of
    /// beside it. Measured against the window, not the field — a flyout is a popup and can be wider
    /// than the control that opened it, but it still can't exceed the window.</summary>
    private const double StackBelowWidth = 620;

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(object), typeof(DateTimePicker),
        new PropertyMetadata(null, OnHeaderChanged));

    /// <summary>Label above the button: text or any content (same shape as <c>TextBox.Header</c>).
    /// Null or an empty string collapses the label row.</summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DateTimePicker c)
            c.PART_Header.Visibility = e.NewValue is null or "" ? Visibility.Collapsed : Visibility.Visible;
    }

    public static readonly DependencyProperty DateProperty = DependencyProperty.Register(
        nameof(Date), typeof(DateOnly?), typeof(DateTimePicker),
        new PropertyMetadata(null, OnValuePropChanged));

    public DateOnly? Date
    {
        get => (DateOnly?)GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(
        nameof(Time), typeof(TimeOnly?), typeof(DateTimePicker),
        new PropertyMetadata(null, OnValuePropChanged));

    public TimeOnly? Time
    {
        get => (TimeOnly?)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    /// <summary>Set false for a date-only field — hides the time half entirely.</summary>
    public static readonly DependencyProperty ShowTimeProperty = DependencyProperty.Register(
        nameof(ShowTime), typeof(bool), typeof(DateTimePicker),
        new PropertyMetadata(true, OnShowTimeChanged));

    public bool ShowTime
    {
        get => (bool)GetValue(ShowTimeProperty);
        set => SetValue(ShowTimeProperty, value);
    }

    /// <summary>Granularity of the slot list, in minutes. Default 15.</summary>
    public static readonly DependencyProperty MinuteStepProperty = DependencyProperty.Register(
        nameof(MinuteStep), typeof(int), typeof(DateTimePicker),
        new PropertyMetadata(15, OnMinuteStepChanged));

    public int MinuteStep
    {
        get => (int)GetValue(MinuteStepProperty);
        set => SetValue(MinuteStepProperty, value);
    }

    /// <summary>
    /// Raised whenever <see cref="Date"/> or <see cref="Time"/> changes: once per Save that changed
    /// either or both (one notification for the pair), and once per property written from code or a
    /// binding with a different value. Hosts that load values into the field and only want user edits
    /// should suppress their handler around the load.
    /// </summary>
    public event EventHandler? ValueChanged;

    private static void OnValuePropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DateTimePicker c) return;
        if (c._publishing) return; // Save raises a single ValueChanged for the pair itself

        // An external write (host loading an event) becomes the new baseline for the pending edit.
        c._editDate = c.Date;
        c._editTime = c.Time;
        c.UpdateDisplay();
        c.SyncPickersFromEdit();
        c.UpdateSaveEnabled();
        c.ValueChanged?.Invoke(c, EventArgs.Empty);
    }

    private static void OnShowTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DateTimePicker c)
        {
            var vis = c.ShowTime ? Visibility.Visible : Visibility.Collapsed;
            c.PART_TimePanel.Visibility = vis;
            c.PART_Divider.Visibility = vis;
            c.UpdateDisplay();
        }
    }

    private static void OnMinuteStepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DateTimePicker c)
        {
            c.RebuildTimeSlots();
            c.SyncStepSelection();
            c.SyncPickersFromEdit();
        }
    }

    // ---- Property -> UI ----

    private void SyncPickersFromEdit()
    {
        _syncingToPickers = true;
        try
        {
            if (_editDate is DateOnly d)
            {
                // Plain DateTime — the navigator has no offset concept, so there is nothing
                // here that can shift a day. SetSelection also pages the grid to that month.
                var dt = d.ToDateTime(TimeOnly.MinValue);
                PART_Calendar.SetSelection(new[] { dt });
                _lastCalendarSelection = new[] { dt };
            }
            else
            {
                PART_Calendar.ClearSelection();
                _lastCalendarSelection = Array.Empty<DateTime>();
            }

            PART_TimeText.Text = FormatTime(_editTime);
            PART_TimeList.SelectedItem = _editTime is TimeOnly t ? FormatTime(t) : null;
        }
        finally
        {
            _syncingToPickers = false;
        }
    }

    /// <summary>Dirty = the pending edit differs from what's published. Gates Save, per the
    /// save-disabled-until-dirty standard.</summary>
    private void UpdateSaveEnabled() =>
        PART_SaveButton.IsEnabled = _editDate != Date || _editTime != Time;

    // ---- UI -> Property ----

    private void OnCalendarSelectionChanged(object? sender, EventArgs e)
    {
        if (_syncingToPickers) return;

        var current = PART_Calendar.GetSelection();

        // MultiMonthDateNavigator is multi-select by design (Ctrl/Shift are part of its
        // scheduler role). A date FIELD holds exactly one value, so collapse to the day the
        // user just added — falling back to whatever remains when the gesture removed one.
        var added = current.Except(_lastCalendarSelection).ToList();
        var picked = added.Count > 0 ? added[0]
                   : current.Count > 0 ? current[0]
                   : (DateTime?)null;

        if (picked is not DateTime dt)
        {
            _lastCalendarSelection = Array.Empty<DateTime>();
            _editDate = null;
            UpdateSaveEnabled();
            return;
        }

        // Push the collapsed single-day set back so the grid doesn't keep showing the
        // multi-day highlight the gesture produced.
        if (current.Count != 1)
        {
            _syncingToPickers = true;
            try { PART_Calendar.SetSelection(new[] { dt }); }
            finally { _syncingToPickers = false; }
        }

        _lastCalendarSelection = new[] { dt };
        _editDate = DateOnly.FromDateTime(dt);
        UpdateSaveEnabled();
    }

    /// <summary>The list is virtualized and has no realized items until the flyout opens,
    /// so scroll-to-selection has to happen here rather than when the value is set —
    /// otherwise a 6pm value opens the list sitting at midnight.</summary>
    private void OnFlyoutOpened(object? sender, object e)
    {
        // Re-baseline the pending edit on every open, so a Cancel (or a click-away) from a previous
        // session can't leak stale values into this one.
        _closingDeliberately = false;
        _editDate = Date;
        _editTime = Time;
        SyncPickersFromEdit();
        UpdateSaveEnabled();

        ApplyResponsiveLayout();
        ScrollTimeIntoView();
    }

    /// <summary>
    /// Blocks click-away dismissal while there is an unsaved edit, so a stray click can't silently
    /// discard a date the user just picked. A CLEAN flyout still dismisses normally — there is
    /// nothing to lose, and trapping the user in an unchanged popup would be hostile. Save, Cancel
    /// and Escape all get through via <see cref="_closingDeliberately"/>.
    /// </summary>
    private void OnFlyoutClosing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
    {
        if (_closingDeliberately) return;
        if (_editDate != Date || _editTime != Time) args.Cancel = true;
    }

    private void ScrollTimeIntoView()
    {
        if (PART_TimeList.SelectedItem is not null)
            PART_TimeList.ScrollIntoView(PART_TimeList.SelectedItem);
    }

    /// <summary>
    /// Side-by-side when there's room, stacked when there isn't. Re-evaluated on every open rather
    /// than once, so the flyout follows a window that was resized between openings.
    /// </summary>
    private void ApplyResponsiveLayout()
    {
        // No time half means nothing to stack — a lone calendar always fits.
        var available = XamlRoot?.Size.Width ?? double.MaxValue;
        var availableHeight = XamlRoot?.Size.Height ?? double.MaxValue;
        var stacked = ShowTime && available < StackBelowWidth;

        // Stacked adds the calendar's height to the popup, which can exceed a short window.
        // Cap it and let the content scroll rather than run off the bottom. Side-by-side always
        // fits, so scrolling stays off there and the popup keeps sizing to its content.
        PART_FlyoutScroll.MaxHeight = stacked ? Math.Max(320, availableHeight - 120) : double.PositiveInfinity;
        PART_FlyoutScroll.VerticalScrollMode = stacked ? ScrollMode.Enabled : ScrollMode.Disabled;
        PART_FlyoutScroll.VerticalScrollBarVisibility =
            stacked ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

        // Tighter gutters when stacked — vertical room is the scarce axis there.
        PART_FlyoutGrid.RowSpacing = stacked ? 8 : 12;

        if (stacked)
        {
            Grid.SetRow(PART_Calendar, 0);
            Grid.SetColumn(PART_Calendar, 0);
            Grid.SetColumnSpan(PART_Calendar, 3);

            // The divider turns from a column rule into a row rule.
            Grid.SetRow(PART_Divider, 1);
            Grid.SetColumn(PART_Divider, 0);
            Grid.SetColumnSpan(PART_Divider, 3);
            PART_Divider.Width = double.NaN;
            PART_Divider.Height = 1;
            PART_Divider.HorizontalAlignment = HorizontalAlignment.Stretch;
            PART_Divider.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetRow(PART_TimePanel, 2);
            Grid.SetColumn(PART_TimePanel, 0);
            Grid.SetColumnSpan(PART_TimePanel, 3);
            PART_TimePanel.Width = StepBarWidth();
            // The time column stays step-bar width (narrower than the 250 calendar above it), so
            // centre it — left-aligned it reads as a ragged edge rather than a deliberate stack.
            PART_TimePanel.HorizontalAlignment = HorizontalAlignment.Center;

            // Shorter list: stacked adds the calendar's height to the popup, so a full-height list
            // would run off the bottom of exactly the small window that triggered stacking.
            PART_TimeList.Height = 150;
        }
        else
        {
            Grid.SetRow(PART_Calendar, 0);
            Grid.SetColumn(PART_Calendar, 0);
            Grid.SetColumnSpan(PART_Calendar, 1);

            Grid.SetRow(PART_Divider, 0);
            Grid.SetColumn(PART_Divider, 1);
            Grid.SetColumnSpan(PART_Divider, 1);
            PART_Divider.Width = 1;
            PART_Divider.Height = double.NaN;
            PART_Divider.HorizontalAlignment = HorizontalAlignment.Center;
            PART_Divider.VerticalAlignment = VerticalAlignment.Stretch;

            Grid.SetRow(PART_TimePanel, 0);
            Grid.SetColumn(PART_TimePanel, 2);
            Grid.SetColumnSpan(PART_TimePanel, 1);
            PART_TimePanel.Width = StepBarWidth();
            PART_TimePanel.HorizontalAlignment = HorizontalAlignment.Left;

            PART_TimeList.Height = 220;
        }
    }

    // ---- Slot granularity ----

    private void OnStepToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb) return;
        if (tb.Tag is not string tag || !int.TryParse(tag, out var minutes)) return;

        // Single-select. Clicking the ALREADY-active step must not clear it — a step bar with
        // nothing chosen has no answer to give — so the clicked button is re-checked either way.
        MinuteStep = minutes; // DP callback rebuilds the slot list
        SyncStepSelection();

        // The rebuild replaces every container, so the list keeps its old scroll OFFSET and lands
        // somewhere unrelated (2:19 AM for an 11:30 value). Re-anchor on the selection.
        ScrollTimeIntoView();
    }

    /// <summary>
    /// The step bar sizes to its buttons; the time column then sizes to the bar, so the text box,
    /// the bar and the slot list all share one right edge. Measured rather than hardcoded so it
    /// survives a font-size or padding change without drifting back out of alignment.
    /// </summary>
    private void OnStepBarSizeChanged(object sender, SizeChangedEventArgs e) => MatchTimeColumnToStepBar();

    private void MatchTimeColumnToStepBar()
    {
        var w = StepBarWidth();
        // Guard the assignment: the panel and the bar are in the same layout pass, and writing an
        // unchanged Width would keep re-entering SizeChanged.
        if (Math.Abs(PART_TimePanel.Width - w) > 0.5)
        {
            PART_TimePanel.Width = w;
        }
    }

    /// <summary>Measured bar width, or the design width until the bar has had a layout pass.</summary>
    private double StepBarWidth() => PART_StepBar.ActualWidth > 0 ? PART_StepBar.ActualWidth : 204;

    private void SyncStepSelection()
    {
        foreach (var tb in PART_StepBar.Children.OfType<ToggleButton>())
        {
            tb.IsChecked = tb.Tag is string t && int.TryParse(t, out var m) && m == MinuteStep;
        }
    }

    private void OnTimeListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingToPickers) return;
        if (PART_TimeList.SelectedItem is not string slot) return;
        if (!TypeableTimeBox.TryParseTime(slot, out var parsed)) return;

        _editTime = parsed;
        PART_TimeText.Text = FormatTime(parsed);
        UpdateSaveEnabled();
    }

    private void OnTimeTextKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        CommitTypedTime();
        e.Handled = true;
    }

    private void OnTimeTextLostFocus(object sender, RoutedEventArgs e) => CommitTypedTime();

    /// <summary>Parse whatever is in the text box. Empty clears; unparseable reverts to
    /// the last good value rather than silently discarding it.</summary>
    private void CommitTypedTime()
    {
        if (_syncingToPickers) return;

        var typed = PART_TimeText.Text?.Trim() ?? string.Empty;

        if (typed.Length == 0)
        {
            _editTime = null;
            PART_TimeList.SelectedItem = null;
            UpdateSaveEnabled();
            return;
        }

        if (TypeableTimeBox.TryParseTime(typed, out var parsed))
        {
            _editTime = parsed;
            // Re-canonicalize ("4p" -> "4:00 PM") and mirror into the slot list.
            _syncingToPickers = true;
            try
            {
                PART_TimeText.Text = FormatTime(parsed);
                PART_TimeList.SelectedItem = SlotFor(parsed);
            }
            finally { _syncingToPickers = false; }
            UpdateSaveEnabled();
        }
        else
        {
            PART_TimeText.Text = FormatTime(_editTime);
        }
    }

    /// <summary>Empties the pending edit but does NOT publish or close — the user still has to Save
    /// (or Cancel out of) the clear, same as any other change made in here.</summary>
    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _editDate = null;
        _editTime = null;
        SyncPickersFromEdit();
        UpdateSaveEnabled();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // Pick up a time typed but never blurred.
        CommitTypedTime();

        if (_editDate != Date || _editTime != Time)
        {
            _publishing = true;
            try
            {
                Date = _editDate;
                Time = _editTime;
            }
            finally { _publishing = false; }
            UpdateDisplay();
            UpdateSaveEnabled();
            ValueChanged?.Invoke(this, EventArgs.Empty); // one notification for the pair
        }

        _closingDeliberately = true;
        PART_Flyout.Hide();
    }

    /// <summary>Drops the pending edit. Nothing was published while the flyout was open, so this is
    /// just a close — the next open re-baselines from the live values.</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e) => CancelAndClose();

    /// <summary>Escape is a Cancel, not a no-op. Without this the dirty guard in
    /// <see cref="OnFlyoutClosing"/> would swallow Escape and leave the user with no keyboard exit.
    /// Registered with handledEventsToo so a child that already handled the key can't hide it.</summary>
    private void OnFlyoutKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        CancelAndClose();
    }

    private void CancelAndClose()
    {
        _closingDeliberately = true; // an explicit abandon, so the dirty guard must not block it
        PART_Flyout.Hide();
    }

    // ---- Display ----

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDisplay();

    private void UpdateDisplay()
    {
        if (Date is null && Time is null)
        {
            PART_DisplayText.Text = "—";
            return;
        }

        var dateStr = Date is DateOnly d ? FormatDateForWidth(d, ActualWidth) : "(no date)";
        var timeStr = ShowTime && Time is TimeOnly t ? FormatTime(t) : string.Empty;

        PART_DisplayText.Text = string.IsNullOrEmpty(timeStr) ? dateStr : $"{dateStr}  {timeStr}";
    }

    /// <summary>Tiered formats: long when there's room, compact when narrow, so the
    /// combined date+time string doesn't clip at typical pane widths.</summary>
    private static string FormatDateForWidth(DateOnly d, double widthPx)
    {
        if (widthPx >= 320) return d.ToString("ddd MMM d, yyyy", CultureInfo.CurrentCulture);
        if (widthPx >= 220) return d.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
        return d.ToString("M/d/yyyy", CultureInfo.CurrentCulture);
    }

    private static string FormatTime(TimeOnly? t) =>
        t.HasValue ? t.Value.ToString("h:mm tt", CultureInfo.CurrentCulture) : string.Empty;

    // ---- Time slots ----

    private void RebuildTimeSlots() => PART_TimeList.ItemsSource = BuildTimeSlots(MinuteStep);

    /// <summary>Exact slot string for a time, or null when it falls between slots (e.g.
    /// a typed 4:03 against a 15-minute step) — the list then shows no selection rather
    /// than snapping the user's value to the nearest slot.</summary>
    private string? SlotFor(TimeOnly t)
    {
        var step = Math.Max(1, MinuteStep);
        return t.Minute % step == 0 ? FormatTime(t) : null;
    }

    private static List<string> BuildTimeSlots(int minuteStep)
    {
        var step = Math.Max(1, minuteStep);
        var list = new List<string>(24 * 60 / step);
        for (var h = 0; h < 24; h++)
        {
            for (var m = 0; m < 60; m += step)
            {
                list.Add(new TimeOnly(h, m).ToString("h:mm tt", CultureInfo.CurrentCulture));
            }
        }
        return list;
    }
}
