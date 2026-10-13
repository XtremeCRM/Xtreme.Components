namespace Xtreme.Components.Docking.Controls;

/// <summary>
/// Payload of <see cref="DockHost.FlyoutDismissRequested"/>: which item's flyout the user tapped
/// away, cancelable so a host can gate the close (unsaved-changes prompts) or substitute its own
/// outcome (fully closing the pane instead of the default slide-out to the rail).
/// </summary>
public sealed class FlyoutDismissEventArgs(DockItem item) : System.ComponentModel.CancelEventArgs
{
    /// <summary>The item whose flyout is being dismissed.</summary>
    public DockItem Item { get; } = item;
}
