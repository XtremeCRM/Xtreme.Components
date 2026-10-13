using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Xtreme.Components.Scheduler.Contracts;
using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Rendering;

namespace Xtreme.Components.Scheduler.Interaction;

/// <summary>One column of a time grid: its index plus the day and (optional) resource it maps to.</summary>
internal sealed record TimeColumn(int Index, DateTime Day, object? ResourceId);

/// <summary>
/// Pointer interaction for a time grid (Day/Week/Planner): drag-to-move (with cross-column /
/// cross-resource drop), drag-the-edge-to-resize, and click/drag-to-create on empty slots.
/// </summary>
/// <remarks>
/// Handlers live on a single full-covering, hit-testable overlay so the pointer is always over
/// the same element through a drag — under-pointer dispatch then delivers every PointerMoved
/// (bubbling from the underlying blocks proved unreliable under Uno WASM). Grabbed blocks are
/// found by hit-testing the pointer against the precomputed <see cref="EventLayout"/> data, not
/// the visual tree. The control never mutates data — it reports results through callbacks.
/// </remarks>
internal sealed class TimeGridInteraction
{
    private enum Mode { None, Move, ResizeTop, ResizeBottom, Create }

    private const double EdgeThreshold = 7;
    private const double DragThreshold = 4;
    private const double MinDurationMinutes = 15;
    private const double DefaultCreateMinutes = 60;

    private readonly Canvas _overlay;
    private readonly IReadOnlyList<TimeColumn> _columns;
    private readonly IReadOnlyList<IReadOnlyList<EventLayout>> _columnLayouts;
    private readonly double? _fixedColumnWidth;
    private readonly TimeIncrementConfig _increment;
    private readonly SchedulerPalette _palette;
    private readonly bool _isReadOnly;

    private readonly Action<AppointmentCreateArgs> _onCreate;
    private readonly Action<EmptySlotSelectedArgs>? _onEmptyClick;
    private readonly Action<AppointmentMoveArgs> _onMove;
    private readonly Action<AppointmentResizeArgs> _onResize;
    private readonly Action<ExternalDropArgs>? _onExternalDrop;
    private readonly Action<AppointmentModel, bool>? _onClick;
    private readonly Action<AppointmentModel, Point?>? _onContext;
    private readonly Action<AppointmentModel?, Point>? _onHover;
    private readonly Func<AppointmentModel, bool>? _isResizable;
    private readonly Action<InputSystemCursorShape?>? _onCursor;
    private readonly Func<IReadOnlyList<AppointmentModel>>? _getSelection;
    private readonly Action<AppointmentsMoveArgs>? _onMoveMany;
    private readonly Action<AppointmentsResizeArgs>? _onResizeMany;
    private AppointmentModel? _hoverAppt;
    private bool _pressCtrl;
    private bool _fineSnap;
    private bool _copyDrag;

    private Mode _mode = Mode.None;
    private AppointmentModel? _appt;
    private double _origStartMin;
    private double _origEndMin;
    private double _grabOffsetY;
    private double _createStartMin;
    private int _grabColumnIndex;
    private Point _pressPoint;
    private bool _moved;
    private readonly List<Border> _ghostPool = new();

    // One grabbed instance of a multi-selection: its grid column + day-relative span. A multi-resource
    // event contributes one entry per lane it appears in.
    private readonly record struct GroupItem(int Col, double StartMin, double EndMin, AppointmentModel Appt);
    private readonly List<GroupItem> _group = new();
    private bool _isGroup;
    private int _grpMinCol, _grpMaxCol;
    private double _grpMinStart, _grpMaxEnd;

