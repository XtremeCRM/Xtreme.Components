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

    /// <summary>
    /// <c>--selftest</c> checks for the drag pipeline, run against a throwaway host with no persistence
    /// (the saved layout is left alone): the guides appear, drops land, and every way a drag can end —
    /// cancel, a placement that throws, a rebuild under it, a float window that won't open — leaves no
    /// guides behind, the pane where it belongs, and the next drag working.
    /// </summary>
    internal async Task<List<string>> SelfTestAsync()
    {
        var problems = new List<string>();
        var host = BuildHost(persist: false);
        if (_host is not null) Children.Remove(_host);
        _host = host;
        SetRow(host, 1);
        Children.Add(host);
        await Task.Delay(600);

        DockItem Item(string id) => host.Items.First(i => i.DockId == id);
        void Check(bool ok, string what)
        {
            if (!ok) problems.Add(what);
        }

        void CheckEnded(string scenario, int overlayBaseline)
        {
            Check(!host.IsDragActive, $"{scenario}: drag still active");
            Check(host.DragVisualCount == 0, $"{scenario}: {host.DragVisualCount} drag visual(s) left");
            Check(host.OverlayChildCount == overlayBaseline, $"{scenario}: overlay has {host.OverlayChildCount} children, expected {overlayBaseline}");
        }

        async Task NextDragWorks(string scenario)
        {
            await Task.Delay(300);
            var started = host.SelfTestBeginDrag(Item("output"));
            Check(started, $"{scenario}: the next drag would not start");
            host.SelfTestCancel();
        }

        try
        {
            var baseline = host.OverlayChildCount;
            var properties = Item("properties");

            // 1. The guides appear: four edge markers and two document-compass buttons per edge.
            Check(host.SelfTestBeginDrag(properties), "guides: drag did not start");
            Check(host.EdgeMarkerCount == 4, $"guides: {host.EdgeMarkerCount} edge markers, expected 4");
            Check(host.DocumentGuideCount == 8, $"guides: {host.DocumentGuideCount} document-compass buttons, expected 8");
            host.SelfTestMoveTo(host.SelfTestItemCenter(Item("explorer")));
            Check(host.ClusterGuideCount == 5, $"guides: {host.ClusterGuideCount} slot-compass buttons over Explorer, expected 5");
            await SnapshotAsync(host, "slot-compass");
            if (host.SelfTestDocumentGuideCenter(DockEdge.Left, DockBand.Inner) is { } hover)
            {
                host.SelfTestMoveTo(hover);
                await SnapshotAsync(host, "document-compass");
            }
            host.SelfTestCancel();
            CheckEnded("cancel", baseline);
            Check(properties.Edge == DockEdge.Right, "cancel: the pane moved");

            // 2. A release over an edge marker docks there.
            await Task.Delay(300);
            Check(host.SelfTestBeginDrag(properties), "drag did not start");
            if (host.SelfTestMarkerCenter(DockEdge.Left) is { } left) host.SelfTestMoveTo(left);
            host.SelfTestRelease();
            CheckEnded("edge drop", baseline);
            Check(properties.Edge == DockEdge.Left && properties.Band == DockBand.Outer, $"edge drop: landed {properties.Edge}/{properties.Band}, expected Left/Outer");

            // 3. The document compass's inner button docks against the document.
            await Task.Delay(300);
            Check(host.SelfTestBeginDrag(properties), "drag did not start");
            if (host.SelfTestDocumentGuideCenter(DockEdge.Right, DockBand.Inner) is { } inner) host.SelfTestMoveTo(inner);
            host.SelfTestRelease();
            CheckEnded("inner drop", baseline);
            Check(properties.Edge == DockEdge.Right && properties.Band == DockBand.Inner, $"inner drop: landed {properties.Edge}/{properties.Band}, expected Right/Inner");

            // 4. A placement that throws: nothing stranded, no guides, the next drag works.
            await Task.Delay(300);
            var output = Item("output");
            host.FaultInjector = point => point == DockFault.ApplyDrop;
            Check(host.SelfTestBeginDrag(output), "throwing drop: drag did not start");
            if (host.SelfTestMarkerCenter(DockEdge.Top) is { } top) host.SelfTestMoveTo(top);
            host.SelfTestRelease();
            host.FaultInjector = null;
            CheckEnded("throwing drop", baseline);
            Check(host.Items.Contains(output) && output.IsOpen && !output.IsFloating, "throwing drop: the pane was stranded");
            await NextDragWorks("throwing drop");

            // 5. A rebuild under a drag ends it cleanly.
            await Task.Delay(300);
            Check(host.SelfTestBeginDrag(properties), "drag did not start");
            host.SelfTestRebuild();
            CheckEnded("rebuild mid-drag", host.OverlayChildCount);
            await NextDragWorks("rebuild mid-drag");

            // 6. A float whose window won't open re-docks the pane instead of losing it.
            if (host.PlatformServices.SupportsFloatingWindows)
            {
                await Task.Delay(300);
                host.FaultInjector = point => point == DockFault.FloatWindow;
                Check(host.SelfTestBeginDrag(properties), "failed float: drag did not start");
                host.SelfTestMoveTo(host.SelfTestDocumentCenter());
                host.SelfTestRelease();
                host.FaultInjector = null;
                await Task.Delay(300);
                CheckEnded("failed float", baseline);
                Check(!properties.IsFloating && properties.IsOpen && properties.Parent is not null, "failed float: the pane was stranded");
                await NextDragWorks("failed float");
            }
        }
        catch (Exception ex)
        {
            problems.Add($"threw: {ex}");
        }
        finally
        {
            host.FaultInjector = null;
            ShowHost(); // back to the persisted host
        }

        return problems;
    }

    /// <summary>
    /// With <c>XC_SELFTEST_SNAPSHOTS</c> set to a folder, saves a PNG of the host mid-drag there — a way
    /// to look at the guides from a headless run. Does nothing otherwise.
    /// </summary>
    private static async Task SnapshotAsync(FrameworkElement element, string name)
    {
        if (Environment.GetEnvironmentVariable("XC_SELFTEST_SNAPSHOTS") is not { Length: > 0 } folder) return;

        try
        {
            await Task.Delay(250); // let the guides render
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(element);
            var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await bitmap.GetPixelsAsync());

            using var image = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(
                bitmap.PixelWidth, bitmap.PixelHeight, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul));
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, image.GetPixels(), pixels.Length);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(System.IO.Path.Combine(folder, $"dock-{name}.png"), data.ToArray());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"selftest: docking snapshot '{name}' failed: {ex.Message}");
        }
    }

    private DockHost BuildHost(bool persist = true)
    {
        // The shipped Windows implementation: float affordances appear on Windows desktop and nowhere
        // else (it reports no float support on other runtimes).
        var platform = new Xtreme.Components.Docking.Platform.Win32DockPlatformServices
        {
            Log = line => System.Diagnostics.Debug.WriteLine(line),
        };

        var host = new DockHost
        {
            PersistenceKey = persist ? PersistenceKey : null,
            Persistence = persist ? _persistence : null,
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
