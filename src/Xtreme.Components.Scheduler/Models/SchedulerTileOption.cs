namespace Xtreme.Components.Scheduler.Models;

/// <summary>
/// A host-supplied on/off entry rendered in the scheduler's Settings flyout under "Tiles" — the generic hook
/// that lets an app expose which data tiles are available and let the user toggle each. The library knows
/// nothing about what a tile means; it renders the <see cref="Label"/> and reports toggles by <see cref="Key"/>.
/// </summary>
public sealed class SchedulerTileOption
{
    /// <summary>Stable identifier the host switches on when a toggle changes (e.g. "customer-balance").</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Display label for the toggle.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Whether the tile is currently enabled (drives the switch's initial state).</summary>
    public bool IsOn { get; init; }
}