    public TimeGridInteraction(
        Canvas overlay,
        IReadOnlyList<TimeColumn> columns,
        IReadOnlyList<IReadOnlyList<EventLayout>> columnLayouts,
        double? fixedColumnWidth,
        TimeIncrementConfig increment,
        SchedulerPalette palette,
        bool isReadOnly,
        Action<AppointmentCreateArgs> onCreate,
        Action<AppointmentMoveArgs> onMove,
        Action<AppointmentResizeArgs> onResize,
        Action<ExternalDropArgs>? onExternalDrop = null,
        Action<AppointmentModel, bool>? onClick = null,
        Action<AppointmentModel, Point?>? onContext = null,
        Action<AppointmentModel?, Point>? onHover = null,
        Func<AppointmentModel, bool>? isResizable = null,
        Action<InputSystemCursorShape?>? onCursor = null,
        Func<IReadOnlyList<AppointmentModel>>? getSelection = null,
        Action<AppointmentsMoveArgs>? onMoveMany = null,
        Action<AppointmentsResizeArgs>? onResizeMany = null,
        Action<EmptySlotSelectedArgs>? onEmptyClick = null)
    {
        _overlay = overlay;
        _columns = columns;
        _columnLayouts = columnLayouts;
        _fixedColumnWidth = fixedColumnWidth;
        _increment = increment;
        _palette = palette;
        _isReadOnly = isReadOnly;
        _onCreate = onCreate;
        _onEmptyClick = onEmptyClick;
        _onMove = onMove;
        _onResize = onResize;
        _onExternalDrop = onExternalDrop;
        _onClick = onClick;
        _onContext = onContext;
        _onHover = onHover;
        _isResizable = isResizable;
        _onCursor = onCursor;
        _getSelection = getSelection;
        _onMoveMany = onMoveMany;
        _onResizeMany = onResizeMany;

        // Click-to-select and hover (for tooltips) work even in read-only — the overlay covers the
        // blocks, so it's the only surface that sees the pointer. Drag / create / resize are edit-only.
        _overlay.PointerPressed += OnPointerPressed;
        _overlay.PointerReleased += OnPointerReleased;
        _overlay.PointerCanceled += OnPointerCanceled;
        _overlay.PointerMoved += OnPointerMoved;
        _overlay.PointerExited += OnPointerExited;

        if (_isReadOnly)
        {
            return;
        }

        // External drag-drop (e.g. an unscheduled-queue item dropped on a slot).
        _overlay.AllowDrop = true;
        _overlay.DragOver += OnDragOver;
        _overlay.Drop += OnDrop;
        _overlay.DragLeave += OnDragLeave;
    }

    private double PxPerMin => _increment.PixelsPerMinute;

