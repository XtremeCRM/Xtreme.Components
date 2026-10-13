using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Xtreme.Components.Scheduler.Contracts;
using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Rendering;

namespace Xtreme.Components.Scheduler.Interaction;

/// <summary>
/// One interactive resource lane of the continuous Planner timeline: its absolute Y band
/// (<paramref name="Top"/>/<paramref name="Height"/>), overlap sub-row count, the resource it
/// maps to, and its precomputed event layout (minutes measured from the range origin).
/// </summary>
internal sealed record PlannerLaneBand(
    double Top,
    double Height,
    int RowCount,
    object? ResourceId,
    IReadOnlyList<EventLayout> Layout);

/// <summary>
/// Pointer interaction for the horizontal resource timeline (Planner) — the rotation of
/// <see cref="TimeGridInteraction"/>: <b>X = time</b> (minutes from the range origin) and
/// <b>Y = resource lane</b>. Drag a bar's body to retime it (horizontal) and reassign its
/// resource (vertical, across lanes); drag a bar's left/right edge to resize; drag across an
/// empty lane to create; accept an external <see cref="ISchedulerDroppable"/> drop. A click on a bar
/// reports a click (select / double-click edit, decided by the host control); a click on an empty lane
/// reports an empty-slot click; hovering reports the bar under the pointer (for the hover card).
/// Read-only keeps clicks, right-clicks and hover, and drops every editing gesture.
/// </summary>
/// <remarks>
/// Same overlay strategy as <see cref="TimeGridInteraction"/>: a single full-covering,
/// hit-testable <see cref="Canvas"/> is the pointer surface and ghost host (bubbling from bars is
/// unreliable under Uno WASM). Lanes have non-uniform heights, so the pointer Y is mapped to a
/// lane via precomputed <see cref="PlannerLaneBand"/> bands rather than a flat division. Results
/// are reported in <em>minutes from the range origin</em>; the host view converts them to
/// DateTimes (applying any per-resource timezone) and raises the public events.
/// </remarks>
internal sealed class PlannerTimelineInteraction
{
    // Click = pressed on a bar that can't be dragged (read-only or locked): only a click can follow.
    private enum Mode { None, Click, Move, ResizeStart, ResizeEnd, Create }

    private const double EdgeThreshold = 7;
    private const double DragThreshold = 4;
    private const double MinDurationMinutes = 15;
    private const double DefaultCreateMinutes = 60;

    private readonly Canvas _overlay;
    private readonly IReadOnlyList<PlannerLaneBand> _bands;
    private readonly double _pixelsPerMinute;
    private readonly double _rowHeight;
    private readonly double _totalMinutes;
    private readonly double _snapMinutes;
    private readonly SchedulerPalette _palette;
    private readonly bool _isReadOnly;

    // onMove trailing bools: (…, isCopy, withShift); onResize trailing bool: (…, withShift).
    private readonly Action<AppointmentModel, object?, object?, double, double, bool, bool> _onMove;
    private readonly Action<AppointmentModel, object?, double, double, bool> _onResize;
    private readonly Action<object?, double, double, Point?> _onCreate;
    private readonly Action<ISchedulerDroppable, object?, double, double>? _onExternalDrop;
    private readonly Action<AppointmentModel, Point?>? _onContext;
    private readonly Action<AppointmentModel, bool>? _onClick;
    private readonly Action<object?, double, Point?>? _onEmptyClick;
    private readonly Action<AppointmentModel?, Point>? _onHover;
    private AppointmentModel? _hoverAppt;
    private bool _pressCtrl;

    private Mode _mode = Mode.None;
    private AppointmentModel? _appt;
    private double _origStartMin;
    private double _origEndMin;
    private double _grabOffsetX;
    private double _createStartMin;
    private int _grabBandIndex;
    private Point _pressPoint;
    private bool _moved;
    private bool _fineSnap;
    private bool _copyDrag;
    private Border? _ghost;

