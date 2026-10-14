using Microsoft.UI.Input;

namespace Xtreme.Components.Scheduler.Controls;

/// <summary>
/// A corner resize grip: a hit-testable panel that shows the diagonal (NW–SE) resize cursor on hover and
/// reports drag deltas via <see cref="Resized"/>. Host its visual (the grip icon) as its content, with the
/// icon set non-hit-testable so this panel receives the pointer. (<c>Thumb</c> is sealed and can't carry
/// the cursor.)
/// </summary>
/// <remarks>
/// The drag is measured in WINDOW coordinates from captured raw pointer events (XU-930). Manipulation
/// events were used before, but Uno measures their translation relative to the element itself, and this
/// grip moves as the panel it resizes grows or shrinks (and moves again if the host popup re-places
/// itself), so the reported translation fed back into the size and the panel jumped about.
/// </remarks>
internal sealed partial class ResizeGrip : Grid
{
    private uint? _pointerId;
    private Point _start;

    public ResizeGrip()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthwestSoutheast);
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit-testable across the bounds
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerEnded;
        PointerCanceled += OnPointerEnded;
        PointerCaptureLost += OnPointerEnded;
    }

    /// <summary>Raised when a drag begins (host snapshots the starting size).</summary>
    public event EventHandler? ResizeStarted;

    /// <summary>Raised on each drag step with the CUMULATIVE (dx, dy) pointer travel from the drag start,
    /// in window coordinates, so the host applies <c>startSize + cumulative</c> — smoother than summing
    /// per-frame deltas against a possibly-stale ActualWidth.</summary>
    public event EventHandler<Point>? Resized;

    /// <summary>Raised when the drag ends (host persists the final size).</summary>
    public event EventHandler? ResizeCompleted;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(null);
        if (_pointerId is not null || (point.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed))
        {
            return;
        }
        if (!CapturePointer(e.Pointer))
        {
            return;
        }
        _pointerId = e.Pointer.PointerId;
        _start = point.Position;
        e.Handled = true;
        ResizeStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId)
        {
            return;
        }
        var p = e.GetCurrentPoint(null).Position;
        e.Handled = true;
        Resized?.Invoke(this, new Point(p.X - _start.X, p.Y - _start.Y));
    }

    private void OnPointerEnded(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId)
        {
            return;
        }
        _pointerId = null; // first: releasing the capture below raises PointerCaptureLost back into here
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
        ResizeCompleted?.Invoke(this, EventArgs.Empty);
    }
}