    private double ColumnWidth =>
        _fixedColumnWidth ?? (_columns.Count > 0 ? _overlay.ActualWidth / _columns.Count : _overlay.ActualWidth);

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_overlay);
        var p = point.Position;
        _pressPoint = p;
        _moved = false;
        _isGroup = false;
        _group.Clear();
        _pressCtrl = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
        _fineSnap = InteractionModifiers.IsFineSnap(e.KeyModifiers);
        _grabColumnIndex = ColumnIndexAt(p.X);

        // Dismiss any hover popup the moment the user starts interacting.
        if (_hoverAppt is not null)
        {
            _hoverAppt = null;
            _onHover?.Invoke(null, default);
        }

        // Right button = a context-menu request on the appointment under the pointer (if any). It never
        // starts a drag/resize/create, and works in read-only too (the host still navigates from it).
        if (point.Properties.IsRightButtonPressed)
        {
            _mode = Mode.None;
            _appt = null;
            if (_onContext is not null && HitTest(p.X, p.Y) is { } ctxLayout)
            {
                _onContext(ctxLayout.Appointment, ToRoot(p));
            }
            e.Handled = true;
            return;
        }

        var hit = HitTest(p.X, p.Y);
        if (hit is { } layout)
        {
            _appt = layout.Appointment;
            _origStartMin = layout.StartMinutes;
            _origEndMin = layout.EndMinutes;
            _grabOffsetY = p.Y - layout.StartMinutes * PxPerMin;

            if (_isReadOnly || layout.Appointment.IsLocked)
            {
                _mode = Mode.None; // read-only or locked: tap only, no drag/resize (locked tap is a no-op host-side)
            }
            else
            {
                // Resize only on the already-selected appointment (so two touching events are never
                // ambiguous at the shared edge); on any other appointment an edge press is a move/click.
                var canResize = _isResizable?.Invoke(layout.Appointment) ?? true;
                var topY = layout.StartMinutes * PxPerMin;
                var botY = layout.EndMinutes * PxPerMin;
                _mode = canResize && p.Y - topY < EdgeThreshold ? Mode.ResizeTop
                    : canResize && botY - p.Y < EdgeThreshold ? Mode.ResizeBottom
                    : Mode.Move;

                // Grabbing part of a multi-selection drives the whole group by a shared delta.
                var selection = _getSelection?.Invoke();
                if (selection is { Count: > 1 } && selection.Any(s => Same(s, _appt)))
                {
                    BuildGroup(selection);
                }
            }
        }
        else
        {
            _appt = null;
            _mode = _isReadOnly ? Mode.None : Mode.Create;
            if (!_isReadOnly)
            {
                _createStartMin = Snap(p.Y / PxPerMin);
            }
        }

        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_overlay).Position;
        _fineSnap = InteractionModifiers.IsFineSnap(e.KeyModifiers);
        _copyDrag = InteractionModifiers.IsCopy(e.KeyModifiers);

        // Not pressing: report the appointment under the pointer so the host can show a hover popup,
        // and reflect what a press would do via the cursor (move vs resize).
        if (_mode == Mode.None)
        {
            var hit = HitTest(p.X, p.Y);
            UpdateCursor(hit, p);
            var hovered = hit?.Appointment;
            if (!ReferenceEquals(hovered, _hoverAppt))
            {
                _hoverAppt = hovered;
                _onHover?.Invoke(hovered, p);
            }
            return;
        }

        if (!_moved && Math.Abs(p.Y - _pressPoint.Y) < DragThreshold && Math.Abs(p.X - _pressPoint.X) < DragThreshold)
        {
            return;
        }
        _moved = true;

        var colIndex = ColumnIndexAt(p.X);

        // A multi-selection drag/resize moves the whole group by one shared, group-clamped delta.
        if (_isGroup)
        {
            DrawGroupGhosts(p, colIndex);
            return;
        }

        switch (_mode)
        {
            case Mode.Move:
            {
                var duration = _origEndMin - _origStartMin;
                var newStart = Math.Clamp(Snap((p.Y - _grabOffsetY) / PxPerMin), 0, 24 * 60 - duration);
                DrawGhost(colIndex, newStart, newStart + duration, _appt);
                break;
            }
            case Mode.ResizeTop:
            {
                var newStart = Math.Clamp(Snap(p.Y / PxPerMin), 0, _origEndMin - MinDurationMinutes);
                DrawGhost(_grabColumnIndex, newStart, _origEndMin, _appt);
                break;
            }
            case Mode.ResizeBottom:
            {
                var newEnd = Math.Clamp(Snap(p.Y / PxPerMin), _origStartMin + MinDurationMinutes, 24 * 60);
                DrawGhost(_grabColumnIndex, _origStartMin, newEnd, _appt);
                break;
            }
            case Mode.Create:
            {
                var current = Snap(p.Y / PxPerMin);
                var start = Math.Min(_createStartMin, current);
                var end = Math.Max(_createStartMin, current);
                if (end - start < MinDurationMinutes)
                {
                    end = start + MinDurationMinutes;
                }
                DrawGhost(colIndex, start, end, null);
                break;
            }
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_overlay).Position;
        _fineSnap = InteractionModifiers.IsFineSnap(e.KeyModifiers);
        _copyDrag = InteractionModifiers.IsCopy(e.KeyModifiers);
        // Shift at drop is surfaced to the host (it may skip its confirm dialog); the control itself
        // ascribes no meaning to it.
        var withShift = InteractionModifiers.IsShift(e.KeyModifiers);
        var mode = _mode;
        var appt = _appt;
        var moved = _moved;
        var isGroup = _isGroup;

        RemoveGhost();
        _mode = Mode.None;
        _appt = null;
        _moved = false;
        _isGroup = false;

        // A press+release on an appointment without dragging = a click (select/open). Read-only too.
        // _pressCtrl (captured on press) tells the host whether to toggle into a multi-selection.
        if (!moved && appt is not null)
        {
            _onClick?.Invoke(appt, _pressCtrl);
        }

        if (mode == Mode.None)
        {
            return;
        }

        // Multi-selection drag/resize → one batch event with the group-clamped delta applied to all.
        if (isGroup)
        {
            if (moved)
            {
                EmitGroup(p, mode, withShift);
            }
            return;
        }

        var col = _columns[ColumnIndexAt(p.X)];
        switch (mode)
        {
            case Mode.Move when appt is not null && moved:
            {
                var duration = _origEndMin - _origStartMin;
                var newStartMin = Math.Clamp(Snap((p.Y - _grabOffsetY) / PxPerMin), 0, 24 * 60 - duration);
                var newStart = col.Day.AddMinutes(newStartMin);
                _onMove(new AppointmentMoveArgs
                {
                    Appointment = appt,
                    NewStart = newStart,
                    NewEnd = newStart.AddMinutes(duration),
                    FromResourceId = GrabbedResourceId(),
                    ToResourceId = col.ResourceId,
                    IsCopy = _copyDrag,
                    WithShift = withShift,
                });
                break;
            }
            case Mode.ResizeTop when appt is not null && moved:
            {
                var newStartMin = Math.Clamp(Snap(p.Y / PxPerMin), 0, _origEndMin - MinDurationMinutes);
                _onResize(new AppointmentResizeArgs
                {
                    Appointment = appt,
                    NewStart = appt.Start.Date.AddMinutes(newStartMin),
                    NewEnd = appt.End,
                    WithShift = withShift,
                });
                break;
            }
            case Mode.ResizeBottom when appt is not null && moved:
            {
                var newEndMin = Math.Clamp(Snap(p.Y / PxPerMin), _origStartMin + MinDurationMinutes, 24 * 60);
                _onResize(new AppointmentResizeArgs
                {
                    Appointment = appt,
                    NewStart = appt.Start,
                    NewEnd = appt.Start.Date.AddMinutes(newEndMin),
                    WithShift = withShift,
                });
                break;
            }
            case Mode.Create:
            {
                // A click on an empty slot no longer creates — it reports the lane + time so the host can
                // track a "selected resource". The double-click→create gesture is detected one level up
                // (SchedulerControl), which re-raises a create from a second click. A DRAG still creates a
                // ranged appointment directly.
                if (!moved && _onEmptyClick is not null)
                {
                    _onEmptyClick(new EmptySlotSelectedArgs
                    {
                        Start = col.Day.AddMinutes(_createStartMin),
                        ResourceId = col.ResourceId,
                        Position = ToRoot(p),
                    });
                    break;
                }

                double startMin, endMin;
                if (moved)
                {
                    var current = Snap(p.Y / PxPerMin);
                    startMin = Math.Min(_createStartMin, current);
                    endMin = Math.Max(_createStartMin, current);
                    if (endMin - startMin < MinDurationMinutes)
                    {
                        endMin = startMin + MinDurationMinutes;
                    }
                }
                else
                {
                    startMin = _createStartMin;
                    endMin = Math.Min(startMin + DefaultCreateMinutes, 24 * 60);
                }
                _onCreate(new AppointmentCreateArgs
                {
                    Start = col.Day.AddMinutes(startMin),
                    End = col.Day.AddMinutes(endMin),
                    ResourceId = col.ResourceId,
                    Position = ToRoot(p),
                });
                break;
            }
        }
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        RemoveGhost();
        _mode = Mode.None;
        _appt = null;
        _moved = false;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _onCursor?.Invoke(null);
        if (_hoverAppt is not null)
        {
            _hoverAppt = null;
            _onHover?.Invoke(null, default);
        }
    }

    // Drive the pointer cursor on hover: an appointment body = move (SizeAll); the top/bottom edge of
    // a resizable (selected) appointment = resize (SizeNorthSouth); empty grid = default arrow.
    private void UpdateCursor(EventLayout? hit, Point p)
    {
        if (_onCursor is null)
        {
            return;
        }
        if (_isReadOnly || hit is not { } layout || layout.Appointment.IsLocked)
        {
            _onCursor(null); // locked / empty / read-only → plain arrow (no move/resize affordance)
            return;
        }

        var resizable = _isResizable?.Invoke(layout.Appointment) ?? true;
        var topY = layout.StartMinutes * PxPerMin;
        var botY = layout.EndMinutes * PxPerMin;
        var onEdge = resizable && (p.Y - topY < EdgeThreshold || botY - p.Y < EdgeThreshold);
        _onCursor(onEdge ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.SizeAll);
    }

    private void OnDragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        var item = ExternalDropHandler.GetDroppable(e.DataView);
        if (item is null)
        {
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;

        var p = e.GetPosition(_overlay);
        var col = ColumnIndexAt(p.X);
        var start = Snap(p.Y / PxPerMin);
        DrawGhost(col, start, start + DropDurationMinutes(item), null);
    }

    private void OnDrop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        var item = ExternalDropHandler.GetDroppable(e.DataView);
        RemoveGhost();
        if (item is null || _onExternalDrop is null)
        {
            return;
        }

        var p = e.GetPosition(_overlay);
        var col = _columns[ColumnIndexAt(p.X)];
        var startMin = Snap(p.Y / PxPerMin);
        var start = col.Day.AddMinutes(startMin);
        _onExternalDrop(new ExternalDropArgs
        {
            Item = item,
            Start = start,
            End = start.AddMinutes(DropDurationMinutes(item)),
            ResourceId = col.ResourceId,
        });
    }

    private void OnDragLeave(object sender, Microsoft.UI.Xaml.DragEventArgs e) => RemoveGhost();

    private double DropDurationMinutes(ISchedulerDroppable item)
    {
        var d = item.DefaultDuration.TotalMinutes;
        return d < MinDurationMinutes ? DefaultCreateMinutes : d;
    }

    private EventLayout? HitTest(double x, double y)
    {
        var colIndex = ColumnIndexAt(x);
        if (colIndex < 0 || colIndex >= _columnLayouts.Count)
        {
            return null;
        }

        var minutes = y / PxPerMin;
        var colW = ColumnWidth;
        var withinCol = x - colIndex * colW;

        // Prefer the block whose overlap sub-column the pointer is actually over.
        foreach (var el in _columnLayouts[colIndex])
        {
            if (minutes < el.StartMinutes || minutes > el.EndMinutes)
            {
                continue;
            }
            var subW = colW / Math.Max(1, el.ColumnCount);
            var sub = subW > 0 ? (int)(withinCol / subW) : 0;
            if (sub == el.ColumnIndex)
            {
                return el;
            }
        }

        // Fallback: any block covering this time.
        foreach (var el in _columnLayouts[colIndex])
        {
            if (minutes >= el.StartMinutes && minutes <= el.EndMinutes)
            {
                return el;
            }
        }
        return null;
    }

    // ---- Group (multi-selection) drag/resize ---------------------------------------------------

    private static bool Same(AppointmentModel a, AppointmentModel b) =>
        ReferenceEquals(a, b) || (a.Id is not null && Equals(a.Id, b.Id));

    // Collect every grabbed instance of the selection (a multi-resource event contributes one per lane)
    // and cache the group's column/time bounds so the shared delta can be clamped to keep all in-bounds.
    private void BuildGroup(IReadOnlyList<AppointmentModel> selection)
    {
        for (var c = 0; c < _columnLayouts.Count; c++)
        {
            foreach (var el in _columnLayouts[c])
            {
                // Locked items never move, even when caught in a multi-selection drag.
                if (!el.Appointment.IsLocked && selection.Any(s => Same(s, el.Appointment)))
                {
                    _group.Add(new GroupItem(c, el.StartMinutes, el.EndMinutes, el.Appointment));
                }
            }
        }
        if (_group.Count == 0)
        {
            return;
        }
        _isGroup = true;
        _grpMinCol = _group.Min(g => g.Col);
        _grpMaxCol = _group.Max(g => g.Col);
        _grpMinStart = _group.Min(g => g.StartMin);
        _grpMaxEnd = _group.Max(g => g.EndMin);
    }

    // The shared delta for the current pointer, clamped so the whole group stays in-bounds (relative
    // offsets intact). dCol/dMin drive Move; dTop/dBot drive the two resize edges.
    private (int dCol, double dMin, double dTop, double dBot) GroupDelta(Point p, int colIndex)
    {
        var lastCol = _columns.Count - 1;
        var dMin = Math.Clamp(Snap((p.Y - _grabOffsetY) / PxPerMin) - _origStartMin, -_grpMinStart, 24 * 60 - _grpMaxEnd);
        var dCol = Math.Clamp(colIndex - _grabColumnIndex, -_grpMinCol, lastCol - _grpMaxCol);

        var dTop = Math.Clamp(Snap(p.Y / PxPerMin) - _origStartMin, -_grpMinStart, _group.Min(g => g.EndMin - MinDurationMinutes - g.StartMin));
        var dBot = Math.Clamp(Snap(p.Y / PxPerMin) - _origEndMin, _group.Max(g => g.StartMin + MinDurationMinutes - g.EndMin), _group.Min(g => 24 * 60 - g.EndMin));

        return (dCol, dMin, dTop, dBot);
    }

    private void DrawGroupGhosts(Point p, int colIndex)
    {
        var (dCol, dMin, dTop, dBot) = GroupDelta(p, colIndex);
        var items = new List<(int Col, double Start, double End, AppointmentModel? Appt)>(_group.Count);
        foreach (var g in _group)
        {
            switch (_mode)
            {
                case Mode.Move: items.Add((g.Col + dCol, g.StartMin + dMin, g.EndMin + dMin, g.Appt)); break;
                case Mode.ResizeTop: items.Add((g.Col, g.StartMin + dTop, g.EndMin, g.Appt)); break;
                case Mode.ResizeBottom: items.Add((g.Col, g.StartMin, g.EndMin + dBot, g.Appt)); break;
            }
        }
        DrawGhosts(items);
    }

    private void EmitGroup(Point p, Mode mode, bool withShift)
    {
        var (dCol, dMin, dTop, dBot) = GroupDelta(p, ColumnIndexAt(p.X));
        switch (mode)
        {
            case Mode.Move when (dCol != 0 || dMin != 0) && _onMoveMany is not null:
            {
                var items = new List<AppointmentMoveItem>(_group.Count);
                foreach (var g in _group)
                {
                    var newCol = g.Col + dCol;
                    var start = _columns[newCol].Day.AddMinutes(g.StartMin + dMin);
                    items.Add(new AppointmentMoveItem
                    {
                        Appointment = g.Appt,
                        NewStart = start,
                        NewEnd = start.AddMinutes(g.EndMin - g.StartMin),
                        FromResourceId = _columns[g.Col].ResourceId,
                        ToResourceId = _columns[newCol].ResourceId,
                    });
                }
                _onMoveMany(new AppointmentsMoveArgs { Items = items, IsCopy = _copyDrag, WithShift = withShift });
                break;
            }
            case Mode.ResizeTop when dTop != 0 && _onResizeMany is not null:
                _onResizeMany(new AppointmentsResizeArgs { Items = BuildResizeItems(top: true, dTop), WithShift = withShift });
                break;
            case Mode.ResizeBottom when dBot != 0 && _onResizeMany is not null:
                _onResizeMany(new AppointmentsResizeArgs { Items = BuildResizeItems(top: false, dBot), WithShift = withShift });
                break;
        }
    }

    // Resize is per-appointment (a multi-resource event appears as several group items but is one event)
    // — dedupe by appointment, shifting the grabbed edge by the shared delta.
    private List<AppointmentResizeItem> BuildResizeItems(bool top, double delta)
    {
        var seen = new List<AppointmentModel>();
        var items = new List<AppointmentResizeItem>();
        foreach (var g in _group)
        {
            if (seen.Any(s => Same(s, g.Appt)))
            {
                continue;
            }
            seen.Add(g.Appt);
            var dayBase = g.Appt.Start.Date;
            items.Add(new AppointmentResizeItem
            {
                Appointment = g.Appt,
                NewStart = top ? dayBase.AddMinutes(g.StartMin + delta) : g.Appt.Start,
                NewEnd = top ? g.Appt.End : dayBase.AddMinutes(g.EndMin + delta),
            });
        }
        return items;
    }

    // ---- Ghost (drag preview) — a pool so a group drag can show one ghost per selected item ---------

    private void DrawGhost(int columnIndex, double startMin, double endMin, AppointmentModel? appt) =>
        DrawGhosts(new[] { (columnIndex, startMin, endMin, appt) });

    private void DrawGhosts(IReadOnlyList<(int Col, double Start, double End, AppointmentModel? Appt)> items)
    {
        while (_ghostPool.Count < items.Count)
        {
            var g = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1.5),
                BorderBrush = _palette.AccentForeground,
                IsHitTestVisible = false,
                Opacity = 0.8,
            };
            _ghostPool.Add(g);
            _overlay.Children.Add(g);
        }
        for (var i = items.Count; i < _ghostPool.Count; i++)
        {
            _ghostPool[i].Visibility = Visibility.Collapsed;
        }

        var colW = ColumnWidth;
        for (var i = 0; i < items.Count; i++)
        {
            var (col, start, end, appt) = items[i];
            var g = _ghostPool[i];
            g.Visibility = Visibility.Visible;
            g.Background = appt?.BackColor is Color c ? new SolidColorBrush(c) : (Brush)_palette.EventDefaultBack;
            g.Child = ResizeReadout(start, end) ?? CopyBadge();
            Canvas.SetLeft(g, col * colW + 2);
            Canvas.SetTop(g, start * PxPerMin);
            g.Width = Math.Max(0, colW - 4);
            g.Height = Math.Max(MinDurationMinutes * PxPerMin, (end - start) * PxPerMin);
        }
    }

    // During a resize, an enlarged time pill on the dragged edge of the ghost makes it obvious exactly
    // where the new start/end will land — in both coarse (grid-snap) and fine (Alt, per-minute) mode;
    // null for move / create. Reuses the event time-pill look for consistency.
    private FrameworkElement? ResizeReadout(double startMin, double endMin)
    {
        var atTop = _mode == Mode.ResizeTop;
        if (!atTop && _mode != Mode.ResizeBottom)
        {
            return null;
        }
        var edge = DateTime.MinValue.AddMinutes(atTop ? startMin : endMin);
        return new Border
        {
            Background = EventBlockFactory.TimePillBackBrush,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = atTop ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = edge.ToString("h:mm tt"),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = EventBlockFactory.TimePillForegroundBrush,
                TextWrapping = TextWrapping.NoWrap,
            },
        };
    }

    // When Ctrl is held during a move, a centred "+ Copy" pill on the ghost signals that the drop will
    // duplicate rather than relocate; null in every other mode. Reuses the event time-pill look.
    private FrameworkElement? CopyBadge()
    {
        if (_mode != Mode.Move || !_copyDrag)
        {
            return null;
        }
        return new Border
        {
            Background = EventBlockFactory.TimePillBackBrush,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "+ Copy",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = EventBlockFactory.TimePillForegroundBrush,
                TextWrapping = TextWrapping.NoWrap,
            },
        };
    }

    private void RemoveGhost()
    {
        foreach (var g in _ghostPool)
        {
            _overlay.Children.Remove(g);
        }
        _ghostPool.Clear();
    }

    // Snap to the configured grid (minor tick), or — while the fine-snap modifier (Alt) is held —
    // to an exact one-minute boundary for precise start/end adjustment.
    private double Snap(double minutes)
    {
        var step = _fineSnap ? InteractionModifiers.FineSnapMinutes : _increment.MinorTick.TotalMinutes;
        if (step <= 0)
        {
            return Math.Clamp(minutes, 0, 24 * 60);
        }
        return Math.Clamp(Math.Round(minutes / step) * step, 0, 24 * 60);
    }

    private int ColumnIndexAt(double x)
    {
        if (_columns.Count == 0)
        {
            return 0;
        }
        var colW = ColumnWidth;
        var index = colW > 0 ? (int)(x / colW) : 0;
        return Math.Clamp(index, 0, _columns.Count - 1);
    }

    private object? GrabbedResourceId() =>
        _grabColumnIndex >= 0 && _grabColumnIndex < _columns.Count ? _columns[_grabColumnIndex].ResourceId : null;

    // The pointer position (given in overlay coords) mapped to the XamlRoot content's coordinate space, so a
    // host can anchor a popup at the click regardless of scroll/layout. Null when the overlay isn't yet in a
    // live tree.
    private Point? ToRoot(Point overlayPoint)
    {
        var root = _overlay.XamlRoot?.Content;
        return root is null ? null : _overlay.TransformToVisual(root).TransformPoint(overlayPoint);
    }
}
