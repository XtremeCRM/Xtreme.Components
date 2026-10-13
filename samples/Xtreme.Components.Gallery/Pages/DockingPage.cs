using Xtreme.Components.Docking;
using Xtreme.Components.Docking.Controls;

namespace Xtreme.Components.Gallery.Pages;

/// <summary>
/// A <see cref="DockHost"/> built in code, the way a real host drives it: items added to a collection,
/// edges and sizes set as data. Two panes stack on the left, two more share a tab group below them.
/// The layout is saved through a small <see cref="IDockPersistence"/> over the app's local settings,
/// so moves, sizes, pins, closes and tab choices survive a restart; "Reset layout" forgets them.
/// Float affordances appear only where the platform supports OS windows (Windows desktop).
/// </summary>
public sealed partial class DockingPage : Grid
{
    private const string PersistenceKey = "Gallery.Docking";

    private readonly GalleryDockPersistence _persistence = new();
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
    private DockHost? _host;

    public DockingPage()
    {
        var reset = new Button { Content = "Reset layout", Margin = new Thickness(12, 0, 0, 0) };
        reset.Click += (_, _) =>
        {
            // Detach the old host from the store first, so its pending save (flushed when it unloads)
            // can't write the old layout back after the reset.
            if (_host is not null) _host.Persistence = null;
            _persistence.Forget(PersistenceKey);
            ShowHost(); // a fresh host starts from the authored layout
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        toolbar.Children.Add(_status);
        toolbar.Children.Add(reset);

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(toolbar);

        ShowHost();
    }

    private void ShowHost()
    {
        if (_host is not null) Children.Remove(_host);

        _host = BuildHost();
        SetRow(_host, 1);
        Children.Add(_host);
    }

    private DockHost BuildHost()
    {
        // The shipped Windows implementation: float affordances appear on Windows desktop and nowhere
        // else (it reports no float support on other runtimes).
        var platform = new Xtreme.Components.Docking.Platform.Win32DockPlatformServices
        {
            Log = line => System.Diagnostics.Debug.WriteLine(line),
        };

        var host = new DockHost
        {
            PersistenceKey = PersistenceKey,
            Persistence = _persistence,
            Document = Panel("Document", "#FF2D5B8A"),
            EdgePriority = [DockEdge.Right, DockEdge.Top, DockEdge.Bottom, DockEdge.Left],
            PlatformServices = platform,
        };

        host.Loaded += (_, _) =>
        {
            if (App.MainWindow is { } window)
            {
                platform.HostWindow = window;
                platform.CaptureShellWindowHandle(DispatcherQueue);
                platform.PrimePoolWhenIdle(DispatcherQueue);
            }
        };

        host.Items.Add(new DockItem { DockId = "explorer", Title = "Explorer", Edge = DockEdge.Left, AllowedDockEdges = DockEdge.Horizontal, DockLength = 220, Content = Panel("Explorer", "#FF3E7C4F") });
        host.Items.Add(new DockItem { DockId = "filters", Title = "Filters", Edge = DockEdge.Left, AllowedDockEdges = DockEdge.Horizontal, TabGroup = "left-tools", DockLength = 220, Content = Panel("Filters (tabbed with Map)", "#FF4F8A5F") });
        host.Items.Add(new DockItem { DockId = "map", Title = "Map", Edge = DockEdge.Left, AllowedDockEdges = DockEdge.Horizontal, TabGroup = "left-tools", DockLength = 220, AllowFloat = true, Content = Panel("Map (tabbed with Filters)", "#FF2E6E6E") });
        host.Items.Add(new DockItem { DockId = "properties", Title = "Properties", Edge = DockEdge.Right, DockLength = 280, AllowFloat = true, Content = Panel("Properties", "#FF8A5A2D") });
        host.Items.Add(new DockItem { DockId = "toolbar", Title = "Toolbar", Edge = DockEdge.Top, DockLength = 80, NarrowBehavior = DockNarrowBehavior.Keep, CanClose = false, Content = Panel("Toolbar (kept when narrow)", "#FF5A4F8A") });
        host.Items.Add(new DockItem { DockId = "output", Title = "Output", Edge = DockEdge.Bottom, DockLength = 110, NarrowBehavior = DockNarrowBehavior.Hide, Content = Panel("Output (hidden when narrow; the restore menu shows it)", "#FF6B6B6B") });

        void Refresh() => _status.Text =
            $"{(host.IsNarrow ? "Narrow: panes on rails" : "Wide: docked")}  ·  open panes {host.Items.Count(i => i.IsOpen)}/{host.Items.Count}";
        host.NarrowChanged += (_, _) => Refresh();
        foreach (var item in host.Items)
            item.RegisterPropertyChangedCallback(DockItem.IsOpenProperty, (_, _) => Refresh());
        host.Loaded += (_, _) => Refresh();

        return host;
    }

    private static Border Panel(string label, string hex) => new()
    {
        Background = new SolidColorBrush(ParseHex(hex)),
        Child = new TextBlock { Text = label, Margin = new Thickness(12), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), VerticalAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap },
    };

    private static Color ParseHex(string hex) => Color.FromArgb(
        Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16), Convert.ToByte(hex.Substring(7, 2), 16));

    /// <summary>
    /// The simplest useful <see cref="IDockPersistence"/>: the layout document in the app's local
    /// settings, falling back to memory where local settings are unavailable, so the page works
    /// either way.
    /// </summary>
    private sealed class GalleryDockPersistence : IDockPersistence
    {
        private readonly Dictionary<string, string> _memory = new();

        public Task<string?> LoadAsync(string key, CancellationToken ct = default)
        {
            try
            {
                if (Windows.Storage.ApplicationData.Current.LocalSettings.Values[SettingName(key)] is string stored)
                    return Task.FromResult<string?>(stored);
            }
            catch
            {
                // No local settings on this head; use the session copy.
            }

            return Task.FromResult(_memory.TryGetValue(key, out var document) ? document : null);
        }

        public Task SaveAsync(string key, string document, CancellationToken ct = default)
        {
            _memory[key] = document;
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values[SettingName(key)] = document;
            }
            catch
            {
                // Session-only on this head.
            }

            return Task.CompletedTask;
        }

        public void Forget(string key)
        {
            _memory.Remove(key);
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values.Remove(SettingName(key));
            }
            catch
            {
                // Nothing stored there.
            }
        }

        private static string SettingName(string key) => $"Dock.{key}";
    }
}