    public PlannerTimelineInteraction(
        Canvas overlay,
        IReadOnlyList<PlannerLaneBand> bands,
        double pixelsPerMinute,
        double rowHeight,
        double totalMinutes,
        double snapMinutes,
        SchedulerPalette palette,
        bool isReadOnly,
        Action<AppointmentModel, object?, object?, double, double, bool, bool> onMove,
        Action<AppointmentModel, object?, double, double, bool> onResize,
        Action<object?, double, double, Point?> onCreate,
        Action<ISchedulerDroppable, object?, double, double>? onExternalDrop = null,
        Action<AppointmentModel, Point?>? onContext = null,
        Action<AppointmentModel, bool>? onClick = null,
        Action<object?, double, Point?>? onEmptyClick = null,
        Action<AppointmentModel?, Point>? onHover = null)
    {
        _overlay = overlay;
        _bands = bands;
        _pixelsPerMinute = pixelsPerMinute;
        _rowHeight = rowHeight;
        _totalMinutes = totalMinutes;
        _snapMinutes = snapMinutes;
        _palette = palette;
        _isReadOnly = isReadOnly;
        _onMove = onMove;
        _onResize = onResize;
        _onCreate = onCreate;
        _onExternalDrop = onExternalDrop;
        _onContext = onContext;
        _onClick = onClick;
        _onEmptyClick = onEmptyClick;
        _onHover = onHover;

        // Pointer handlers are always live: clicks, right-clicks and hover work in read-only too (the host
        // still selects and navigates). Drag / resize / create are gated on _isReadOnly inside them.
        _overlay.PointerPressed += OnPointerPressed;
        _overlay.PointerMoved += OnPointerMoved;
        _overlay.PointerReleased += OnPointerReleased;
        _overlay.PointerCanceled += OnPointerCanceled;
        _overlay.PointerExited += OnPointerExited;

        if (_isReadOnly)
        {
            return;
        }

        _overlay.AllowDrop = true;
        _overlay.DragOver += OnDragOver;
        _overlay.Drop += OnDrop;
        _overlay.DragLeave += OnDragLeave;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_overlay);
        var p = point.Position;
        HideHover();

        // Right button = a context-menu request on the bar under the pointer (if any). It never starts a
        // drag/resize/create, and works in read-only too (the host still navigates from it).
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

        _pressPoint = p;
        _moved = false;
        _pressCtrl = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
        _fineSnap = InteractionModifiers.IsFineSnap(e.KeyModifiers);
        _grabBandIndex = BandIndexAt(p.Y);

