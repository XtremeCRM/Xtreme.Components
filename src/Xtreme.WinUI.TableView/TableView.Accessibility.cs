using Microsoft.UI.Xaml.Automation.Peers;
using System.Collections.Generic;
using WinUI.TableView.AutomationPeers;

namespace WinUI.TableView;

/// <summary>
/// Partial class for TableView that provides UI Automation support.
/// </summary>
public partial class TableView
{
    /// <summary>
    /// Gets the currently realized row containers.
    /// </summary>
    internal IReadOnlyList<TableViewRow> Rows => _rows;

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new TableViewAutomationPeer(this);
    }
}
