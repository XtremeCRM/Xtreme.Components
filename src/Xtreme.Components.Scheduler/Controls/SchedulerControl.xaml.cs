using System.Collections.ObjectModel;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;
using Xtreme.Components.Scheduler.Contracts;
using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Rendering;

namespace Xtreme.Components.Scheduler.Controls;

/// <summary>
/// The root scheduler control. A primitive-only, cross-platform calendar/scheduler that
/// renders Day / Week / Month / Agenda / Planner views over a set of
/// <see cref="AppointmentModel"/> items grouped by <see cref="ResourceModel"/>.
/// </summary>
/// <remarks>
/// The control persists nothing itself — later phases raise events for the host to act on.
/// Views are composed in code-behind from the dependency-property inputs.
/// </remarks>
public sealed partial class SchedulerControl : UserControl
{
    public SchedulerControl()
    {
        InitializeComponent();

        // Opaque surface from the get-go so a host page's background/watermark never shows through
        // the transparent grid cells before the first render. Re-applied per-theme in RenderActiveView.
        Background = SchedulerPalette.For(ActualTheme).SurfaceBackground;

        // Seed the date-dependent default at construction (DP metadata defaults must be
        // compile-time-stable, so "today" is assigned here rather than in Register()).
        SelectedDate = new DateTimeOffset(DateTime.Today);

        Toolbar.PreviousClicked += (_, _) => Navigate(-1);
        Toolbar.NextClicked += (_, _) => Navigate(+1);
        Toolbar.TodayClicked += (_, _) =>
        {
            SelectedDates = null; // "Today" is a single-day jump — drop any multi-date set
            SelectedDate = new DateTimeOffset(DateTime.Today);
        };
        Toolbar.ViewSelected += (_, view) =>
        {
            // Week/Month/Planner are inherently contiguous — leaving Day/Agenda drops the multi-date set
            // (Legacy sets visible intervals per active view). Day↔Agenda keep it.
            if (view is SchedulerView.Week or SchedulerView.Month or SchedulerView.Planner)
            {
                SelectedDates = null;
            }
            CurrentView = view;
        };
        Toolbar.GroupModeChanged += (_, mode) => GroupMode = mode;
        Toolbar.HideIdleChanged += (_, hide) => HideIdleResources = hide;
        Toolbar.TimeIncrementChanged += (_, cfg) => TimeIncrement = cfg;
        Toolbar.ColumnMinWidthChanged += (_, w) => ColumnMinWidth = w;
        Toolbar.CornerRadiusChanged += (_, r) => AppointmentCornerRadius = r;
        Toolbar.ShowTilesChanged += (_, v) => ShowTiles = v;
        Toolbar.FirstDayOfWeekChanged += (_, d) => FirstDayOfWeek = d;
        Toolbar.WorkHoursChanged += (_, w) =>
        {
            WorkDayStart = w.Start;
            WorkDayEnd = w.End;
        };
        Toolbar.DatesSelected += (_, dates) => ApplyPickedDates(dates);

        // Host-supplied custom toolbar buttons: push the collection into the toolbar whenever it
        // changes (and once now, harmlessly empty), and bubble the toolbar's click up to the host.
        _toolbarButtons.CollectionChanged += (_, _) => Toolbar.SetCustomButtons(_toolbarButtons);
        Toolbar.CustomButtonClicked += (_, b) => ToolbarButtonClicked?.Invoke(this, b);

        // Adornment toggle group: its own reactive cluster (button row when wide, an "Adornments" dropdown
        // when narrow). Push the collection on change; bubble a click up as a toggle request for the host.
        _toolbarAdornments.CollectionChanged += (_, _) => Toolbar.SetAdornmentButtons(_toolbarAdornments);
        Toolbar.AdornmentClicked += (_, b) => AdornmentToggleRequested?.Invoke(this, b);

        // Host-supplied per-tile on/off options in the Settings flyout; bubble toggles up to the host.
        Toolbar.TileOptionToggled += (_, o) => TileOptionToggled?.Invoke(this, o);

        Loaded += (_, _) => { _lastRenderedTheme = ActualTheme; RenderActiveView(); };

        // Re-render on a REAL theme change only. Uno raises ActualThemeChanged on background elements when a
        // ContentDialog/popup opens (same theme, no actual change) — an unconditional RenderActiveView there
        // rebuilds the entire grid and saturates the UI thread for seconds, which is what made a permission
        // dialog opened over a busy scheduler render its button faint for ~10s. Guard on the theme value.
        ActualThemeChanged += (_, _) =>
        {
            if (ActualTheme == _lastRenderedTheme) return;
            _lastRenderedTheme = ActualTheme;
            RenderActiveView();
        };

        // The Delete key removes the selection — but KeyDown only fires when the control tree holds
        // keyboard focus. The event blocks are IsTabStop=false, so a pointer-select never lands focus
        // here on its own; GrabKeyboardFocus() (called from the pointer-selection paths) does it, and
        // IsTabStop lets this UserControl actually accept that focus.
        IsTabStop = true;
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Host-supplied custom buttons rendered on the toolbar (between the view switcher and Settings).
    /// Add a <see cref="SchedulerToolbarButton"/> to graft an app feature onto the bar; handle
    /// <see cref="ToolbarButtonClicked"/> (or the button's own <c>Command</c>) to act on it. The
    /// control itself stays agnostic about what the buttons do.
    /// </summary>
    public ObservableCollection<SchedulerToolbarButton> ToolbarButtons => _toolbarButtons;

    private readonly ObservableCollection<SchedulerToolbarButton> _toolbarButtons = new();

    /// <summary>Raised when a custom <see cref="ToolbarButtons"/> entry is clicked (after its own
    /// <c>Command</c> runs, if any).</summary>
    public event EventHandler<SchedulerToolbarButton>? ToolbarButtonClicked;

    /// <summary>
    /// Adornment toggle buttons, rendered as their own reactive group on the toolbar — a button row when
    /// wide, an "Adornments" dropdown when narrow — one per template. Handle
    /// <see cref="AdornmentToggleRequested"/> to toggle the clicked adornment on the current selection.
    /// </summary>
    public ObservableCollection<SchedulerToolbarButton> ToolbarAdornments => _toolbarAdornments;

    private readonly ObservableCollection<SchedulerToolbarButton> _toolbarAdornments = new();

    /// <summary>Raised when an adornment group button is clicked (the host runs the toggle + confirmation).</summary>
    public event EventHandler<SchedulerToolbarButton>? AdornmentToggleRequested;

    /// <summary>Set the per-tile on/off options shown in the Settings flyout under "Show tiles". The host
    /// supplies the available tiles (with their current enabled state); the library renders the toggles and
    /// raises <see cref="TileOptionToggled"/> when one changes. Pass an empty list to clear them.</summary>
    public void SetTileOptions(IReadOnlyList<SchedulerTileOption> options) => Toolbar?.SetTileOptions(options);

    /// <summary>Raised when the user toggles a per-tile option in the Settings flyout (carries the new state).</summary>
    public event EventHandler<SchedulerTileOption>? TileOptionToggled;

    /// <summary>Raised when the user clicks/drag-selects an empty slot. The host creates the appointment.</summary>
    public event EventHandler<AppointmentCreateArgs>? AppointmentCreateRequested;

    /// <summary>Raised on a single click of an empty Day/Week slot. Selecting a slot does NOT create —
    /// the host uses this to track the clicked lane as the "selected resource" for new appointments.
    /// A second click within the double-click window raises <see cref="AppointmentCreateRequested"/>.</summary>
    public event EventHandler<EmptySlotSelectedArgs>? EmptySlotSelected;

    /// <summary>Raised when the user drags an appointment to a new time/resource. The host persists it.</summary>
    public event EventHandler<AppointmentMoveArgs>? AppointmentMoveRequested;

    /// <summary>Raised when the user drags an appointment's edge to resize it. The host persists it.</summary>
    public event EventHandler<AppointmentResizeArgs>? AppointmentResizeRequested;

    /// <summary>Raised when an external <see cref="Contracts.ISchedulerDroppable"/> is dropped on a slot.</summary>
    public event EventHandler<ExternalDropArgs>? ExternalAppointmentDropped;

    /// <summary>
    /// Raised when the user clicks/taps an appointment. The control selects it (visual) regardless;
    /// this is the hook for opening an editor. Set <see cref="AppointmentEditRequestedArgs.Handled"/>
    /// to true to run your own editor (the editor lives outside this control); if no handler does,
    /// the control shows its own default editor. Clicking another appointment raises this again.
    /// </summary>
    public event EventHandler<AppointmentEditRequestedArgs>? AppointmentEditRequested;

    /// <summary>Raised whenever the selection set changes (single or multi). The host shows its editor
    /// for a single selection and hides it for a multi-selection.</summary>
    public event EventHandler<IReadOnlyList<AppointmentModel>>? SelectionChanged;

    /// <summary>
    /// Raised when the user right-clicks an appointment. The control selects it (visual) and reports it
    /// here; the host builds and shows its own context menu (the library is domain-agnostic). Anchor the
    /// menu at <see cref="AppointmentContextRequestedArgs.Position"/>.
    /// </summary>
    public event EventHandler<AppointmentContextRequestedArgs>? AppointmentContextRequested;

    /// <summary>Raised when the user drags a multi-selection — every item shares one (time, lane) delta.</summary>
    public event EventHandler<AppointmentsMoveArgs>? AppointmentsMoveRequested;

    /// <summary>Raised when the user resizes a multi-selection — the same edge-delta applies to all.</summary>
    public event EventHandler<AppointmentsResizeArgs>? AppointmentsResizeRequested;

    /// <summary>Raised when the user deletes the current selection (Delete key or <see cref="DeleteSelected"/>).</summary>
    public event EventHandler<AppointmentsDeleteArgs>? AppointmentsDeleteRequested;

    // The canonical selection set the two selection DPs project. Mutated only here.
    private readonly List<AppointmentModel> _selection = new();

    // Last theme a full RenderActiveView ran under — so the ActualThemeChanged handler can skip the spurious
    // same-theme events Uno raises when a ContentDialog/popup opens (see the constructor).
    private ElementTheme _lastRenderedTheme = ElementTheme.Default;
    private bool _selectionSyncing;

    // Last body scroll offset, preserved across re-renders so a live appointment upsert (or any
    // Appointments change) doesn't snap the grid back to business hours.
    private double? _scrollV;
    private double? _scrollH;

    private void OnViewScrollOffsetChanged(object? sender, (double V, double H) o)
    {
        _scrollV = o.V;
        _scrollH = o.H;
    }

    private void ForwardCreate(object? sender, AppointmentCreateArgs a) => AppointmentCreateRequested?.Invoke(this, a);

    // A single click selects; a second click on the SAME appointment within this window opens the editor.
    private const int DoubleClickMs = 400;
    private long _lastClickTick;
    private AppointmentModel? _lastClickAppt;

    // The default duration of a double-click-created appointment (matches the interaction's drag default).
    private const double DefaultCreateMinutes = 60;
    // Empty-slot double-click tracker (mirrors the appointment one): a second click on the same lane/slot
    // within the window creates; a single click only reports the lane as the selected resource.
    private long _lastEmptyClickTick;
    private DateTime _lastEmptyClickStart;
    private object? _lastEmptyClickResourceId;

    // A single click of an empty Day/Week slot: report the lane (for selected-resource tracking) and, on a
    // second click of the SAME slot within the double-click window, raise a create instead.
    private void OnViewEmptySlotClicked(object? sender, EmptySlotSelectedArgs e)
    {
        EmptySlotSelected?.Invoke(this, e);

        var now = Environment.TickCount64;
        var isDoubleClick = now - _lastEmptyClickTick <= DoubleClickMs
            && _lastEmptyClickStart == e.Start
            && Equals(_lastEmptyClickResourceId, e.ResourceId);
        _lastEmptyClickTick = now;
        _lastEmptyClickStart = e.Start;
        _lastEmptyClickResourceId = e.ResourceId;

        if (!isDoubleClick)
        {
            // Quick-add mode: the single click IS the create.
            if (SingleClickCreates && !IsReadOnly)
            {
                RaiseSlotCreate(e);
            }
            return;
        }
        _lastEmptyClickTick = 0; // consume, so a quick third click starts a fresh single-click

        // In quick-add mode the first click already created — a double-click must not add a second.
        if (SingleClickCreates)
        {
            return;
        }
        RaiseSlotCreate(e);
    }

    private void RaiseSlotCreate(EmptySlotSelectedArgs e)
        => AppointmentCreateRequested?.Invoke(this, new AppointmentCreateArgs
        {
            Start = e.Start,
            End = e.Start.AddMinutes(DefaultCreateMinutes),
            ResourceId = e.ResourceId,
            Position = e.Position,
        });

    private void OnViewAppointmentClicked(object? sender, AppointmentClickArgs e)
    {
        var appt = e.Appointment;

        // Locked appointments are non-interactive: clicking neither selects nor opens the editor (and
        // so can't be Delete-key removed). The lock's red bottom strip is the only feedback. This is the
        // edit-path guard; move/resize are already blocked in the interaction layer.
        if (appt.IsLocked)
        {
            _lastClickAppt = null;
            return;
        }

        if (e.WithCtrl)
        {
            // Ctrl/Cmd+Click toggles membership; building a multi-selection does not open the editor.
            var existing = _selection.FirstOrDefault(x => TimeGridView.SameAppointment(x, appt));
            if (existing is not null)
            {
                _selection.Remove(existing);
            }
            else
            {
                _selection.Add(appt);
            }
            CommitSelection();
            GrabKeyboardFocus();
            _lastClickAppt = null;
            return;
        }

        // Plain click → single-select only. A SECOND click on the same appointment within the double-click
        // window opens the editor (single = select, double = edit).
        _selection.Clear();
        _selection.Add(appt);
        CommitSelection();
        GrabKeyboardFocus();

        var now = Environment.TickCount64;
        var isDoubleClick = _lastClickAppt is not null
            && TimeGridView.SameAppointment(_lastClickAppt, appt)
            && now - _lastClickTick <= DoubleClickMs;
        _lastClickTick = now;
        _lastClickAppt = appt;

        if (!isDoubleClick)
        {
            return;
        }
        _lastClickAppt = null; // consume, so a quick third click starts a fresh single-click

        var args = new AppointmentEditRequestedArgs { Appointment = appt };
        AppointmentEditRequested?.Invoke(this, args);
        if (!args.Handled)
        {
            ShowDefaultEditor(appt);
        }
    }

    // Right-click on an appointment: select it (highlight) so the menu targets the same event, then surface
    // the context request for the host to show its menu. Locked appointments aren't selectable, but the
    // request is still raised so the host can navigate to a participant (a read-only action) from one.
    private void OnViewAppointmentContextRequested(object? sender, AppointmentContextRequestedArgs e)
    {
        if (!e.Appointment.IsLocked)
        {
            _selection.Clear();
            _selection.Add(e.Appointment);
            CommitSelection();
            GrabKeyboardFocus();
        }
        AppointmentContextRequested?.Invoke(this, e);
    }

    // Move keyboard focus onto the scheduler after a pointer-driven selection so the Delete key routes to
    // OnKeyDown. FocusState.Pointer marks it as a pointer-originated focus (no focus rectangle). Only called
    // from genuine user clicks — never from host-driven SelectedAppointments writes, which must not steal focus.
    private void GrabKeyboardFocus() => Focus(FocusState.Pointer);

    /// <summary>Push the current selection set to the DPs + the view visual and notify the host. The
    /// single <see cref="SelectedAppointment"/> mirrors the set only when exactly one is selected (the
    /// editor's single target); a multi-selection leaves it null so the host hides its editor.</summary>
    private void CommitSelection()
    {
        _selectionSyncing = true;
        SelectedAppointments = _selection.ToArray();
        SelectedAppointment = _selection.Count == 1 ? _selection[0] : null;
        _selectionSyncing = false;

        if (ViewHost?.Child is TimeGridView grid)
        {
            grid.ApplySelection(_selection);
        }
        SelectionChanged?.Invoke(this, SelectedAppointments ?? Array.Empty<AppointmentModel>());
    }

    // Fallback editor when no host handled AppointmentEditRequested. Deferred: a built-in default
    // editor popup is planned; for now the host is expected to handle the event.
    private void ShowDefaultEditor(AppointmentModel appt)
    {
        // TODO: built-in default event editor popup (see AppointmentEditRequested).
    }

    private void ForwardMove(object? sender, AppointmentMoveArgs a) => AppointmentMoveRequested?.Invoke(this, a);

    private void ForwardResize(object? sender, AppointmentResizeArgs a) => AppointmentResizeRequested?.Invoke(this, a);

    private void ForwardMoveMany(object? sender, AppointmentsMoveArgs a) => AppointmentsMoveRequested?.Invoke(this, a);

    private void ForwardResizeMany(object? sender, AppointmentsResizeArgs a) => AppointmentsResizeRequested?.Invoke(this, a);

    private void ForwardExternalDrop(object? sender, ExternalDropArgs a) => ExternalAppointmentDropped?.Invoke(this, a);

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!IsReadOnly && _selection.Count > 0 && e.Key == VirtualKey.Delete)
        {
            DeleteSelected(IsShiftDown());
            e.Handled = true;
        }
    }

    /// <summary>True when Shift is currently held. Read at the moment of the delete so the host can skip
    /// its confirmation (Shift+Delete = delete without confirming).</summary>
    private static bool IsShiftDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    /// <summary>Raise a delete request for the current selection (Delete key or host toolbar) and clear it.
    /// The control never mutates data — the host persists the deletion and refreshes. <paramref name="withShift"/>
    /// is surfaced on the event so the host may skip its confirmation prompt.</summary>
    public void DeleteSelected(bool withShift = false)
    {
        if (IsReadOnly || _selection.Count == 0)
        {
            return;
        }
        // Locked appointments can't be deleted (defence in depth — a locked item shouldn't be selectable,
        // but a host may set the selection directly). Delete the rest; bail if nothing is deletable.
        var deletable = _selection.Where(a => !a.IsLocked).ToArray();
        if (deletable.Length == 0)
        {
            return;
        }
        AppointmentsDeleteRequested?.Invoke(this, new AppointmentsDeleteArgs { Appointments = deletable, WithShift = withShift });
        _selection.Clear();
        CommitSelection();
    }

    #region Appointments

    /// <summary>The appointments to display. The control treats this as read-only input.</summary>
    public IReadOnlyList<AppointmentModel>? Appointments
    {
        get => (IReadOnlyList<AppointmentModel>?)GetValue(AppointmentsProperty);
        set => SetValue(AppointmentsProperty, value);
    }

    public static readonly DependencyProperty AppointmentsProperty =
        DependencyProperty.Register(
            nameof(Appointments),
            typeof(IReadOnlyList<AppointmentModel>),
            typeof(SchedulerControl),
            new PropertyMetadata(null, OnSchedulerInvalidated));

    /// <summary>The clicked/selected appointment, shown with the selected visual. Set it to null to
    /// clear the selection (e.g. when an editor closes). Changing it does not rebuild the view.</summary>
    public AppointmentModel? SelectedAppointment
    {
        get => (AppointmentModel?)GetValue(SelectedAppointmentProperty);
        set => SetValue(SelectedAppointmentProperty, value);
    }

    public static readonly DependencyProperty SelectedAppointmentProperty =
        DependencyProperty.Register(
            nameof(SelectedAppointment),
            typeof(AppointmentModel),
            typeof(SchedulerControl),
            new PropertyMetadata(null, OnSelectedAppointmentChanged));

    private static void OnSelectedAppointmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Ignore the echo from CommitSelection's own write; only react to external writes (host clears
        // or restores a single selection), which resync the set and re-apply the visual.
        if (d is not SchedulerControl c || c._selectionSyncing)
        {
            return;
        }
        c._selection.Clear();
        if (c.SelectedAppointment is { } a)
        {
            c._selection.Add(a);
        }
        c.CommitSelection();
    }

    /// <summary>The full selection set (Ctrl+Click multi-select). The control updates it as the user
    /// selects; the host reads it (and <see cref="SelectionChanged"/>) for batch actions and to hide
    /// its editor when more than one is selected.</summary>
    public IReadOnlyList<AppointmentModel>? SelectedAppointments
    {
        get => (IReadOnlyList<AppointmentModel>?)GetValue(SelectedAppointmentsProperty);
        private set => SetValue(SelectedAppointmentsProperty, value);
    }

    public static readonly DependencyProperty SelectedAppointmentsProperty =
        DependencyProperty.Register(
            nameof(SelectedAppointments),
            typeof(IReadOnlyList<AppointmentModel>),
            typeof(SchedulerControl),
            new PropertyMetadata(null));

    #endregion

    #region Appointment templating

    /// <summary>
    /// Optional custom template for full time-grid blocks (Day / Week). The template's DataContext
    /// is an <see cref="AppointmentContext"/> (precomputed brushes, contrast foreground, time
    /// strings, status flags). Null = the built-in default block.
    /// </summary>
    public DataTemplate? AppointmentTemplate
    {
        get => (DataTemplate?)GetValue(AppointmentTemplateProperty);
        set => SetValue(AppointmentTemplateProperty, value);
    }

    public static readonly DependencyProperty AppointmentTemplateProperty =
        DependencyProperty.Register(
            nameof(AppointmentTemplate),
            typeof(DataTemplate),
            typeof(SchedulerControl),
            new PropertyMetadata(null, OnSchedulerInvalidated));

    /// <summary>Custom template for compact chips (Month cells, all-day rows). Null = built-in default.</summary>
    public DataTemplate? AppointmentChipTemplate
    {
        get => (DataTemplate?)GetValue(AppointmentChipTemplateProperty);
        set => SetValue(AppointmentChipTemplateProperty, value);
    }

    public static readonly DependencyProperty AppointmentChipTemplateProperty =
        DependencyProperty.Register(
            nameof(AppointmentChipTemplate),
            typeof(DataTemplate),
            typeof(SchedulerControl),
            new PropertyMetadata(null, OnSchedulerInvalidated));

    /// <summary>Custom template for Planner timeline bars. Null = built-in default.</summary>
    public DataTemplate? AppointmentBarTemplate
    {
        get => (DataTemplate?)GetValue(AppointmentBarTemplateProperty);
        set => SetValue(AppointmentBarTemplateProperty, value);
    }

    public static readonly DependencyProperty AppointmentBarTemplateProperty =
        DependencyProperty.Register(
            nameof(AppointmentBarTemplate),
            typeof(DataTemplate),
            typeof(SchedulerControl),
            new PropertyMetadata(null, OnSchedulerInvalidated));

    /// <summary>Custom template for the hover popup shown over an appointment. Its DataContext is an
    /// <see cref="AppointmentContext"/>. Null = the built-in default popup (name, time, status, note).</summary>
    public DataTemplate? AppointmentTooltipTemplate
    {
        get => (DataTemplate?)GetValue(AppointmentTooltipTemplateProperty);
        set => SetValue(AppointmentTooltipTemplateProperty, value);
    }

    public static readonly DependencyProperty AppointmentTooltipTemplateProperty =
        DependencyProperty.Register(
            nameof(AppointmentTooltipTemplate),
            typeof(DataTemplate),
            typeof(SchedulerControl),
            new PropertyMetadata(null, OnSchedulerInvalidated));

    private Func<AppointmentContext, FrameworkElement?>? _appointmentRenderer;
    private Func<AppointmentContext, FrameworkElement?>? _appointmentTooltipRenderer;

    /// <summary>
    /// Optional code renderer, the highest-priority hook: returns the block content for an
    /// appointment (switch on <see cref="AppointmentContext.Surface"/> to vary by shape), or null
    /// to fall through to the matching template / built-in default. Set in code-behind.
    /// </summary>
    public Func<AppointmentContext, FrameworkElement?>? AppointmentRenderer
    {
        get => _appointmentRenderer;
        set
        {
            _appointmentRenderer = value;
            RenderActiveView();
        }
    }

    /// <summary>
    /// Optional code renderer for the hover popup: returns the popup content for an appointment, or
    /// null to fall through to <see cref="AppointmentTooltipTemplate"/> / the built-in default.
    /// </summary>
    public Func<AppointmentContext, FrameworkElement?>? AppointmentTooltipRenderer
    {
        get => _appointmentTooltipRenderer;
        set
        {
            _appointmentTooltipRenderer = value;
            RenderActiveView();
        }
    }

    #endregion

    #region SchedulerResources

    /// <summary>
    /// The resources (Planner lanes / grouping keys) available to the control.
    /// Named <c>SchedulerResources</c> rather than <c>Resources</c> to avoid shadowing
    /// <see cref="FrameworkElement.Resources"/> (the XAML ResourceDictionary).
    /// </summary>
    public IReadOnlyList<ResourceModel>? SchedulerResources
    {
        get => (IReadOnlyList<ResourceModel>?)GetValue(SchedulerResourcesProperty);
        set => SetValue(SchedulerResourcesProperty, value);
    }

    public static readonly DependencyProperty SchedulerResourcesProperty =
        DependencyProperty.Register(
            nameof(SchedulerResources),
            typeof(IReadOnlyList<ResourceModel>),
            typeof(SchedulerControl),
            new PropertyMetadata(null, OnSchedulerInvalidated));

    #endregion

    #region CurrentView

    /// <summary>The active calendar view. Defaults to <see cref="SchedulerView.Week"/>.</summary>
    public SchedulerView CurrentView
    {
        get => (SchedulerView)GetValue(CurrentViewProperty);
        set => SetValue(CurrentViewProperty, value);
    }

    public static readonly DependencyProperty CurrentViewProperty =
        DependencyProperty.Register(
            nameof(CurrentView),
            typeof(SchedulerView),
            typeof(SchedulerControl),
            new PropertyMetadata(SchedulerView.Week, OnSchedulerInvalidated));

    #endregion

    #region SelectedDate

    /// <summary>The anchor date the current view is built around. Time component is ignored.</summary>
    public DateTimeOffset SelectedDate
    {
        get => (DateTimeOffset)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(
            nameof(SelectedDate),
            typeof(DateTimeOffset),
            typeof(SchedulerControl),
            new PropertyMetadata(default(DateTimeOffset), OnSchedulerInvalidated));

    #endregion

    #region IsReadOnly

    /// <summary>When true, all interaction (move/resize/create/drop) is suppressed.</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(
            nameof(IsReadOnly),
            typeof(bool),
            typeof(SchedulerControl),
            new PropertyMetadata(false, OnSchedulerInvalidated));

    #endregion

    #region SingleClickCreates

    /// <summary>Quick-add mode: when true, a single click on an empty Day/Week slot raises
    /// <see cref="AppointmentCreateRequested"/> immediately instead of waiting for a double-click. The second
    /// click of a double-click is swallowed, so a double-click never creates twice. Default false.</summary>
    public bool SingleClickCreates
    {
        get => (bool)GetValue(SingleClickCreatesProperty);
        set => SetValue(SingleClickCreatesProperty, value);
    }

    public static readonly DependencyProperty SingleClickCreatesProperty =
        DependencyProperty.Register(
            nameof(SingleClickCreates),
            typeof(bool),
            typeof(SchedulerControl),
            new PropertyMetadata(false));

    #endregion

    #region TimeIncrement

    /// <summary>Vertical time-scale configuration for the time-grid views.</summary>
    public TimeIncrementConfig TimeIncrement
    {
        get => (TimeIncrementConfig)GetValue(TimeIncrementProperty);
        set => SetValue(TimeIncrementProperty, value);
    }

    public static readonly DependencyProperty TimeIncrementProperty =
        DependencyProperty.Register(
            nameof(TimeIncrement),
            typeof(TimeIncrementConfig),
            typeof(SchedulerControl),
            new PropertyMetadata(null, OnSchedulerInvalidated));

    #endregion

    #region GroupMode

    /// <summary>Planner header hierarchy (resource-major vs day-major).</summary>
    public SchedulerGroupMode GroupMode
    {
        get => (SchedulerGroupMode)GetValue(GroupModeProperty);
        set => SetValue(GroupModeProperty, value);
    }

    public static readonly DependencyProperty GroupModeProperty =
        DependencyProperty.Register(
            nameof(GroupMode),
            typeof(SchedulerGroupMode),
            typeof(SchedulerControl),
            new PropertyMetadata(SchedulerGroupMode.ByResource, OnSchedulerInvalidated));

    #endregion

    #region HideIdleResources

    /// <summary>When true, resource lanes/columns with no events in the visible range are hidden.</summary>
    public bool HideIdleResources
    {
        get => (bool)GetValue(HideIdleResourcesProperty);
        set => SetValue(HideIdleResourcesProperty, value);
    }

    public static readonly DependencyProperty HideIdleResourcesProperty =
        DependencyProperty.Register(
            nameof(HideIdleResources),
            typeof(bool),
            typeof(SchedulerControl),
            new PropertyMetadata(false, OnSchedulerInvalidated));

    #endregion

    #region VisibleDays

    /// <summary>How many consecutive days the Day view shows (1 = a single day; set by the
    /// date-range picker). Other views ignore it.</summary>
    public int VisibleDays
    {
        get => (int)GetValue(VisibleDaysProperty);
        set => SetValue(VisibleDaysProperty, value);
    }

    public static readonly DependencyProperty VisibleDaysProperty =
        DependencyProperty.Register(
            nameof(VisibleDays),
            typeof(int),
            typeof(SchedulerControl),
            new PropertyMetadata(1, OnSchedulerInvalidated));

    #endregion

    #region SelectedDates

    /// <summary>
    /// An explicit set of NON-contiguous dates to display side by side (the "next 4 Saturdays"
    /// feature — Legacy's multi-select DateNavigator). When non-null and non-empty, the Day view
    /// renders exactly these dates as adjacent columns (gaps collapsed) and the Agenda view groups
    /// by them, overriding <see cref="SelectedDate"/> + <see cref="VisibleDays"/>. Null/empty (the
    /// default) = the ordinary single-anchor behaviour. Only Day and Agenda honour it; switching to
    /// Week/Month/Planner clears it. A contiguous run collapses back to the anchor + span path, so
    /// this only ever holds a genuinely scattered set.
    /// </summary>
    public IReadOnlyList<DateTime>? SelectedDates
    {
        get => (IReadOnlyList<DateTime>?)GetValue(SelectedDatesProperty);
        set => SetValue(SelectedDatesProperty, value);
    }

    public static readonly DependencyProperty SelectedDatesProperty =
        DependencyProperty.Register(
            nameof(SelectedDates),
            typeof(IReadOnlyList<DateTime>),
            typeof(SchedulerControl),
            new PropertyMetadata(null, OnSchedulerInvalidated));

    /// <summary>True when a genuine multi-date set is active (drives the Day/Agenda date columns).</summary>
    private bool HasMultiDateSelection => SelectedDates is { Count: > 0 };

    /// <summary>The sorted, de-duplicated multi-date set (empty when none is active).</summary>
    private IReadOnlyList<DateTime> MultiDates =>
        SelectedDates is { Count: > 0 } dates
            ? dates.Select(d => d.Date).Distinct().OrderBy(d => d).ToArray()
            : Array.Empty<DateTime>();

    #endregion

    #region ColumnMinWidth

    /// <summary>Minimum width (px) of a resource/date column in Day/Week; below this the body
    /// scrolls horizontally instead of squeezing columns.</summary>
    public double ColumnMinWidth
    {
        get => (double)GetValue(ColumnMinWidthProperty);
        set => SetValue(ColumnMinWidthProperty, value);
    }

    public static readonly DependencyProperty ColumnMinWidthProperty =
        DependencyProperty.Register(
            nameof(ColumnMinWidth),
            typeof(double),
            typeof(SchedulerControl),
            new PropertyMetadata(80d, OnSchedulerInvalidated));

    #endregion

    #region AppointmentCornerRadius

    /// <summary>Corner radius (px) of timed event blocks and the selection outline. Settings-driven.</summary>
    public double AppointmentCornerRadius
    {
        get => (double)GetValue(AppointmentCornerRadiusProperty);
        set => SetValue(AppointmentCornerRadiusProperty, value);
    }

    public static readonly DependencyProperty AppointmentCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(AppointmentCornerRadius),
            typeof(double),
            typeof(SchedulerControl),
            new PropertyMetadata(4d, OnSchedulerInvalidated));

    #endregion

    #region ShowTiles

    /// <summary>Whether data-driven appointment tiles (<see cref="AppointmentModel.Tiles"/>) render.
    /// Settings-driven ("Show tiles"); adornments always render. Default true.</summary>
    public bool ShowTiles
    {
        get => (bool)GetValue(ShowTilesProperty);
        set => SetValue(ShowTilesProperty, value);
    }

    public static readonly DependencyProperty ShowTilesProperty =
        DependencyProperty.Register(
            nameof(ShowTiles),
            typeof(bool),
            typeof(SchedulerControl),
            new PropertyMetadata(true, OnSchedulerInvalidated));

    #endregion

    #region FirstDayOfWeek

    /// <summary>The day the Week view and Planner week start on (Sunday or Monday).</summary>
    public DayOfWeek FirstDayOfWeek
    {
        get => (DayOfWeek)GetValue(FirstDayOfWeekProperty);
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    public static readonly DependencyProperty FirstDayOfWeekProperty =
        DependencyProperty.Register(
            nameof(FirstDayOfWeek),
            typeof(DayOfWeek),
            typeof(SchedulerControl),
            new PropertyMetadata(DayOfWeek.Sunday, OnSchedulerInvalidated));

    #endregion

    #region Work hours

    /// <summary>Start of the global working-hours band shaded in Day/Week.</summary>
    public TimeSpan WorkDayStart
    {
        get => (TimeSpan)GetValue(WorkDayStartProperty);
        set => SetValue(WorkDayStartProperty, value);
    }

    public static readonly DependencyProperty WorkDayStartProperty =
        DependencyProperty.Register(
            nameof(WorkDayStart),
            typeof(TimeSpan),
            typeof(SchedulerControl),
            new PropertyMetadata(TimeSpan.FromHours(8), OnSchedulerInvalidated));

    /// <summary>End of the global working-hours band shaded in Day/Week.</summary>
    public TimeSpan WorkDayEnd
    {
        get => (TimeSpan)GetValue(WorkDayEndProperty);
        set => SetValue(WorkDayEndProperty, value);
    }

    public static readonly DependencyProperty WorkDayEndProperty =
        DependencyProperty.Register(
            nameof(WorkDayEnd),
            typeof(TimeSpan),
            typeof(SchedulerControl),
            new PropertyMetadata(TimeSpan.FromHours(17), OnSchedulerInvalidated));

    #endregion

    /// <summary>The effective time-scale, falling back to the default when unset.</summary>
    internal TimeIncrementConfig EffectiveTimeIncrement => TimeIncrement ?? TimeIncrementConfig.Default;

    private static void OnSchedulerInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SchedulerControl control)
        {
            control.RenderActiveView();
        }
    }

    // Classify a picked date set from the toolbar. A single day or a solid contiguous run folds back
    // into the ordinary anchor + VisibleDays path (rendering + labels stay identical to before); a
    // genuinely scattered set engages SelectedDates. Empty is ignored. The target view stays Agenda when
    // it was Agenda, otherwise Day (a multi-date pick never lands on Week/Month/Planner).
    private void ApplyPickedDates(IReadOnlyList<DateTime> picked)
    {
        var dates = picked.Select(d => d.Date).Distinct().OrderBy(d => d).ToArray();
        if (dates.Length == 0)
        {
            return;
        }

        var targetView = CurrentView == SchedulerView.Agenda ? SchedulerView.Agenda : SchedulerView.Day;
        var contiguous = (dates[^1] - dates[0]).Days + 1 == dates.Length;

        if (contiguous)
        {
            SelectedDates = null;
            SelectedDate = new DateTimeOffset(dates[0]);
            VisibleDays = dates.Length;
        }
        else
        {
            SelectedDates = dates;
            SelectedDate = new DateTimeOffset(dates[0]); // keep the anchor sensible (fetch window seeds off it)
        }
        CurrentView = targetView;
    }

    // The dates the Day/Agenda views should render: the scattered multi-date set when active, else the
    // contiguous anchor..anchor+VisibleDays run.
    private IReadOnlyList<DateTime> EffectiveDayColumns(DateTime anchor, int visibleDays)
        => HasMultiDateSelection ? MultiDates : DayDates(anchor, visibleDays);

    private void Navigate(int direction)
    {
        // Multi-date mode: shift the whole scattered set by a week so a "next N Saturdays" pick pages to
        // the following/previous N Saturdays (keeps the weekday alignment). The anchor follows the set.
        if (HasMultiDateSelection && CurrentView is SchedulerView.Day or SchedulerView.Agenda)
        {
            var shifted = MultiDates.Select(d => d.AddDays(7 * direction)).ToArray();
            SelectedDates = shifted;
            SelectedDate = new DateTimeOffset(shifted[0]);
            return;
        }

        SelectedDate = CurrentView switch
        {
            // Day and Agenda both page by the selected span (VisibleDays).
            SchedulerView.Day or SchedulerView.Agenda => SelectedDate.AddDays(direction * Math.Max(1, VisibleDays)),
            SchedulerView.Month => SelectedDate.AddMonths(direction),
            _ => SelectedDate.AddDays(7 * direction), // Week / Planner
        };
    }

    private void RenderActiveView()
    {
        // Named parts exist after InitializeComponent; guard anyway for safety.
        if (Toolbar is null || ViewHost is null)
        {
            return;
        }

        var palette = SchedulerPalette.For(ActualTheme);
        Background = palette.SurfaceBackground; // opaque; keeps a host watermark from bleeding through cells
        var anchor = SelectedDate.Date;
        var appointments = Appointments ?? Array.Empty<AppointmentModel>();
        var resources = SchedulerResources ?? Array.Empty<ResourceModel>();
        var increment = EffectiveTimeIncrement;
        var templating = new AppointmentTemplating
        {
            TimedTemplate = AppointmentTemplate,
            ChipTemplate = AppointmentChipTemplate,
            BarTemplate = AppointmentBarTemplate,
            TooltipTemplate = AppointmentTooltipTemplate,
            Renderer = AppointmentRenderer,
            TooltipRenderer = AppointmentTooltipRenderer,
            CornerRadius = AppointmentCornerRadius,
            ShowTiles = ShowTiles,
        };

        var visibleDays = Math.Max(1, VisibleDays);

        var multiDates = MultiDates; // sorted/distinct scattered set, or empty
        var multiActive = multiDates.Count > 0 && CurrentView is SchedulerView.Day or SchedulerView.Agenda;

        Toolbar.SetActiveView(CurrentView);
        Toolbar.SetPeriodLabel(multiActive
            ? BuildMultiDateLabel(multiDates)
            : BuildPeriodLabel(CurrentView, anchor, visibleDays, FirstDayOfWeek));
        Toolbar.SetGroupMode(GroupMode);
        Toolbar.SetHideIdle(HideIdleResources);
        Toolbar.SetTimeIncrement(increment);
        Toolbar.SetColumnMinWidth(ColumnMinWidth);
        Toolbar.SetCornerRadius(AppointmentCornerRadius);
        Toolbar.SetShowTiles(ShowTiles);
        Toolbar.SetFirstDayOfWeek(FirstDayOfWeek);
        Toolbar.SetWorkHours(WorkDayStart, WorkDayEnd);
        // Seed the picker with the dates currently on screen so reopening it reflects the selection.
        Toolbar.SetCurrentDates(multiActive
            ? multiDates
            : CurrentView is SchedulerView.Day or SchedulerView.Agenda
                ? DayDates(anchor, visibleDays)
                : new[] { anchor });
        // Resource grouping / hide-idle only apply to the resource-aware views.
        var resourceAware = CurrentView is SchedulerView.Day or SchedulerView.Week or SchedulerView.Planner;
        Toolbar.SetResourceOptionsVisible(resourceAware && resources.Count > 0);

        switch (CurrentView)
        {
            case SchedulerView.Day:
            {
                var view = new TimeGridView();
                view.CreateRequested += ForwardCreate;
                view.EmptySlotClicked += OnViewEmptySlotClicked;
                view.MoveRequested += ForwardMove;
                view.ResizeRequested += ForwardResize;
                view.MoveManyRequested += ForwardMoveMany;
                view.ResizeManyRequested += ForwardResizeMany;
                view.ExternalDropRequested += ForwardExternalDrop;
                view.AppointmentClicked += OnViewAppointmentClicked;
                view.AppointmentContextMenuRequested += OnViewAppointmentContextRequested;
                view.ScrollOffsetChanged += OnViewScrollOffsetChanged;
                view.PendingVerticalOffset = _scrollV;
                view.PendingHorizontalOffset = _scrollH;
                view.SetData(EffectiveDayColumns(anchor, visibleDays), appointments, resources, GroupMode, HideIdleResources, ColumnMinWidth, WorkDayStart, WorkDayEnd, increment, palette, IsReadOnly, templating, _selection);
                ViewHost.Child = view;
                break;
            }
            case SchedulerView.Week:
            {
                var view = new TimeGridView();
                view.CreateRequested += ForwardCreate;
                view.EmptySlotClicked += OnViewEmptySlotClicked;
                view.MoveRequested += ForwardMove;
                view.ResizeRequested += ForwardResize;
                view.MoveManyRequested += ForwardMoveMany;
                view.ResizeManyRequested += ForwardResizeMany;
                view.ExternalDropRequested += ForwardExternalDrop;
                view.AppointmentClicked += OnViewAppointmentClicked;
                view.AppointmentContextMenuRequested += OnViewAppointmentContextRequested;
                view.ScrollOffsetChanged += OnViewScrollOffsetChanged;
                view.PendingVerticalOffset = _scrollV;
                view.PendingHorizontalOffset = _scrollH;
                view.SetData(WeekDates(anchor, FirstDayOfWeek), appointments, resources, GroupMode, HideIdleResources, ColumnMinWidth, WorkDayStart, WorkDayEnd, increment, palette, IsReadOnly, templating, _selection);
                ViewHost.Child = view;
                break;
            }
            case SchedulerView.Month:
            {
                var view = new MonthView();
                view.SetData(anchor, appointments, palette, templating);
                ViewHost.Child = view;
                break;
            }
            case SchedulerView.Agenda:
            {
                var view = new AgendaView();
                if (multiDates.Count > 0)
                {
                    view.SetData(multiDates, appointments, resources, palette, templating);
                }
                else
                {
                    view.SetData(anchor, appointments, resources, visibleDays, palette, templating);
                }
                ViewHost.Child = view;
                break;
            }
            case SchedulerView.Planner:
            {
                var view = new PlannerView();
                view.CreateRequested += ForwardCreate;
                view.MoveRequested += ForwardMove;
                view.ResizeRequested += ForwardResize;
                view.ExternalDropRequested += ForwardExternalDrop;
                view.AppointmentContextMenuRequested += OnViewAppointmentContextRequested;
                view.SetData(anchor, appointments, resources, increment, palette, GroupMode, FirstDayOfWeek, IsReadOnly, templating);
                ViewHost.Child = view;
                break;
            }
        }
    }

    private static IReadOnlyList<DateTime> DayDates(DateTime anchor, int count)
    {
        count = Math.Max(1, count);
        var days = new DateTime[count];
        for (var i = 0; i < count; i++)
        {
            days[i] = anchor.Date.AddDays(i);
        }
        return days;
    }

    private static IReadOnlyList<DateTime> WeekDates(DateTime anchor, DayOfWeek firstDay)
    {
        var start = StartOfWeek(anchor, firstDay);
        var days = new DateTime[7];
        for (var i = 0; i < 7; i++)
        {
            days[i] = start.AddDays(i);
        }
        return days;
    }

    private static DateTime StartOfWeek(DateTime date, DayOfWeek firstDay)
    {
        var diff = (7 + (int)date.DayOfWeek - (int)firstDay) % 7;
        return date.Date.AddDays(-diff);
    }

    private static string BuildPeriodLabel(SchedulerView view, DateTime anchor, int visibleDays, DayOfWeek firstDay)
    {
        var ci = System.Globalization.CultureInfo.CurrentCulture;
        switch (view)
        {
            case SchedulerView.Day:
                // Fixed-width-ish format (abbreviated day/month, zero-padded day) so stepping days
                // doesn't resize the label and shove the prev/next buttons around.
                return visibleDays <= 1
                    ? anchor.ToString("ddd, MMM dd yyyy", ci)
                    : DateRange(anchor, anchor.AddDays(visibleDays - 1));
            case SchedulerView.Month:
                return anchor.ToString("MMMM yyyy", ci);
            case SchedulerView.Agenda:
                // Agenda spans the same selected range as Day.
                return visibleDays <= 1
                    ? anchor.ToString("ddd, MMM dd yyyy", ci)
                    : DateRange(anchor, anchor.AddDays(visibleDays - 1));
            case SchedulerView.Planner: // the timeline spans the selected week
            default: // Week
            {
                var start = StartOfWeek(anchor, firstDay);
                return DateRange(start, start.AddDays(6));
            }
        }
    }

    private static string DateRange(DateTime start, DateTime end) =>
        start.Month == end.Month
            ? $"{start:MMM d} – {end.Day}, {end:yyyy}"
            : $"{start:MMM d} – {end:MMM d}, {end:yyyy}";

    // Period-button caption for a scattered multi-date set, e.g. "4 days · Jul 4 – 25, 2026".
    private static string BuildMultiDateLabel(IReadOnlyList<DateTime> dates) =>
        $"{dates.Count} days · {DateRange(dates[0], dates[^1])}";
}
