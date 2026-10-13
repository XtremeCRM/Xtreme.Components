using Windows.System;

namespace Xtreme.Components.Scheduler.Interaction;

/// <summary>
/// Shared modifier-key conventions for scheduler pointer interactions, kept in one place so the
/// binding is easy to change.
/// </summary>
internal static class InteractionModifiers
{
    /// <summary>
    /// Snap step (minutes) for fine adjustment: holding the fine-snap key lets the start/end land
    /// on an exact one-minute boundary instead of the configured grid tick.
    /// </summary>
    public const double FineSnapMinutes = 1;

    /// <summary>
    /// Holding this key while dragging an edge (or moving / creating) overrides the configured grid
    /// snap and drops to <see cref="FineSnapMinutes"/> granularity — the "fine adjust" convention
    /// used by Office apps, where Alt overrides snap-to-grid.
    /// </summary>
    private const VirtualKeyModifiers FineSnap = VirtualKeyModifiers.Menu; // Alt

    /// <summary>True when the live modifier state requests fine (per-minute) snapping.</summary>
    public static bool IsFineSnap(VirtualKeyModifiers modifiers) => modifiers.HasFlag(FineSnap);

    /// <summary>
    /// Holding this key while dragging an appointment body copies instead of moves — the original stays
    /// put and a duplicate is created at the drop position (the Windows Ctrl-drag convention).
    /// </summary>
    private const VirtualKeyModifiers Copy = VirtualKeyModifiers.Control;

    /// <summary>True when the live modifier state requests a copy drag.</summary>
    public static bool IsCopy(VirtualKeyModifiers modifiers) => modifiers.HasFlag(Copy);

    /// <summary>
    /// The control attaches no behavior to Shift; it is surfaced verbatim on the drag-commit event
    /// args so a host can key its own convention off it (e.g. skipping a confirmation prompt).
    /// </summary>
    public static bool IsShift(VirtualKeyModifiers modifiers) => modifiers.HasFlag(VirtualKeyModifiers.Shift);
}
