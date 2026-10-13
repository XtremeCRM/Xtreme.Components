using Xtreme.Components.Docking;
using Xtreme.Components.Docking.Controls;

namespace Xtreme.Components.Gallery.Pages;

/// <summary>
/// A <see cref="DockHost"/> built in code, the way a real host drives it: items added to a collection,
/// edges and sizes set as data. Two panes stack on the left, two more share a tab group below them.
/// With no <c>PlatformServices</c> set the host is docked-only, so the float affordances stay hidden;
/// an app that supplies <see cref="IDockPlatformServices"/> gets OS-window tear-out on desktop.
/// </summary>
public sealed partial class DockingPage : Grid
{
    public DockingPage()
    {
        var host = new DockHost
        {
            PersistenceKey = "Gallery.Docking",
            Document = Panel("Document", "#FF2D5B8A"),
            EdgePriority = [DockEdge.Right, DockEdge.Top, DockEdge.Bottom, DockEdge.Left],
        };

        host.Items.Add(new DockItem { DockId = "explorer", Title = "Explorer", Edge = DockEdge.Left, AllowedDockEdges = DockEdge.Horizontal, DockLength = 220, Content = Panel("Explorer", "#FF3E7C4F") });
        host.Items.Add(new DockItem { DockId = "filters", Title = "Filters", Edge = DockEdge.Left, AllowedDockEdges = DockEdge.Horizontal, TabGroup = "left-tools", DockLength = 220, Content = Panel("Filters (tabbed with Map)", "#FF4F8A5F") });
        host.Items.Add(new DockItem { DockId = "map", Title = "Map", Edge = DockEdge.Left, AllowedDockEdges = DockEdge.Horizontal, TabGroup = "left-tools", DockLength = 220, AllowFloat = true, Content = Panel("Map (tabbed with Filters)", "#FF2E6E6E") });
        host.Items.Add(new DockItem { DockId = "properties", Title = "Properties", Edge = DockEdge.Right, DockLength = 280, AllowFloat = true, Content = Panel("Properties", "#FF8A5A2D") });
        host.Items.Add(new DockItem { DockId = "toolbar", Title = "Toolbar", Edge = DockEdge.Top, DockLength = 80, NarrowBehavior = DockNarrowBehavior.Keep, CanClose = false, Content = Panel("Toolbar (kept when narrow)", "#FF5A4F8A") });
        host.Items.Add(new DockItem { DockId = "output", Title = "Output", Edge = DockEdge.Bottom, DockLength = 110, NarrowBehavior = DockNarrowBehavior.Hide, Content = Panel("Output (hidden when narrow)", "#FF6B6B6B") });

        var status = new TextBlock { Margin = new Thickness(0, 0, 0, 6), Opacity = 0.75 };
        void Refresh() => status.Text =
            $"{(host.IsNarrow ? "Narrow: overlay mode" : "Wide: docked")}  ·  open panes {host.Items.Count(i => i.IsOpen)}/{host.Items.Count}";
        host.NarrowChanged += (_, _) => Refresh();
        foreach (var item in host.Items)
            item.RegisterPropertyChangedCallback(DockItem.IsOpenProperty, (_, _) => Refresh());
        Loaded += (_, _) => Refresh();

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(host, 1);
        Children.Add(status);
        Children.Add(host);
    }

    private static Border Panel(string label, string hex) => new()
    {
        Background = new SolidColorBrush(ParseHex(hex)),
        Child = new TextBlock { Text = label, Margin = new Thickness(12), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), VerticalAlignment = VerticalAlignment.Top },
    };

    private static Color ParseHex(string hex) => Color.FromArgb(
        Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16), Convert.ToByte(hex.Substring(7, 2), 16));
}