        var hit = HitTest(p.X, p.Y);
        if (hit is { } clicked && (_isReadOnly || clicked.Appointment.IsLocked))
        {
            // Read-only or locked bar: a click (select / edit request) but never a drag or resize.
            _appt = clicked.Appointment;
            _mode = Mode.Click;
        }
        else if (hit is { } layout)
        {
            _appt = layout.Appointment;
            _origStartMin = layout.StartMinutes;
            _origEndMin = layout.EndMinutes;
            _grabOffsetX = p.X - layout.StartMinutes * _pixelsPerMinute;

            var leftX = layout.StartMinutes * _pixelsPerMinute;
            var rightX = layout.EndMinutes * _pixelsPerMinute;
            _mode = p.X - leftX < EdgeThreshold ? Mode.ResizeStart
                : rightX - p.X < EdgeThreshold ? Mode.ResizeEnd
                : Mode.Move;
        }
        else if (_isReadOnly || _grabBandIndex < 0)
        {
            _appt = null;
            _mode = Mode.None;
        }
        else
        {
            _appt = null;
            _mode = Mode.Create;
            _createStartMin = Snap(p.X / _pixelsPerMinute);
        }

        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_overlay).Position;

        // Not pressing: report the bar under the pointer so the view can show its hover card.
        if (_mode == Mode.None)
        {
            var hovered = HitTest(p.X, p.Y)?.Appointment;
            if (!ReferenceEquals(hovered, _hoverAppt))
            {
                _hoverAppt = hovered;
                _onHover?.Invoke(hovered, p);
            }
            return;
        }
        if (_mode == Mode.Click)
        {
            return; // a non-draggable bar: nothing to preview
        }

        _fineSnap = InteractionModifiers.IsFineSnap(e.KeyModifiers);
        _copyDrag = InteractionModifiers.IsCopy(e.KeyModifiers);
        if (!_moved && Math.Abs(p.X - _pressPoint.X) < DragThreshold && Math.Abs(p.Y - _pressPoint.Y) < DragThreshold)
        {
            return;
        }
        _moved = true;

        var targetBand = BandIndexAt(p.Y);
        switch (_mode)
        {
            case Mode.Move:
            {
                var duration = _origEndMin - _origStartMin;
                var newStart = Math.Clamp(Snap((p.X - _grabOffsetX) / _pixelsPerMinute), 0, _totalMinutes - duration);
                DrawGhost(targetBand, newStart, newStart + duration, _appt);
                break;
            }
            case Mode.ResizeStart:
            {
                var newStart = Math.Clamp(Snap(p.X / _pixelsPerMinute), 0, _origEndMin - MinDurationMinutes);
                DrawGhost(_grabBandIndex, newStart, _origEndMin, _appt);
                break;
            }
            case Mode.ResizeEnd:
            {
                var newEnd = Math.Clamp(Snap(p.X / _pixelsPerMinute), _origStartMin + MinDurationMinutes, _totalMinutes);
                DrawGhost(_grabBandIndex, _origStartMin, newEnd, _appt);
                break;
            }
            case Mode.Create:
            {
                var current = Snap(p.X / _pixelsPerMinute);
                var start = Math.Min(_createStartMin, current);
                var end = Math.Max(_createStartMin, current);
                if (end - start < MinDurationMinutes)
                {
                    end = start + MinDurationMinutes;
                }
                DrawGhost(_grabBandIndex, start, end, null);
                break;
            }
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_mode == Mode.None)
        {
            return;
        }
        if (_mode == Mode.Click)
        {
            var clicked = _appt;
            _mode = Mode.None;
            _appt = null;
            if (clicked is not null)
            {
                _onClick?.Invoke(clicked, _pressCtrl);
            }
            return;
        }

        var p = e.GetCurrentPoint(_overlay).Position;
        _fineSnap = InteractionModifiers.IsFineSnap(e.KeyModifiers);
        _copyDrag = InteractionModifiers.IsCopy(e.KeyModifiers);
        // Shift at drop is surfaced to the host (it may skip its confirm dialog); the control itself
        // ascribes no meaning to it.
        var withShift = InteractionModifiers.IsShift(e.KeyModifiers);
        var targetBandIndex = BandIndexAt(p.Y);
        var mode = _mode;
        var appt = _appt;
        var moved = _moved;

        RemoveGhost();
        _mode = Mode.None;
        _appt = null;
        _moved = false;

        // A press+release on a bar without dragging = a click (select; a second click = edit request).
        if (!moved && appt is not null)
        {
            _onClick?.Invoke(appt, _pressCtrl);
            return;
        }

        switch (mode)
        {
            case Mode.Move when appt is not null && moved:
            {
                var duration = _origEndMin - _origStartMin;
                var newStart = Math.Clamp(Snap((p.X - _grabOffsetX) / _pixelsPerMinute), 0, _totalMinutes - duration);
                _onMove(appt, BandResource(_grabBandIndex), BandResource(targetBandIndex), newStart, newStart + duration, _copyDrag, withShift);
                break;
            }
            case Mode.ResizeStart when appt is not null && moved:
            {
                var newStart = Math.Clamp(Snap(p.X / _pixelsPerMinute), 0, _origEndMin - MinDurationMinutes);
                _onResize(appt, BandResource(_grabBandIndex), newStart, _origEndMin, withShift);
                break;
            }
            case Mode.ResizeEnd when appt is not null && moved:
            {
                var newEnd = Math.Clamp(Snap(p.X / _pixelsPerMinute), _origStartMin + MinDurationMinutes, _totalMinutes);
                _onResize(appt, BandResource(_grabBandIndex), _origStartMin, newEnd, withShift);
                break;
            }
            case Mode.Create:
            {
                // A click on an empty lane does not create: it reports the lane + time (the host control
                // turns a double-click, or a single click in quick-add mode, into a create). A drag creates
                // the dragged range directly.
                if (!moved)
                {
                    _onEmptyClick?.Invoke(BandResource(_grabBandIndex), _createStartMin, ToRoot(p));
                    break;
                }
                var current = Snap(p.X / _pixelsPerMinute);
                var startMin = Math.Min(_createStartMin, current);
                var endMin = Math.Max(_createStartMin, current);
                if (endMin - startMin < MinDurationMinutes)
                {
                    endMin = startMin + MinDurationMinutes;
                }
                _onCreate(BandResource(_grabBandIndex), startMin, endMin, ToRoot(p));
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

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => HideHover();

    private void HideHover()
    {
        if (_hoverAppt is not null)
        {
            _hoverAppt = null;
            _onHover?.Invoke(null, default);
        }
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
        var start = Snap(p.X / _pixelsPerMinute);
        DrawGhost(BandIndexAt(p.Y), start, start + DropDurationMinutes(item), null);
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
        var startMin = Snap(p.X / _pixelsPerMinute);
        _onExternalDrop(item, BandResource(BandIndexAt(p.Y)), startMin, startMin + DropDurationMinutes(item));
    }

    private void OnDragLeave(object sender, Microsoft.UI.Xaml.DragEventArgs e) => RemoveGhost();

    private double DropDurationMinutes(ISchedulerDroppable item)
    {
        var d = item.DefaultDuration.TotalMinutes;
        return d < MinDurationMinutes ? DefaultCreateMinutes : d;
    }

    private EventLayout? HitTest(double x, double y)
    {
        var bandIndex = BandIndexContaining(y);
        if (bandIndex < 0)
        {
            return null;
        }

        var band = _bands[bandIndex];
        var minutes = x / _pixelsPerMinute;
        var subRow = Math.Clamp((int)((y - band.Top) / _rowHeight), 0, Math.Max(0, band.RowCount - 1));

        // Prefer the bar whose overlap sub-row the pointer is actually over.
        foreach (var el in band.Layout)
        {
            if (minutes >= el.StartMinutes && minutes <= el.EndMinutes && el.ColumnIndex == subRow)
            {
                return el;
            }
        }

        // Fallback: any bar covering this time.
        foreach (var el in band.Layout)
        {
            if (minutes >= el.StartMinutes && minutes <= el.EndMinutes)
            {
                return el;
            }
        }
        return null;
    }

    private void DrawGhost(int bandIndex, double startMin, double endMin, AppointmentModel? appt)
    {
        if (_bands.Count == 0)
        {
            return;
        }
        var band = _bands[Math.Clamp(bandIndex, 0, _bands.Count - 1)];

        if (_ghost is null)
        {
            _ghost = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1.5),
                BorderBrush = _palette.AccentForeground,
                IsHitTestVisible = false,
                Opacity = 0.8,
            };
            _overlay.Children.Add(_ghost);
        }

        _ghost.Background = appt?.BackColor is Color c ? new SolidColorBrush(c) : (Brush)_palette.EventDefaultBack;
        _ghost.Child = CopyBadge();

        Canvas.SetLeft(_ghost, startMin * _pixelsPerMinute + 1);
        Canvas.SetTop(_ghost, band.Top + 1);
        _ghost.Width = Math.Max(MinDurationMinutes * _pixelsPerMinute, (endMin - startMin) * _pixelsPerMinute - 2);
        _ghost.Height = Math.Max(0, _rowHeight - 2);
    }

    private void RemoveGhost()
    {
        if (_ghost is not null)
        {
            _overlay.Children.Remove(_ghost);
            _ghost = null;
        }
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

    // Snap to the configured grid, or — while the fine-snap modifier (Alt) is held — to an exact
    // one-minute boundary for precise start/end adjustment.
    private double Snap(double minutes)
    {
        var step = _fineSnap ? InteractionModifiers.FineSnapMinutes : _snapMinutes;
        if (step <= 0)
        {
            return Math.Clamp(minutes, 0, _totalMinutes);
        }
        return Math.Clamp(Math.Round(minutes / step) * step, 0, _totalMinutes);
    }

    // The lane under y, or the nearest lane when y is outside every band: above the first, below the
    // last, or on a ByDay day-header row between bands. -1 only when there are no lanes.
    private int BandIndexAt(double y)
    {
        var inside = BandIndexContaining(y);
        if (inside >= 0 || _bands.Count == 0)
        {
            return inside;
        }
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < _bands.Count; i++)
        {
            var b = _bands[i];
            var distance = y < b.Top ? b.Top - y : y - (b.Top + b.Height);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    // The lane whose band contains y, or -1.
    private int BandIndexContaining(double y)
    {
        for (var i = 0; i < _bands.Count; i++)
        {
            if (y >= _bands[i].Top && y < _bands[i].Top + _bands[i].Height)
            {
                return i;
            }
        }
        return -1;
    }

    private object? BandResource(int index) =>
        index >= 0 && index < _bands.Count ? _bands[index].ResourceId : null;

    // The pointer position (in overlay coords) mapped to the XamlRoot content's space, so a host can anchor
    // a context menu at the click regardless of scroll/layout. Null when the overlay isn't yet in a live tree.
    private Point? ToRoot(Point overlayPoint)
    {
        var root = _overlay.XamlRoot?.Content;
        return root is null ? null : _overlay.TransformToVisual(root).TransformPoint(overlayPoint);
    }
}
