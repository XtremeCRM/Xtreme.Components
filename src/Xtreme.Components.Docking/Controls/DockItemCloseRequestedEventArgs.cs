namespace Xtreme.Components.Docking.Controls;

/// <summary>
/// Payload of <see cref="DockItem.CloseRequested"/>: the user asked to close a pane. Set
/// <see cref="System.ComponentModel.CancelEventArgs.Cancel"/> to keep it open, for example while an
/// unsaved-changes prompt runs; close it later by setting <see cref="DockItem.IsOpen"/> to false.
/// </summary>
public sealed class DockItemCloseRequestedEventArgs(DockItem item, bool fromFloatingWindow)
    : System.ComponentModel.CancelEventArgs
{
    /// <summary>The item the user asked to close.</summary>
    public DockItem Item { get; } = item;

    /// <summary>
    /// True when the request came from a floating window's own close button rather than a pane
    /// header, tab strip or header menu.
    /// </summary>
    public bool FromFloatingWindow { get; } = fromFloatingWindow;
}
