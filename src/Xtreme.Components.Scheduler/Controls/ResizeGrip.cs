using Microsoft.UI.Input;

namespace Xtreme.Components.Scheduler.Controls;

/// <summary>
/// A corner resize grip: a hit-testable panel that shows the diagonal (NW–SE) resize cursor on hover and
/// reports drag deltas via <see cref="Resized"/>. Uses manipulation events (reliable on Uno WASM, unlike
/// raw PointerMoved during a held drag) rather than <c>Thumb</c>, which is sealed and can't carry the
/// cursor. Host its visual (the grip icon) as its content, with the icon set non-hit-testable so this
/// panel receives the pointer.
/// </summary>
public partial class ResizeGrip : Grid
{
    public ResizeGrip()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthwestSoutheast);
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit-testable across the bounds
        ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        ManipulationStarted += OnManipulationStarted;
        ManipulationDelta += OnManipulationDelta;
        ManipulationCompleted += OnManipulationCompleted;
    }

    /// <summary>Raised when a drag begins (host snapshots the starting size).</summary>
    public event EventHandler? ResizeStarted;

    /// <summary>Raised on each drag step with the CUMULATIVE (dx, dy) translation from the drag start, so
    /// the host applies <c>startSize + cumulative</c> — smoother than summing per-frame deltas against a
    /// possibly-stale ActualWidth.</summary>
    public event EventHandler<Point>? Resized;

    /// <summary>Raised when the drag ends (host persists the final size).</summary>
    public event EventHandler? ResizeCompleted;

    private void OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        => ResizeStarted?.Invoke(this, EventArgs.Empty);

    private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        => Resized?.Invoke(this, new Point(e.Cumulative.Translation.X, e.Cumulative.Translation.Y));

    private void OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        => ResizeCompleted?.Invoke(this, EventArgs.Empty);
}
