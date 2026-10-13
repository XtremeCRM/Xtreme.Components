namespace Xtreme.Components.Scheduler.Models;

/// <summary>
/// A host-supplied on/off entry in the toolbar's "Visible tiles" menu (see <c>SchedulerControl.SetTileOptions</c>):
/// the generic hook that lets an app expose which data tiles are available and let the user toggle each. The
/// library knows nothing about what a tile means; it renders the <see cref="Label"/> and reports toggles by
/// <see cref="Key"/> (<c>SchedulerControl.TileOptionToggled</c>). It does not filter tiles itself.
/// </summary>
public sealed class SchedulerTileOption
{
    /// <summary>Stable identifier the host switches on when a toggle changes (e.g. "customer-balance").</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Display label for the toggle.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Whether the tile is currently enabled (drives the menu entry's check mark).</summary>
    public bool IsOn { get; init; }
}
