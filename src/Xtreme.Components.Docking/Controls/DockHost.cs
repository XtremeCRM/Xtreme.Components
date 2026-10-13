using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Xtreme.Components.Core;

namespace Xtreme.Components.Docking.Controls;

/// <summary>
/// A dock layout container: one fixed centre document with <see cref="DockItem"/>s docked around it.
/// </summary>
/// <remarks>
/// <para><b>Why a centre document.</b> The arrangement this replaces was built by NESTING two-pane
/// splitters, which pushed the real content deeper with every pane added — in the scheduler the grid
/// ended up three levels down as the innermost pane, so its position in the tree was an accident of
/// which panes happened to exist. Here the document's slot is fixed and panes attach to edges
/// independently, so adding a fourth pane rearranges nothing.</para>
/// <para><b>Layout.</b> Top and Bottom span the full width; Left and Right sit beside the document
/// between them. Each edge region stacks its items along the cross axis with draggable splitters
/// between them — the "adjacent" arrangement. Tabbed groups are the other half of that model and are
/// not built yet.</para>
/// <para><b>Responsive.</b> Below a width threshold the host drops to a narrow zone and each item
/// applies its <see cref="DockItem.NarrowBehavior"/> — by default overlaying the document rather than
/// taking space from it. This works everywhere, including WASM and mobile; only floating is
/// desktop-only.</para>
/// </remarks>
[ContentProperty(Name = nameof(Items))]
public partial class DockHost : ContentControl
{
    // Hysteresis, ported deliberately from the control this replaces rather than reinvented as a single
    // breakpoint. With one threshold, a host sitting exactly on the boundary flips zone on every stray
    // sub-pixel resize; the gap between these two makes the transition sticky in both directions.
    private const double BreakDownAt = 580d;
    private const double BreakUpAt = 625d;

    private enum WidthZone { Narrow, Wide }

    private readonly Grid _root = new();
    private readonly Grid _middle = new();
    private readonly ContentPresenter _documentHost = new();
    private readonly Grid _overlayLayer = new();
    private readonly Grid _transientLayer = new();

    private WidthZone _zone = WidthZone.Wide;
    private bool _zoneInitialized;
    private bool _rebuilding;

    public DockHost()
    {
        Items = new ObservableCollection<DockItem>();
        ((ObservableCollection<DockItem>)Items).CollectionChanged += OnItemsChanged;

        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        _documentHost.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _documentHost.VerticalContentAlignment = VerticalAlignment.Stretch;

        // The overlay layer sits above the docked grid and is only populated in the narrow zone. It is
        // always present but hit-invisible when empty, so entering narrow costs no tree surgery.
        _overlayLayer.IsHitTestVisible = false;

        // The transient layer rides above everything and survives rebuilds — it hosts the short-lived
        // exit ghosts (an unpinned pane sliding off to its rail) that must keep playing across the very
        // rebuild that removed the real pane. Never hit-testable, clipped so a ghost sliding past the
        // host's edge doesn't draw over whatever the app placed beside the dock.
        _transientLayer.IsHitTestVisible = false;
        _transientLayer.SizeChanged += (_, e) => _transientLayer.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
        };

        _root.Children.Add(_middle);
        _root.Children.Add(_overlayLayer);
        _root.Children.Add(_transientLayer);

        // ContentProperty is Items, so XAML children land there; Content is ours to drive.
        Content = _root;

        SizeChanged += OnHostSizeChanged;
        Loaded += OnHostLoaded;
        Unloaded += OnHostUnloaded;

        // The chrome bakes per-theme colours at build time (ComponentTheme), so a live light/dark switch
        // just rebuilds — cheap, and it re-resolves every themed surface in one sweep.
        ActualThemeChanged += (_, _) => Rebuild();
    }

    // ---- Public surface -------------------------------------------------------------------------

    /// <summary>
    /// The centre content — the thing the panes are arranged around. Its slot never moves.
    /// </summary>
    public object? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(object), typeof(DockHost),
        new PropertyMetadata(null, (d, e) => ((DockHost)d)._documentHost.Content = e.NewValue));

    /// <summary>The docked panes. This is the XAML content property, so children go here directly.</summary>
    public IList<DockItem> Items { get; }

    /// <summary>
    /// Identity for layout save/restore via <see cref="Persistence"/>. Null disables persistence.
    /// </summary>
    public string? PersistenceKey
    {
        get => (string?)GetValue(PersistenceKeyProperty);
        set => SetValue(PersistenceKeyProperty, value);
    }

    public static readonly DependencyProperty PersistenceKeyProperty = DependencyProperty.Register(
        nameof(PersistenceKey), typeof(string), typeof(DockHost), new PropertyMetadata(null));

    /// <summary>Host-supplied layout store. See <see cref="IDockPersistence"/> for why this is injected.</summary>
    public IDockPersistence? Persistence { get; set; }

    /// <summary>
    /// Host-supplied platform capabilities. Defaults to docked-only, so a head that never opts in can
    /// never attempt a float on a platform that cannot survive one.
    /// </summary>
    public IDockPlatformServices PlatformServices { get; set; } = DockedOnlyPlatformServices.Instance;

    /// <summary>True when the host is currently in its narrow (responsive) zone.</summary>
    public bool IsNarrow => _zone == WidthZone.Narrow;

    /// <summary>
    /// Whether the document-corner restore menu appears while panes are closed. Default true — a
    /// closed pane must stay recoverable, because <see cref="DockItem.IsOpen"/> persists. Turn off
    /// ONLY when the host app drives open/close programmatically and owns the way back itself
    /// (the DockBlade <c>ShowPaneToggle=false</c> contract).
    /// </summary>
    public bool ShowClosedPaneRestore
    {
        get => (bool)GetValue(ShowClosedPaneRestoreProperty);
        set => SetValue(ShowClosedPaneRestoreProperty, value);
    }

    public static readonly DependencyProperty ShowClosedPaneRestoreProperty = DependencyProperty.Register(
        nameof(ShowClosedPaneRestore), typeof(bool), typeof(DockHost),
        new PropertyMetadata(true, (d, _) => ((DockHost)d).Rebuild()));

    /// <summary>
    /// Whether a restored layout document may reopen/close items (the doc's IsOpen field). Default
    /// true. Hosts whose panes are opened purely by CODE — an event editor, a map toggled from a
    /// toolbar — set false so yesterday's open editor can't pop open uninvited at startup, while
    /// edges, sizes and pin states still restore.
    /// </summary>
    public bool RestoreItemOpenState { get; set; } = true;

    /// <summary>
    /// Opens <paramref name="item"/>'s auto-hide flyout as if its rail tab were tapped — the
    /// programmatic "show the pane over the document" entry point. No-ops unless the item is
    /// currently presented as auto-hidden (unpinned, or collapsed by the narrow zone).
    /// </summary>
    public void OpenFlyout(DockItem item)
    {
        if (!Items.Contains(item) || !item.IsOpen || item.IsFloating) return;

        var autoHidden = !item.IsPinned
            || (_zone == WidthZone.Narrow && item.NarrowBehavior == DockNarrowBehavior.AutoHide);
        if (autoHidden) OpenAutoHideFlyout(item);
    }

    /// <summary>Raised when the host crosses the narrow/wide threshold.</summary>
    public event EventHandler? NarrowChanged;

    /// <summary>Raised when an auto-hide flyout opens (rail tap or <see cref="OpenFlyout"/>), with the
    /// item it presents. List-style hosts use this to reset selection so re-picking the same row still
    /// registers as a change — the dock analogue of the old back-to-master rail tap.</summary>
    public event EventHandler<DockItem>? FlyoutOpened;

    /// <summary>
    /// Raised when the user taps the scrim to dismiss an auto-hide flyout, BEFORE it closes. Set
    /// <see cref="System.ComponentModel.CancelEventArgs.Cancel"/> to keep the flyout open — the hook
    /// for hosts that gate the close (an editor pane's unsaved-changes prompt) or that want a
    /// different outcome than the default slide-out (fully closing the pane). The dismissed item is
    /// <see cref="FlyoutDismissEventArgs.Item"/>.
    /// </summary>
    public event EventHandler<FlyoutDismissEventArgs>? FlyoutDismissRequested;

    // ---- Item wiring ----------------------------------------------------------------------------

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DockItem old in e.OldItems)
                old.LayoutAffectingPropertyChanged -= OnItemLayoutChanged;

        if (e.NewItems is not null)
            foreach (DockItem added in e.NewItems)
                added.LayoutAffectingPropertyChanged += OnItemLayoutChanged;

        Rebuild();
    }

    private void OnItemLayoutChanged(object? sender, EventArgs e) => Rebuild();

    private async void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        // ActualWidth is frequently 0 until the first real arrange, and deciding a zone from 0 would
        // wrongly latch Narrow. Wait for a genuine measurement.
        if (ActualWidth > 0) ApplyResponsiveLayout(ActualWidth);
        Rebuild();

        // After the first build, so a restore that changes nothing costs one no-op rebuild rather than
        // delaying first paint behind an await on the host's storage.
        await RestoreLayoutAsync();

    }

    private void OnHostUnloaded(object sender, RoutedEventArgs e)
    {
        // Flush any close that was deferred for frame timing: the host is leaving the tree, so the
        // stall no longer matters and an unclosed window must not leak past this point.
        foreach (var (timer, window) in _deferredCloses.ToList())
        {
            timer.Stop();
            CloseWindowGuarded(window);
        }
        _deferredCloses.Clear();

        // Stop watching still-floating windows for redock drags — their guides can only render in a
        // host that is leaving the tree. The windows themselves stay up (existing behaviour).
        foreach (var entry in _floatingWindows.Values)
            entry.DragTracker?.Dispose();
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyResponsiveLayout(e.NewSize.Width);

    // ---- Responsive zone ------------------------------------------------------------------------

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;

        var wasInitialized = _zoneInitialized;

        WidthZone next;
        if (!wasInitialized)
        {
            // First real measurement decides on a single midpoint. Using the hysteresis pair here
            // instead would leave a host that starts up INSIDE the 580–625 band matching neither
            // condition, so no zone would ever be applied and the layout would never resolve.
            next = width < (BreakDownAt + BreakUpAt) / 2.0 ? WidthZone.Narrow : WidthZone.Wide;
        }
        else
        {
            next = _zone;
            if (width < BreakDownAt) next = WidthZone.Narrow;
            else if (width > BreakUpAt) next = WidthZone.Wide;
        }

        var changed = next != _zone;
        _zone = next;
        _zoneInitialized = true;

        if (changed || !wasInitialized)
        {
            Rebuild();
            if (changed) NarrowChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- Layout construction --------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the grid from the current items. Cheap enough to run wholesale on any layout-affecting
    /// change, and far easier to reason about than incremental mutation of row/column definitions.
    /// </summary>
    private void Rebuild()
    {
        // Re-entrancy guard: assigning row/column definitions below can raise SizeChanged, and
        // re-entering a rebuild from inside one is how a layout cycle starts.
        if (_rebuilding) return;
        _rebuilding = true;
        try
        {
            RebuildCore();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void RebuildCore()
    {
        // A rebuild tears down the very elements an in-flight IN-HOST drag rides — its handle, the
        // slot chrome, its overlay visuals. Cancel the drag rather than let it outlive its hardware:
        // a stuck _dragItem otherwise blocks every later gesture (observed live: a drag whose element
        // died mid-gesture left the re-dock guides unable to arm until an unrelated commit cleared
        // it). Re-dock drags are exempt — they hold no in-host element, and their guides re-raise on
        // the next tracker callback.
        if (_dragItem is not null && !_redockActive)
        {
            ClearDragState();
        }

        _middle.Children.Clear();
        _middle.RowDefinitions.Clear();
        _middle.ColumnDefinitions.Clear();
        _overlayLayer.Children.Clear();
        _overlayLayer.IsHitTestVisible = false;
        _slotRegistry.Clear();
        _autoHideFlyoutItem = null; // any rebuild closes an open auto-hide flyout
        _flyoutClipHost = null;     // and orphans its exit animation, whose Completed guard notices
        _flyoutSlide = null;

        var docked = new List<DockItem>();
        var overlaid = new List<DockItem>();
        var autoHidden = new List<DockItem>();

        foreach (var item in Items)
        {
            if (!item.IsOpen || item.IsFloating) continue;

            // Unpinned (auto-hide) panes never occupy the docked grid: they collapse to a rail tab at
            // their edge, in every width zone — an auto-hidden pane is already out of the way, so the
            // narrow-zone rules don't apply to it.
            if (!item.IsPinned)
            {
                autoHidden.Add(item);
                continue;
            }

            if (_zone == WidthZone.Narrow)
            {
                switch (item.NarrowBehavior)
                {
                    case DockNarrowBehavior.Hide: continue;
                    case DockNarrowBehavior.Overlay: overlaid.Add(item); continue;
                    // Zone-driven auto-unpin (the default): the pane collapses to its rail tab
                    // WITHOUT touching IsPinned, so widening restores it docked as it was.
                    case DockNarrowBehavior.AutoHide: autoHidden.Add(item); continue;
                    case DockNarrowBehavior.Keep: break;
                }
            }

            docked.Add(item);
        }

        BuildDockedGrid(docked, autoHidden);
        BuildOverlay(overlaid);

        // Floating is plausibly imminent whenever a floatable item is hosted — give a pooling head
        // its chance to absorb the window-construction freeze during idle instead of under the
        // user's eventual tear-out (XU-450). Repeat-safe by contract.
        if (PlatformServices.SupportsFloatingWindows && Items.Any(i => i.AllowFloat))
            PlatformServices.WarmFloatSupport(DispatcherQueue);
    }

    /// <summary>
    /// The order edges claim the host's full extent: the FIRST edge here is laid out outermost and
    /// spans the whole host on its axis; each later edge fits inside the earlier ones. The default
    /// reproduces the common IDE arrangement (toolbars/output span full width). To give a Right pane
    /// full height with the Top yielding to it — Russ's Event Editor case — put Right first.
    /// </summary>
    /// <remarks>
    /// This used to be HARDCODED (Top/Bottom rows outside the Left/Right band); which edge won the
    /// corners was an accident of the grid, not a policy. Now it is the host author's call.
    /// </remarks>
    public IReadOnlyList<DockEdge> EdgePriority
    {
        get => _edgePriority;
        set
        {
            _edgePriority = value;
            Rebuild();
        }
    }

    private IReadOnlyList<DockEdge> _edgePriority =
        new[] { DockEdge.Top, DockEdge.Bottom, DockEdge.Left, DockEdge.Right };

    private void BuildDockedGrid(List<DockItem> docked, List<DockItem> autoHidden)
    {
        // The document is re-wrapped in fresh layer grids every rebuild — orphan it from the previous
        // (detached) wrapper first, same reparenting rule as the items.
        if (_documentHost.Parent is Panel oldParent) oldParent.Children.Remove(_documentHost);

        // Build inside-out in TWO band passes (the doubled compass buttons): INNER-band layers wrap
        // the document first, so they size to the document's extent; OUTER-band layers wrap after,
        // so they run against the host border. Within each band, EdgePriority still decides which
        // edge claims the corners (lowest priority wrapped first, highest ends up outermost). Rails
        // (auto-hide tabs) always belong to the true border, so only the outer pass hosts them.
        FrameworkElement content = WrapDocumentWithClosedPaneRestore();
        var noRails = new List<DockItem>();
        foreach (var edge in _edgePriority.Reverse())
        {
            content = WrapWithEdgeLayer(
                content,
                edge,
                docked.Where(i => i.Edge == edge && i.Band == DockBand.Inner).ToList(),
                noRails);
        }

        foreach (var edge in _edgePriority.Reverse())
        {
            content = WrapWithEdgeLayer(
                content,
                edge,
                docked.Where(i => i.Edge == edge && i.Band == DockBand.Outer).ToList(),
                autoHidden);
        }

        _middle.Children.Add(content);
    }

    /// <summary>
    /// The way back from ✕: while any pane is closed, a small menu button rides the document's
    /// top-right corner listing the closed panes — clicking one reopens it where it was. Without
    /// this, a closed pane is gone for good: <see cref="DockItem.IsOpen"/> is persisted, so not even
    /// an app restart brings it back.
    /// </summary>
    /// <remarks>
    /// On the DOCUMENT cell rather than the host's corner, deliberately: the host's own corners are
    /// prime dock territory (a Right pane's header buttons live exactly there), while the document
    /// cell always exists and is never covered by dock chrome. VS hangs this list off View ▸ Other
    /// Windows; the dock host has no menu bar, so the affordance appears in-situ — and only while
    /// something is actually closed.
    /// </remarks>
    private FrameworkElement WrapDocumentWithClosedPaneRestore()
    {
        // Hosts that drive open/close from code own the way back themselves — no pill.
        if (!ShowClosedPaneRestore) return _documentHost;

        var closedCount = Items.Count(i => !i.IsOpen);
        if (closedCount == 0) return _documentHost;

        var wrap = new Grid();
        wrap.Children.Add(_documentHost);

        // GlobalNavigationButton (hamburger) from the theme symbol font — "there is a menu here".
        var button = DockItemHeader.BuildIconButton("\uE700", closedCount == 1
            ? "1 closed pane — reopen"
            : $"{closedCount} closed panes — reopen");

        var pill = new Border
        {
            Background = ComponentTheme.Surface(ActualTheme),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x90, 0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Child = button,
        };

        button.Click += (_, _) =>
        {
            var flyout = new MenuFlyout();
            foreach (var item in Items.Where(i => !i.IsOpen).ToList())
            {
                var entry = new MenuFlyoutItem { Text = item.Title ?? item.DockId ?? "(untitled pane)" };
                entry.Click += (_, _) =>
                {
                    item.IsOpen = true; // the setter's rebuild re-docks it and re-evaluates this pill
                    PersistLayoutAsync();
                };
                flyout.Items.Add(entry);
            }
            flyout.ShowAt(pill);
        };

        wrap.Children.Add(pill);
        return wrap;
    }

    /// <summary>
    /// Wraps <paramref name="inner"/> with one edge's layer: [rail | region | splitter | inner] in the
    /// edge-appropriate order and axis. An edge with nothing to show adds no layer at all.
    /// </summary>
    private FrameworkElement WrapWithEdgeLayer(
        FrameworkElement inner, DockEdge edge, List<DockItem> items, List<DockItem> autoHidden)
    {
        var hasRail = autoHidden.Any(i => i.Edge == edge);
        if (items.Count == 0 && !hasRail) return inner;

        var layer = new Grid();
        var nearSide = !edge.IsFarSide(); // Left/Top: rail+region before inner; Right/Bottom: after

        // Four tracks: rail, region, splitter, inner — inner is the star.
        var trackSizes = new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) };
        var order = nearSide ? new[] { 0, 1, 2, 3 } : new[] { 3, 2, 1, 0 };
        // order[n] maps semantic track n (0 rail, 1 region, 2 splitter, 3 inner) to its grid index.
        var index = new int[4];
        for (var gridIndex = 0; gridIndex < 4; gridIndex++)
        {
            var semantic = order[gridIndex];
            index[semantic] = gridIndex;
            if (edge.IsHorizontal())
                layer.ColumnDefinitions.Add(new ColumnDefinition { Width = trackSizes[semantic] });
            else
                layer.RowDefinitions.Add(new RowDefinition { Height = trackSizes[semantic] });
        }

        bool seam;
        if (edge.IsHorizontal())
        {
            seam = AddRegion(layer, items, edge, column: index[1], splitterColumn: index[2]);
            AddRail(layer, autoHidden, edge, column: index[0]);
        }
        else
        {
            seam = AddRegion(layer, items, edge, row: index[1], splitterRow: index[2]);
            AddRail(layer, autoHidden, edge, row: index[0]);
        }

        // The region's handle protrudes over whatever lies inside this layer, so inset it on the seam
        // side: no text or control ever sits under the handle (XU-631).
        if (seam) inner = PadAgainstSeam(inner, DockSplitter.ClearanceOppositeOf(edge));

        if (edge.IsHorizontal()) Grid.SetColumn(inner, index[3]);
        else Grid.SetRow(inner, index[3]);

        layer.Children.Add(inner);
        return layer;
    }

    /// <returns>True when a resize seam (and so a protruding handle) was added beside the region.</returns>
    private bool AddRegion(
        Grid target, List<DockItem> items, DockEdge edge,
        int column = -1, int splitterColumn = -1, int row = -1, int splitterRow = -1)
    {
        if (items.Count == 0) return false;

        var horizontal = edge.IsHorizontal();
        var region = BuildRegionStack(items);

        // The region's extent along the edge axis is the largest length its items ask for. A region has
        // ONE width (or height); the items stacked inside it share it and divide the cross axis.
        var extent = Math.Clamp(
            items.Max(i => i.DockLength),
            items.Max(i => i.MinLength),
            items.Max(i => i.MaxLength));

        if (horizontal) region.Width = extent;
        else region.Height = extent;

        Place(region, column, row);
        target.Children.Add(region);

        if (items.Any(i => i.CanResize))
        {
            // The region seam belongs to this edge's pane: the splitter draws its handle flush
            // against the pane, protruding over the document side (the MasterDetail look).
            var splitter = new DockSplitter(horizontal ? Orientation.Vertical : Orientation.Horizontal, ActualTheme, edge);
            var farSide = edge.IsFarSide();
            var min = items.Max(i => i.MinLength);
            var max = items.Max(i => i.MaxLength);

            // Snapshot at drag start and apply start + cumulative. Dragged reports cumulative
            // translation, so re-reading the live extent each step would double-count the movement.
            var startExtent = extent;
            splitter.DragStarted += (_, _) => startExtent = horizontal ? region.ActualWidth : region.ActualHeight;

            splitter.Dragged += cumulative =>
            {
                // A far-side pane grows when the splitter moves TOWARD the document, i.e. the opposite
                // sign from a near-side one. Getting this wrong makes the pane fight the cursor.
                var applied = farSide ? -cumulative : cumulative;
                var next = Math.Clamp(startExtent + applied, min, max);

                if (horizontal) region.Width = next;
                else region.Height = next;
            };

            // Write back to the model only once the drag ends. Setting DockLength mid-drag would raise
            // LayoutAffectingPropertyChanged on every frame, rebuilding the whole grid underneath the
            // splitter the user is still holding.
            splitter.DragCompleted += (_, _) =>
            {
                var final = horizontal ? region.ActualWidth : region.ActualHeight;
                foreach (var i in items) i.SetDockLengthSilently(final);
                PersistLayoutAsync();
            };

            Place(splitter, splitterColumn, splitterRow);
            target.Children.Add(splitter);
            return true;
        }

        return false;
    }

    private static void Place(FrameworkElement element, int column, int row)
    {
        if (column >= 0) Grid.SetColumn(element, column);
        if (row >= 0) Grid.SetRow(element, row);
    }

    /// <summary>
    /// Insets content that borders a seam so the splitter's handle never covers it (XU-631). A
    /// transparent padded Panel rather than a Margin or a Border: the content may be a persistent
    /// DockItem (a Margin would travel with it into float windows), or the document host, which every
    /// rebuild re-parents by removing it from its Panel parent.
    /// </summary>
    private static FrameworkElement PadAgainstSeam(FrameworkElement content, Thickness clearance)
    {
        if (clearance.Left == 0 && clearance.Top == 0 && clearance.Right == 0 && clearance.Bottom == 0)
            return content;

        var pad = new Grid { Padding = clearance };
        pad.Children.Add(content);
        return pad;
    }

    /// <summary>
    /// Stacks an edge's SLOTS vertically with splitters between them — on EVERY edge. A slot is
    /// either a standalone item or a whole tab group: this is the "grouped or arranged adjacent"
    /// model, where <see cref="DockItem.TabGroup"/> merges items into one slot and everything else
    /// stacks.
    /// </summary>
    /// <remarks>
    /// Top/Bottom used to stack side-by-side (columns), which made an edge drop onto an occupied
    /// Top edge land BESIDE the existing pane — Russ: "I drop to land above, but it lands to the
    /// left". DevExpress stacks edge layers from the border inward, i.e. top/bottom panes stack as
    /// ROWS; rows everywhere also makes the slot compass's insert arrows read the same (↑ above,
    /// ↓ below) on every edge. Side-by-side within an edge returns with the recursive-split model.
    /// </remarks>
    private Grid BuildRegionStack(List<DockItem> items)
    {
        // Level 1: partition into SLOTS (tab groups / standalone), preserving order of first
        // appearance. Grouping is by name across the whole edge, so authoring order inside a group
        // doesn't matter.
        var slots = new List<List<DockItem>>();
        var groupSlot = new Dictionary<string, int>();
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.TabGroup))
            {
                slots.Add([item]);
            }
            else if (groupSlot.TryGetValue(item.TabGroup!, out var at))
            {
                slots[at].Add(item);
            }
            else
            {
                groupSlot[item.TabGroup!] = slots.Count;
                slots.Add([item]);
            }
        }

        // Level 2: partition slots into LAYERS. Slots sharing a LayerGroup sit SIDE-BY-SIDE in one
        // layer (the compass's ←/→ drops); a null LayerGroup gives the slot its own full-span layer,
        // which is exactly the pre-layer behaviour, so existing layouts read unchanged.
        var layers = new List<List<int>>();
        var layerByName = new Dictionary<string, int>();
        for (var s = 0; s < slots.Count; s++)
        {
            var name = slots[s][0].LayerGroup;
            if (string.IsNullOrEmpty(name))
            {
                layers.Add([s]);
            }
            else if (layerByName.TryGetValue(name!, out var at))
            {
                layers[at].Add(s);
            }
            else
            {
                layerByName[name!] = layers.Count;
                layers.Add([s]);
            }
        }

        var edge = items[0].Edge;
        var stack = new Grid(); // rows = layers, stacked top-down on every edge (the DX layer model)

        // The star track per layer, so the between-layer splitters can rebalance adjacent layers.
        var layerTracks = new List<int>();
        for (var i = 0; i < layers.Count; i++)
        {
            if (i > 0) stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var weight = WeightFor(slots[layers[i][0]]);
            stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(weight, GridUnitType.Star) });
            layerTracks.Add(stack.RowDefinitions.Count - 1);
        }

        var track = 0;
        for (var i = 0; i < layers.Count; i++)
        {
            if (i > 0)
            {
                var between = new DockSplitter(Orientation.Horizontal, ActualTheme);

                // Rebalance the two ADJACENT layers by converting their current pixel sizes into
                // star weights. The ratio becomes MODEL state at drag end — it survives rebuilds
                // and, via the layout document, restarts (Russ asked for both).
                var prevTrack = layerTracks[i - 1];
                var nextTrack = layerTracks[i];
                var prevKey = SlotKey(slots[layers[i - 1][0]][0]);
                var nextKey = SlotKey(slots[layers[i][0]][0]);
                double startPrev = 0, startNext = 0;

                between.DragStarted += (_, _) =>
                {
                    startPrev = stack.RowDefinitions[prevTrack].ActualHeight;
                    startNext = stack.RowDefinitions[nextTrack].ActualHeight;
                };

                between.Dragged += cumulative =>
                {
                    const double minLayer = 60;
                    var total = startPrev + startNext;
                    var newPrev = Math.Clamp(startPrev + cumulative, minLayer, total - minLayer);
                    stack.RowDefinitions[prevTrack].Height = new GridLength(newPrev, GridUnitType.Star);
                    stack.RowDefinitions[nextTrack].Height = new GridLength(total - newPrev, GridUnitType.Star);
                };

                between.DragCompleted += (_, _) =>
                {
                    var finalPrev = stack.RowDefinitions[prevTrack].ActualHeight;
                    var finalNext = stack.RowDefinitions[nextTrack].ActualHeight;
                    if (finalPrev > 0 && finalNext > 0)
                    {
                        _slotWeights[prevKey] = finalPrev;
                        _slotWeights[nextKey] = finalNext;
                        PersistLayoutAsync();
                    }
                };

                Place(between, -1, track);
                stack.Children.Add(between);
                track++;
            }

            // Between-layer handles straddle their seam, so keep this layer clear of the ones above
            // and below it (XU-631).
            var layerElement = PadAgainstSeam(BuildLayer(edge, slots, layers[i]), new Thickness(
                0,
                i > 0 ? DockSplitter.StraddleClearance : 0,
                0,
                i < layers.Count - 1 ? DockSplitter.StraddleClearance : 0));
            Place(layerElement, -1, track);
            stack.Children.Add(layerElement);
            track++;
        }

        return stack;
    }

    /// <summary>
    /// Builds one layer: a single slot fills it outright; multiple slots sit side-by-side in star
    /// columns with splitters between them. Column ratios are session-scoped for now — the layout
    /// document persists layer heights only.
    /// </summary>
    private FrameworkElement BuildLayer(DockEdge edge, List<List<DockItem>> slots, List<int> slotIndexes)
    {
        var layerItems = slotIndexes.SelectMany(s => slots[s]).ToList();

        FrameworkElement BuildSlotChrome(List<DockItem> slot)
            => slot.Count == 1 ? BuildItemChrome(slot[0]) : BuildTabGroupChrome(slot);

        if (slotIndexes.Count == 1)
        {
            var only = slots[slotIndexes[0]];
            var chrome = BuildSlotChrome(only);
            _slotRegistry.Add((edge, only, chrome, layerItems, chrome));
            return chrome;
        }

        // Column pattern [slot, splitter, slot, ...]: slot c sits at column 2c, its leading
        // splitter at 2c-1.
        var layerGrid = new Grid();
        for (var c = 0; c < slotIndexes.Count; c++)
        {
            if (c > 0) layerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var c = 0; c < slotIndexes.Count; c++)
        {
            if (c > 0)
            {
                var between = new DockSplitter(Orientation.Vertical, ActualTheme);
                var prevColumn = (c - 1) * 2;
                var nextColumn = c * 2;
                double startPrev = 0, startNext = 0;

                between.DragStarted += (_, _) =>
                {
                    startPrev = layerGrid.ColumnDefinitions[prevColumn].ActualWidth;
                    startNext = layerGrid.ColumnDefinitions[nextColumn].ActualWidth;
                };

                between.Dragged += cumulative =>
                {
                    const double minSlot = 60;
                    var total = startPrev + startNext;
                    var newPrev = Math.Clamp(startPrev + cumulative, minSlot, total - minSlot);
                    layerGrid.ColumnDefinitions[prevColumn].Width = new GridLength(newPrev, GridUnitType.Star);
                    layerGrid.ColumnDefinitions[nextColumn].Width = new GridLength(total - newPrev, GridUnitType.Star);
                };

                Grid.SetColumn(between, nextColumn - 1);
                layerGrid.Children.Add(between);
            }

            var slot = slots[slotIndexes[c]];
            var chrome = BuildSlotChrome(slot);

            // Side-by-side handles straddle their seam: keep this slot clear of the one before and
            // after it (XU-631). The registry keeps the unpadded chrome for drop-target hit tests.
            var padded = PadAgainstSeam(chrome, new Thickness(
                c > 0 ? DockSplitter.StraddleClearance : 0,
                0,
                c < slotIndexes.Count - 1 ? DockSplitter.StraddleClearance : 0,
                0));
            Grid.SetColumn(padded, c * 2);
            layerGrid.Children.Add(padded);
            _slotRegistry.Add((edge, slot, chrome, layerItems, layerGrid));
        }

        return layerGrid;
    }

    /// <summary>
    /// Docked slots as last built: their edge, member items, on-screen chrome — plus every item in
    /// the slot's LAYER and the layer's own chrome, so the compass's above/below drops (layer ops)
    /// can target and preview the whole row while ←/→ and ▣ target the slot.
    /// </summary>
    private readonly List<(DockEdge Edge, List<DockItem> Items, FrameworkElement Chrome, List<DockItem> LayerItems, FrameworkElement LayerChrome)> _slotRegistry = new();

    /// <summary>A slot's on-screen bounds in overlay coordinates, inflated for sticky-hover checks.</summary>
    private Windows.Foundation.Rect SlotHitRect(int slotIndex, double inflate)
    {
        var chrome = _slotRegistry[slotIndex].Chrome;
        var topLeft = chrome.TransformToVisual(_overlayLayer).TransformPoint(new Windows.Foundation.Point(0, 0));
        return new Windows.Foundation.Rect(
            topLeft.X - inflate, topLeft.Y - inflate,
            chrome.ActualWidth + inflate * 2, chrome.ActualHeight + inflate * 2);
    }

    /// <summary>The on-screen bounds of the LAYER housing a slot, in overlay coordinates.</summary>
    private Windows.Foundation.Rect LayerHitRect(int slotIndex)
    {
        var chrome = _slotRegistry[slotIndex].LayerChrome;
        var topLeft = chrome.TransformToVisual(_overlayLayer).TransformPoint(new Windows.Foundation.Point(0, 0));
        return new Windows.Foundation.Rect(topLeft.X, topLeft.Y, chrome.ActualWidth, chrome.ActualHeight);
    }

    /// <summary>
    /// Cross-axis size weight per slot, keyed by <see cref="SlotKey"/>. A MODEL store, not grid state:
    /// every rebuild recreates the region grids, so a ratio held only in RowDefinitions was silently
    /// reset to 1:1 by any tab switch or pin toggle. Values are the pixel sizes captured at the end of
    /// a between-slot drag — only their ratio matters.
    /// </summary>
    private readonly Dictionary<string, double> _slotWeights = new();

    /// <summary>A slot's stable identity: its group name for a tab group, its first item's DockId otherwise.</summary>
    private static string SlotKey(DockItem first) => first.TabGroup ?? first.DockId ?? string.Empty;

    private double WeightFor(List<DockItem> slot)
    {
        if (_slotWeights.TryGetValue(SlotKey(slot[0]), out var w) && w > 0) return w;
        // A slot with no stored weight sits next to slots whose weights are PIXEL magnitudes, so a
        // literal 1 would render it invisible. The average keeps an unweighted newcomer mid-sized.
        return _slotWeights.Count > 0 ? _slotWeights.Values.Average() : 1;
    }

    // ---- Tab groups -----------------------------------------------------------------------------

    // Selection is keyed by group name and held as the ITEM REFERENCE, not an index or DockId: items
    // are stable instances across rebuilds, indexes shift when a tab closes or floats, and DockId is
    // optional. Selection deliberately does not persist yet — the layout document carries structure,
    // and which tab was frontmost is cheap to lose.
    private readonly Dictionary<string, DockItem> _tabSelection = new();

    /// <summary>
    /// Renders a tab group: a strip of tab buttons, then the selected item's content. The tabs ARE the
    /// titles, so the group shows no per-item header; the float/close affordances sit at the end of the
    /// strip and act on the selected item.
    /// </summary>
    private FrameworkElement BuildTabGroupChrome(List<DockItem> group)
    {
        var key = group[0].TabGroup!;

        // The stored selection may have been closed or floated since the last rebuild — group here is
        // already filtered to visible items, so falling back to the first is the natural successor.
        var selected = _tabSelection.TryGetValue(key, out var stored) && group.Contains(stored)
            ? stored
            : group[0];

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var strip = new Grid
        {
            Height = 26,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0x80, 0x80, 0x80)),
        };
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // pin
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // float
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // close

        // Hand-rolled DOCUMENT TABS, not SelectorBar. SelectorBar was the first choice, but its
        // selection indicator never rendered visibly here (Russ: "these don't look like tabs …
        // you can't tell which is active") and its ItemContainer input pipeline already cost us the
        // original drag bug. These are the classic dock tabs: the active one is a raised box merged
        // with the content below it, inactive ones are flat dimmed text with a hover wash.
        var activeBg = ComponentTheme.Surface(ActualTheme);
        var tabOutline = ComponentTheme.SurfaceOutline(ActualTheme);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(4, 0, 0, 0),
        };

        var tabs = new List<(Border Tab, DockItem Item)>();
        foreach (var item in group)
        {
            var isSelected = ReferenceEquals(item, selected);
            var tab = new Border
            {
                Padding = new Thickness(10, 3, 10, 3),
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                Background = isSelected ? activeBg : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = tabOutline,
                BorderThickness = isSelected ? new Thickness(1, 1, 1, 0) : new Thickness(0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock
                {
                    Text = item.Title,
                    FontSize = 12,
                    Opacity = isSelected ? 1.0 : 0.65,
                    FontWeight = isSelected
                        ? Microsoft.UI.Text.FontWeights.SemiBold
                        : Microsoft.UI.Text.FontWeights.Normal,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            if (!isSelected)
            {
                tab.PointerEntered += (s, _) =>
                    ((Border)s).Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x18, 0x80, 0x80, 0x80));
                tab.PointerExited += (s, _) =>
                    ((Border)s).Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            panel.Children.Add(tab);
            tabs.Add((tab, item));
        }

        var bar = panel;

        // Tabs are drag handles too: pull a tab out of the strip to ungroup it — drop it on an edge,
        // on another slot's guides, past the host bounds to float, or nowhere (snap back). A plain
        // click (release without crossing the drag threshold, still over the tab) selects.
        WireTabStripDrag(strip, tabs, item =>
        {
            _tabSelection[key] = item;
            Rebuild();
        });

        Grid.SetColumn(bar, 0);
        strip.Children.Add(bar);

        if (selected.ShowPinToggle)
        {
            // Same MasterDetailControl pin as the header: E718 = pinned (a docked strip's selected
            // tab is by definition pinned), clicking unpins to the rail.
            var pin = DockItemHeader.BuildIconButton("\uE718", "Unpin (auto-hide)");
            // Only the SELECTED pane leaves the group (a rail tab appears at the edge), so the ghost
            // is its content region alone \u2014 the strip stays put and refills with the next tab.
            pin.Click += (_, _) => AnimateUnpinSlideOut(selected, selected);
            Grid.SetColumn(pin, 1);
            strip.Children.Add(pin);
        }

        // No float button here either — tearing a TAB out past the host bounds floats it.
        if (selected.CanClose)
        {
            var close = DockItemHeader.BuildButton("✕", "Close");
            close.Click += (_, _) =>
            {
                selected.RaiseCloseRequested();
                selected.IsOpen = false;
            };
            Grid.SetColumn(close, 2);
            strip.Children.Add(close);
        }

        Orphan(selected);
        Grid.SetRow(strip, 0);
        Grid.SetRow(selected, 1);
        grid.Children.Add(strip);
        grid.Children.Add(selected);
        return grid;
    }

    /// <summary>
    /// Makes a group strip's tabs drag handles for the re-dock/ungroup/float pipeline.
    /// </summary>
    /// <remarks>
    /// Raw pointer events on the STRIP, with handledEventsToo — the same pipeline as the headers.
    /// History note: this began as manipulation events on SelectorBarItems and never fired
    /// (ItemContainer captures the pointer and marks events Handled for its selection work — the
    /// original "drag to float does not work" bug); the strip-level rewrite fixed the drag, and the
    /// tabs later became hand-rolled Borders outright when SelectorBar's selection indicator proved
    /// invisible here — so selection is now ours too: a release below the drag threshold, still
    /// over the pressed tab, invokes <paramref name="select"/>.
    /// </remarks>
    private void WireTabStripDrag(Grid strip, List<(Border Tab, DockItem Item)> tabs, Action<DockItem> select)
    {
        const double dragThreshold = 6;

        var armed = false;
        var dragging = false;
        uint armedPointer = 0;
        Border? armedTab = null;
        DockItem? armedItem = null;
        var pressPoint = default(Windows.Foundation.Point); // overlay coords
        var grabOffset = default(Windows.Foundation.Point); // within the grabbed tab

        void Finish(bool commit)
        {
            if (dragging && commit) CommitHeaderDrag();
            armed = false;
            dragging = false;
            armedTab = null;
            armedItem = null;
        }

        strip.AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (armed || _dragItem is not null) return;

            foreach (var (tab, item) in tabs)
            {
                var local = e.GetCurrentPoint(tab).Position;
                if (local.X < 0 || local.Y < 0 || local.X > tab.ActualWidth || local.Y > tab.ActualHeight)
                    continue;

                armed = true;
                dragging = false;
                armedPointer = e.Pointer.PointerId;
                armedTab = tab;
                armedItem = item;
                grabOffset = local;
                pressPoint = e.GetCurrentPoint(_overlayLayer).Position;
                break;
            }
        }), handledEventsToo: true);

        strip.AddHandler(PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (!armed || e.Pointer.PointerId != armedPointer) return;

            var current = e.GetCurrentPoint(_overlayLayer).Position;
            if (!dragging)
            {
                var dx = current.X - pressPoint.X;
                var dy = current.Y - pressPoint.Y;
                if (dx * dx + dy * dy < dragThreshold * dragThreshold) return;

                dragging = true;

                // Steal the pointer so the SelectorBarItem's pending click can't complete mid-drag.
                // Best effort: if the platform keeps the item's capture instead, events still route
                // here through the ancestor chain, and ItemContainer's own release guard (pointer
                // must still be OVER the item) keeps a real drag from selecting on release.
                strip.CapturePointer(e.Pointer);
                BeginHeaderDrag(armedItem!, armedTab!, grabOffset);
            }

            UpdateHeaderDrag(new Windows.Foundation.Point(current.X - pressPoint.X, current.Y - pressPoint.Y));
        }), handledEventsToo: true);

        strip.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (!armed || e.Pointer.PointerId != armedPointer) return;

            var wasDragging = dragging;
            var tab = armedTab;
            var item = armedItem;
            Finish(commit: true);

            // A clean click: never crossed the drag threshold and released still over the pressed
            // tab — that's the selection gesture.
            if (!wasDragging && tab is not null && item is not null)
            {
                var local = e.GetCurrentPoint(tab).Position;
                if (local.X >= 0 && local.Y >= 0 && local.X <= tab.ActualWidth && local.Y <= tab.ActualHeight)
                    select(item);
            }
        }), handledEventsToo: true);

        strip.AddHandler(PointerCanceledEvent, new PointerEventHandler((_, e) =>
        {
            if (armed && e.Pointer.PointerId == armedPointer) Finish(commit: true);
        }), handledEventsToo: true);

        strip.PointerCaptureLost += (_, e) =>
        {
            // Only the STRIP's own capture ending matters here. The armed item's capture-lost — the
            // direct consequence of the steal above — bubbles through this handler too, and treating
            // it as the end would kill the drag the moment it started.
            if (armed && dragging && e.Pointer.PointerId == armedPointer && ReferenceEquals(e.OriginalSource, strip))
                Finish(commit: true);
        };
    }

    /// <summary>
    /// Detaches an item from whatever panel last held it. Rebuilds create fresh chrome around the same
    /// item instances, and an unselected tab keeps its (detached) previous parent between rebuilds —
    /// re-adding a parented element is the one thing reparenting genuinely forbids. FloatItem already
    /// enforces this for the window transition; this makes every chrome path follow the same rule
    /// instead of relying on the framework tolerating a stale parent.
    /// </summary>
    private static void Orphan(DockItem item)
    {
        if (item.Parent is Panel p) p.Children.Remove(item);
    }

    /// <summary>
    /// Wraps an item in its header chrome. Lives on the host rather than in a DockItem template because
    /// the header's contents depend on host-level facts — notably whether the platform can float at all.
    /// </summary>
    private FrameworkElement BuildItemChrome(DockItem item)
    {
        Orphan(item);
        if (!item.ShowHeader) return item;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new DockItemHeader(item, PlatformServices.SupportsFloatingWindows);
        header.CloseRequested += (_, _) =>
        {
            item.RaiseCloseRequested();
            item.IsOpen = false;
        };
        header.PinRequested += (_, _) =>
        {
            // The host owns the transition (the state-machine rule): toggling triggers the rebuild
            // that moves the pane between the docked grid and its rail — and if this header lives in
            // an auto-hide flyout, the same rebuild closes the flyout, which is the correct outcome.
            if (item.IsPinned)
            {
                AnimateUnpinSlideOut(item, grid); // whole chrome (header + content) slides off
            }
            else
            {
                item.IsPinned = true;
                PersistLayoutAsync();
            }
        };
        // The menu's Float; the drag's tear-out reaches the same FloatItem. Placed at the pane's
        // current screen position rather than a cursor point — there is no drag in flight here, so
        // "float where it already is" is the least surprising landing spot.
        header.FloatRequested += (_, _) => FloatItem(item);

        header.DragStarted += (_, grabOffset) => BeginHeaderDrag(item, header, grabOffset);
        header.DragMoved += cumulative => UpdateHeaderDrag(cumulative);
        header.DragCompleted += (_, _) => CommitHeaderDrag();

        Grid.SetRow(header, 0);
        Grid.SetRow(item, 1);
        grid.Children.Add(header);
        grid.Children.Add(item);
        return grid;
    }

    // ---- Floating (command path) ----------------------------------------------------------------
    //
    // The float command per XU-444 scope: a button, no cross-window drag, no docking guides. The
    // mechanism is the XU-396-proven one — detach the SAME instance and reattach it in the new window,
    // so all view state survives for free. Both transitions live here and nowhere else: XU-442's
    // "re-dock leaves the pane collapsed" bug was two half-aware code paths disagreeing about whether
    // the docked slot was open, and this single owner is the structural fix.

    // The Closed handler is stored alongside the window so DockItemBack can unsubscribe it — windows
    // get RECYCLED into the pre-warm slot, and a recycled window re-floated for a different item must
    // not still carry the previous item's close-handling lambda. The drag tracker (the platform's
    // watch on the window for the drag-back-to-redock gesture) is scoped the same way.
    private readonly Dictionary<DockItem, (Window Window, Windows.Foundation.TypedEventHandler<object, WindowEventArgs> ClosedHandler, IDisposable? DragTracker)> _floatingWindows = new();
    private readonly List<(Microsoft.UI.Dispatching.DispatcherQueueTimer Timer, Window Window)> _deferredCloses = new();
    private bool _closingWindowProgrammatically;

    // NOTE: the library used to keep its own pre-warmed spare window here. That whole mechanism moved
    // behind IDockPlatformServices.TryAcquireFloatWindow/TryReleaseFloatWindow: the [DockPerf] numbers
    // (XU-450: construction ~3.6s of UI freeze, close 18ms, and AppWindow.Hide() succeeding WITHOUT
    // hiding on Uno Skia desktop) mean every pooling decision is a platform judgment — where the
    // construction freeze may land, and whether a freed window can genuinely be hidden for reuse. The
    // host owns those; the library only constructs on demand as the fallback.

    private void FloatItem(DockItem item)
    {
        if (item.IsFloating || !item.AllowFloat || !PlatformServices.SupportsFloatingWindows) return;

        // Mark floating FIRST: the rebuild this triggers drops the item's chrome out of the docked
        // tree. The chrome grid still holds the item as a child, so explicitly orphan it — an element
        // cannot enter the new window's tree while it has a parent (detach first, then the move is
        // legal; two-trees-at-once is the only real constraint).
        item.IsFloating = true;
        if (item.Parent is Panel dockedParent) dockedParent.Children.Remove(item);

        // Window acquisition is host policy (pooling, pre-construction) via IDockPlatformServices; the
        // library's fallback is construct-on-demand, paying the platform's construction cost (~3.6s on
        // Uno Skia desktop, XU-450) under the gesture.
        var floatSw = System.Diagnostics.Stopwatch.StartNew();
        var window = PlatformServices.TryAcquireFloatWindow();
        var warm = window is not null;

        // A single-child shell rather than window.Content = item directly: the re-dock path detaches
        // the item by removing it from a PANEL parent, and direct window content has none. (The
        // "Dock back" button that used to live here is retired — dragging the window back over the
        // host is the re-dock gesture now, and the window's ✕ closes the pane.)
        var wrapper = new Grid();
        wrapper.Children.Add(item);

        // A pooled window can be DEAD — Russ hit this: a window whose hide silently no-oped sat visible
        // and blank, he closed it by hand, and the next float activated the corpse and threw from
        // Window.Activate(). The pool now evicts on Closed, but the library still refuses to trust it:
        // present defensively, retry with a fresh window, and revert the whole transition if even that
        // fails, so the pane can never be stranded invisible (dropped from the docked tree, no window).
        if (window is null || !TryPresentFloatWindow(item, window, wrapper))
        {
            if (warm) Console.WriteLine("[DockPerf] FloatItem: pooled window failed to present — constructing fresh");
            warm = false;
            if (!TryPresentFloatWindow(item, new Window(), wrapper))
            {
                if (item.Parent is Panel p) p.Children.Remove(item);
                item.IsFloating = false; // triggers the rebuild that re-docks the pane
                Console.WriteLine("[DockPerf] FloatItem: fresh window also failed — float reverted");
                return;
            }
        }

        // The item now lives under the new window's XamlRoot — let content resync per-root state.
        item.RaiseReparented();

        // [DockPerf] diagnostics stay until XU-450 closes — its repro reads these lines.
        Console.WriteLine($"[DockPerf] FloatItem: warm={warm}, total={floatSw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Attempts to show <paramref name="item"/>'s float wrapper in <paramref name="window"/>. False
    /// means the window was unusable (typically a dead pooled window); nothing is left subscribed or
    /// registered on failure.
    /// </summary>
    private bool TryPresentFloatWindow(DockItem item, Window window, UIElement wrapper)
    {
        try
        {
            window.Title = item.Title ?? "Panel";
            window.Content = wrapper;
            window.Activate();
        }
        catch
        {
            // Detach the wrapper so a retry can attach it to another window — an element cannot enter
            // a second tree while the dead window's tree still holds it.
            try { window.Content = null; } catch { /* the window is beyond caring */ }
            return false;
        }

        // Subscribe only after presentation succeeds, so a failed attempt leaves no handler behind.
        // The window's own ✕ means "close this pane" — the item returns to the docked tree but stays
        // closed, exactly like the header's ✕ on a docked pane. An explicit "Dock back" is the reopen
        // path. Distinguishing the two is the XU-442 lesson.
        Windows.Foundation.TypedEventHandler<object, WindowEventArgs> onClosed = (_, _) =>
        {
            if (!_closingWindowProgrammatically)
                DockItemBack(item, reopen: false, windowAlreadyClosed: true);
        };
        window.Closed += onClosed;

        // Watch the window for the OS title-bar drag so dragging it back over the host can offer the
        // dock guides. Null tracker (the default platform) = the gesture simply isn't available.
        var tracker = PlatformServices.TrackFloatingWindowDrag(
            window,
            screen => OnFloatDragMoved(item, screen),
            screen => OnFloatDragDropped(item, screen));

        _floatingWindows[item] = (window, onClosed, tracker);

        // The host's window-management policy (XU-438 group activation, later position/size) applies
        // here; the library has no opinion.
        PlatformServices.OnWindowSpawned(window);
        return true;
    }

    private void DockItemBack(DockItem item, bool reopen, bool windowAlreadyClosed = false)
    {
        if (!item.IsFloating) return;

        Window? windowToClose = null;
        if (_floatingWindows.Remove(item, out var entry))
        {
            entry.Window.Closed -= entry.ClosedHandler;
            entry.DragTracker?.Dispose(); // the window is no longer this pane's — stop watching its drags

            // Same rule in reverse: orphan the item from the window's tree before the rebuild puts it
            // back into the docked one.
            if (item.Parent is Panel floatParent) floatParent.Children.Remove(item);

            if (!windowAlreadyClosed)
            {
                // NOTHING window-infra-related runs synchronously here. The first dock-back fix closed
                // the window inline (stall); the second hid it inline to recycle it — still slow,
                // because Hide/Content-detach are plausibly the same blocking cost family as
                // construction and teardown (XU-442). The synchronous path now touches only the
                // wrapper grid (the Orphan above); hide-or-close happens on a timer after the re-dock
                // frame has presented. Cosmetic cost: the emptied floating window lingers ~600ms.
                windowToClose = entry.Window;
            }
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        item.IsFloating = false;
        var tFloatFlip = sw.ElapsedMilliseconds;
        item.IsOpen = reopen; // reopen=true is "Dock back"; false is the window's ✕ = close the pane.
        var tOpenFlip = sw.ElapsedMilliseconds;

        // Back under the host's XamlRoot (or parked closed awaiting reopen) — same resync hook as the
        // float direction; handlers read IsFloating/IsOpen for where the item landed.
        item.RaiseReparented();

        // [DockPerf] diagnostics stay until XU-450 (the ~3.6s Window construction investigation)
        // closes — its repro instructions read these lines from the harness's redirected stdout.
        Console.WriteLine($"[DockPerf] DockItemBack sync path: IsFloating flip={tFloatFlip}ms, IsOpen flip={tOpenFlip - tFloatFlip}ms, total={sw.ElapsedMilliseconds}ms");

        if (windowToClose is not null) DeferWindowRelease(windowToClose);
    }

    /// <summary>
    /// Disposes of a freed floating window AFTER the re-dock frame has presented: recycle it into the
    /// pre-warm slot when possible, close it when not.
    /// </summary>
    private void DeferWindowRelease(Window window)
    {
        var timer = DispatcherQueue.CreateTimer();
        // 200ms, down from 600: long enough for the re-dock frame to present, short enough that the
        // emptied floating window barely registers before it disappears (Russ flagged the linger).
        timer.Interval = TimeSpan.FromMilliseconds(200);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            _deferredCloses.RemoveAll(x => ReferenceEquals(x.Window, window));

            var sw = System.Diagnostics.Stopwatch.StartNew();

            long tContent;
            try
            {
                window.Content = null;
                tContent = sw.ElapsedMilliseconds;
            }
            catch
            {
                // The user closed the window during the deferral; nothing left to release.
                Console.WriteLine("[DockPerf] DeferWindowRelease: window already gone");
                return;
            }

            // Offer the freed window back to the host's pool. The host must VERIFY its hide worked
            // (AppWindow.Hide() succeeds without hiding on Uno Skia desktop — XU-450) and own the
            // window's liveness from here. A declined offer falls back to the measured-cheap close.
            if (PlatformServices.TryReleaseFloatWindow(window))
            {
                Console.WriteLine($"[DockPerf] DeferWindowRelease: content={tContent}ms → released to host pool ({sw.ElapsedMilliseconds - tContent}ms)");
                return;
            }

            CloseWindowGuarded(window);
            Console.WriteLine($"[DockPerf] DeferWindowRelease: content={tContent}ms, declined by pool, close={sw.ElapsedMilliseconds - tContent}ms");
        };
        _deferredCloses.Add((timer, window)); // rooted here so the pending timer can't be collected
        timer.Start();
    }

    private void CloseWindowGuarded(Window window)
    {
        // Guard the Closed handler path: this close is ours, not the user closing the window.
        _closingWindowProgrammatically = true;
        try { window.Close(); }
        catch { /* already closed, or teardown quirks — the window is unreferenced either way */ }
        finally { _closingWindowProgrammatically = false; }
    }

    // ---- Persistence ----------------------------------------------------------------------------

    // Deliberately a hand-rolled delimited format rather than System.Text.Json. Reflection-based
    // serialization is exactly the shape the WASM publish trimmer strips (XU-360), and a layout
    // document that silently fails to round-trip only on a published build is a bad trade for a few
    // fields per item.
    //
    // v5 format: dockId|edge|length|isOpen|tabGroup|isPinned|slotWeight|band|layerGroup, records
    // separated by ';' in ITEM ORDER — record order IS the persisted slot/layer order, since
    // drag-drop can reorder, group and layer panes. slotWeight (0 = none stored) restores the
    // between-layer ratio; items in one slot share a key so they carry the same value. band is the
    // outer/inner placement of the doubled compass; layerGroup is side-by-side layer membership.
    // ApplyLayout accepts v1 (4), v2 (6), v3 (7) and v4 (8) records too.
    private const char RecordSeparator = ';';
    private const char FieldSeparator = '|';

    /// <summary>Serializes the current layout. Items without a <see cref="DockItem.DockId"/> are skipped.</summary>
    private string SerializeLayout()
        => string.Join(RecordSeparator, Items
            .Where(i => !string.IsNullOrWhiteSpace(i.DockId))
            .Select(i => string.Join(FieldSeparator,
                i.DockId,
                (int)i.Edge,
                i.DockLength.ToString("F0", System.Globalization.CultureInfo.InvariantCulture),
                i.IsOpen ? "1" : "0",
                // Sanitize: a group name carrying our separators would shear the document.
                (i.TabGroup ?? string.Empty).Replace(FieldSeparator, '_').Replace(RecordSeparator, '_'),
                i.IsPinned ? "1" : "0",
                (_slotWeights.TryGetValue(SlotKey(i), out var w) ? w : 0)
                    .ToString("F0", System.Globalization.CultureInfo.InvariantCulture),
                (int)i.Band,
                (i.LayerGroup ?? string.Empty).Replace(FieldSeparator, '_').Replace(RecordSeparator, '_'))));

    private void ApplyLayout(string document)
    {
        var orderedIds = new List<string>();

        foreach (var record in document.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var f = record.Split(FieldSeparator);
            if (f.Length is not (4 or 6 or 7 or 8 or 9)) continue; // v1..v5 records only

            var item = Items.FirstOrDefault(i => i.DockId == f[0]);
            if (item is null) continue; // A pane that no longer exists — ignore rather than fail.

            orderedIds.Add(f[0]);

            if (Enum.TryParse<DockEdge>(f[1], out var edge) && item.AllowedDockEdges.HasFlag(edge))
                item.Edge = edge;

            if (double.TryParse(f[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var length))
                item.SetDockLengthSilently(Math.Clamp(length, item.MinLength, item.MaxLength));

            if (RestoreItemOpenState) item.IsOpen = f[3] == "1";

            if (f.Length >= 6)
            {
                // TabGroup must be applied BEFORE the weight is read: SlotKey prefers the group name.
                item.TabGroup = string.IsNullOrEmpty(f[4]) ? null : f[4];
                item.IsPinned = f[5] == "1";
            }

            if (f.Length >= 7
                && double.TryParse(f[6], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var weight)
                && weight > 0)
            {
                _slotWeights[SlotKey(item)] = weight;
            }

            if (f.Length >= 8 && int.TryParse(f[7], out var band)
                && Enum.IsDefined(typeof(DockBand), band))
            {
                item.Band = (DockBand)band;
            }

            if (f.Length >= 9)
            {
                item.LayerGroup = string.IsNullOrEmpty(f[8]) ? null : f[8];
            }
        }

        RestoreItemOrder(orderedIds);
    }

    /// <summary>
    /// Reorders Items to match the persisted sequence. Items the document doesn't mention (new panes
    /// added since the save) keep their relative order and end up after the known ones — appended, not
    /// dropped, matching the "new cards must still show up somewhere" rule.
    /// </summary>
    private void RestoreItemOrder(List<string> orderedIds)
    {
        if (orderedIds.Count == 0) return;

        var list = (ObservableCollection<DockItem>)Items;
        var desired = orderedIds
            .Select(id => list.FirstOrDefault(i => i.DockId == id))
            .Where(i => i is not null)
            .Cast<DockItem>()
            .Concat(list.Where(i => !orderedIds.Contains(i.DockId ?? string.Empty)))
            .ToList();

        // Move (never Clear): Clear raises a Reset with no OldItems, which would leak the per-item
        // LayoutAffectingPropertyChanged subscriptions OnItemsChanged maintains.
        for (var target = 0; target < desired.Count; target++)
        {
            var current = list.IndexOf(desired[target]);
            if (current != target) list.Move(current, target);
        }
    }

    private async void PersistLayoutAsync()
    {
        if (Persistence is null || string.IsNullOrWhiteSpace(PersistenceKey)) return;

        try
        {
            await Persistence.SaveAsync(PersistenceKey!, SerializeLayout());
        }
        catch
        {
            // Best-effort by contract: failing to save a UI preference must never surface to the user,
            // and this runs from a drag-completed handler where a throw would be unobservable anyway.
        }
    }

    private async Task RestoreLayoutAsync()
    {
        if (Persistence is null || string.IsNullOrWhiteSpace(PersistenceKey)) return;

        try
        {
            var document = await Persistence.LoadAsync(PersistenceKey!);
            if (!string.IsNullOrWhiteSpace(document))
            {
                ApplyLayout(document!);
                Rebuild();
            }
        }
        catch
        {
            // No saved layout, or an unreadable one — fall back to the layout as authored in XAML.
        }
    }

    // ---- Header drag: intra-host re-dock (XU-447 half one) --------------------------------------
    //
    // Drag a pane's header toward an edge; docking-guide INDICATORS (small squares at each allowed
    // edge's midpoint, per the UnoDock prior-art approach the epic recommends) show where a drop is
    // legal; hovering one previews the landing region; releasing over it re-docks the item there.
    // Everything is visual-only until release — the drag hazard noted on XU-447 is that a mid-drag
    // rebuild would destroy the very element being dragged, so the ONLY mutation is item.Edge at
    // commit. Cross-window tear-out drag is the deferred second half.

    private const double GuideSize = 44;
    private const double GuideInset = 16;
    private const double GuideHitPadding = 8; // forgiving hover on a 44px target
    private const double PreviewThickness = 72;

    /// <summary>What releasing the drag right now would do.</summary>
    private enum DropKind { None, Edge, Group, InsertAbove, InsertBelow, InsertLeft, InsertRight }

    private DockItem? _dragItem;
    // Handle top-left in overlay coords — now only the basis for _dragPointer below. It is NOT the
    // ghost's anchor any more: the ghost centres on the pointer (see CenterGhostOn).
    private Windows.Foundation.Point _dragOrigin;
    private Windows.Foundation.Point _dragPointer; // actual pointer in overlay coords (hit testing)
    private Windows.Foundation.Point _dragLastPointer; // last tracked pointer — the release lands here
    private Border? _dragGhost;
    private TextBlock? _dragGhostFloatHint; // the ghost's "❐ float" tag — visible when release = float
    private Border? _dragPreview;
    private readonly List<Border> _dragZones = new();
    private DockEdge _dragHoverEdge = DockEdge.None;
    private DockBand _dragHoverBand = DockBand.Outer; // meaningful only while _dragHoverEdge != None
    // True while the active drag is a floating window being dragged back over the host (no ghost, no
    // tear-out, commit comes from the platform's dropped callback rather than a pointer release).
    private bool _redockActive;

    // The document compass (DevExpress reference, Russ's spec): anchored at the document's centre,
    // no centre button, and DOUBLED arrows per direction distinguished by DISTANCE — the INNER
    // arrow docks only the document's extent (DockBand.Inner), the OUTER docks against the entire
    // host border (DockBand.Outer). Built once per drag alongside the edge markers.
    private readonly List<(Border Guide, DockEdge Edge, DockBand Band, Windows.Foundation.Rect Hit)> _documentGuides = new();

    // Per-slot drop targets: hovering a docked pane raises a small guide cluster over it — centre
    // square = join it as a tab group, directional squares = dock adjacently before/after it in its
    // region's stack. This is the "group them or place left/right/over/under" Russ asked for.
    private DropKind _dropKind = DropKind.None;
    private int _hoverSlotIndex = -1;
    // Edge is meaningful only when Kind == DropKind.Edge; DockEdge.None otherwise. (No cluster guide
    // produces Edge today — the compass is slot-local — but the shape stays for the recursive build.)
    private readonly List<(Border Guide, DropKind Kind, DockEdge Edge, Windows.Foundation.Rect Hit)> _clusterGuides = new();

    // The cluster's overall footprint in overlay coords — hover territory, so crossing the compass's
    // own buttons (which may overhang a thin slot) never reads as leaving the slot. Zero when down.
    private Windows.Foundation.Rect _clusterBounds;

    private void BeginHeaderDrag(DockItem item, FrameworkElement handle, Windows.Foundation.Point grabOffset)
    {
        if (_dragItem is not null) return;
        _dragItem = item;
        _dragHoverEdge = DockEdge.None;
        _dropKind = DropKind.None;
        _hoverSlotIndex = -1;

        _dragOrigin = handle.TransformToVisual(_overlayLayer)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        // The pointer is wherever inside the header the user grabbed — tracking the corner instead
        // would make the guides feel off by the grab offset.
        _dragPointer = new Windows.Foundation.Point(_dragOrigin.X + grabOffset.X, _dragOrigin.Y + grabOffset.Y);
        _dragLastPointer = _dragPointer;

        ShowDragGuides(item);

        // A lightweight ghost of the header follows the drag; the real pane stays where it is until
        // the drop commits. The "❐ float" tag lights up whenever a release RIGHT NOW would float
        // the pane (open space, no guide targeted) — Russ asked for a float indication, and the tag
        // doubles as the discoverability for drop-anywhere-floats.
        _dragGhostFloatHint = new TextBlock
        {
            Text = "❐ float",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        _dragGhost = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xC0, 0x40, 0x40, 0x40)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform { X = _dragOrigin.X, Y = _dragOrigin.Y },
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = item.Title,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                        FontSize = 13,
                        TextWrapping = TextWrapping.NoWrap,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    _dragGhostFloatHint,
                },
            },
        };
        _overlayLayer.Children.Add(_dragGhost);

        // Measure now so the ghost can be centred on the very first frame: it has no ActualWidth until
        // a layout pass runs, and without this it would appear corner-anchored for one frame and then
        // snap across to the cursor.
        _dragGhost.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        PositionGhostUnder(_dragPointer);
    }

    /// <summary>
    /// Raises the drag guides for <paramref name="item"/>: the four edge MARKERS at the host borders
    /// and the DOCUMENT COMPASS at the document's centre (both straight from the DevExpress demo
    /// Russ screenshotted). Shared by the two drag sources: in-host header/tab drags (which add a
    /// ghost on top) and the floating-window re-dock drag (where the floating window is the ghost).
    /// </summary>
    private void ShowDragGuides(DockItem item)
    {
        ShowDocumentCompass(item);

        // One guide square per edge the item may legally land on — a disallowed edge never appears
        // (the AllowedDockEdges contract). Glyphs are half-filled squares spelled as escapes: plain
        // BMP unicode, no symbol font (the mojibake lesson).
        foreach (var edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
        {
            if (!item.AllowedDockEdges.HasFlag(edge)) continue;

            // Arrows pointing into their edge — matching the DevExpress edge markers and the cluster's
            // cross-axis arrows, so one visual language covers both guide tiers.
            var glyph = edge switch
            {
                DockEdge.Left => "←",
                DockEdge.Right => "→",
                DockEdge.Top => "↑",
                _ => "↓",
            };

            var guide = new Border
            {
                Width = GuideSize,
                Height = GuideSize,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xD8, 0x2B, 0x2B, 0x2B)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Tag = edge,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontSize = 20,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            switch (edge)
            {
                case DockEdge.Left:
                    guide.HorizontalAlignment = HorizontalAlignment.Left;
                    guide.VerticalAlignment = VerticalAlignment.Center;
                    guide.Margin = new Thickness(GuideInset, 0, 0, 0);
                    break;
                case DockEdge.Right:
                    guide.HorizontalAlignment = HorizontalAlignment.Right;
                    guide.VerticalAlignment = VerticalAlignment.Center;
                    guide.Margin = new Thickness(0, 0, GuideInset, 0);
                    break;
                case DockEdge.Top:
                    guide.HorizontalAlignment = HorizontalAlignment.Center;
                    guide.VerticalAlignment = VerticalAlignment.Top;
                    guide.Margin = new Thickness(0, GuideInset, 0, 0);
                    break;
                default:
                    guide.HorizontalAlignment = HorizontalAlignment.Center;
                    guide.VerticalAlignment = VerticalAlignment.Bottom;
                    guide.Margin = new Thickness(0, 0, 0, GuideInset);
                    break;
            }

            _dragZones.Add(guide);
            _overlayLayer.Children.Add(guide);
        }
    }

    /// <summary>
    /// Places the drag ghost horizontally centred on the pointer and sitting just BELOW it (overlay
    /// coords) — the cursor stays clear of the label rather than sitting on top of it.
    /// </summary>
    /// <remarks>
    /// It used to translate the ghost from the header's top-left corner by the drag's cumulative
    /// delta, which anchored it to the corner — so grabbing a wide header anywhere but its far left
    /// left the ghost trailing behind the cursor by exactly the grab offset (Russ: "far off to the
    /// left ... by the distance of where I clicked on the header"). Positioning against the pointer
    /// makes the grab point irrelevant, which is what a drag ghost should do.
    /// <para>Falls back to <c>DesiredSize</c> before the first layout pass has given the ghost an
    /// ActualWidth, so the very first frame lands correctly rather than jumping into place.</para>
    /// </remarks>
    private void PositionGhostUnder(Windows.Foundation.Point pointer)
    {
        if (_dragGhost is null) return;

        var width = _dragGhost.ActualWidth > 0 ? _dragGhost.ActualWidth : _dragGhost.DesiredSize.Width;

        var transform = (Microsoft.UI.Xaml.Media.TranslateTransform)_dragGhost.RenderTransform;
        transform.X = pointer.X - (width / 2);
        transform.Y = pointer.Y + GhostCursorGap;
    }

    /// <summary>How far below the pointer the drag ghost's top edge sits — clear of a standard arrow
    /// cursor without drifting away from it.</summary>
    private const double GhostCursorGap = 18;

    private void UpdateHeaderDrag(Windows.Foundation.Point cumulative)
    {
        if (_dragItem is null || _dragGhost is null) return;

        // Hit-test the POINTER (grab point + cumulative) — cumulative keeps reporting while the
        // pointer is over the guides, so no capture juggling.
        var pointer = new Windows.Foundation.Point(_dragPointer.X + cumulative.X, _dragPointer.Y + cumulative.Y);

        PositionGhostUnder(pointer);

        // TEAR-OUT (XU-447 half two, the trigger): dragging clearly past the host bounds floats the
        // pane at the cursor. The threshold keeps an edge-guide graze from tearing out accidentally.
        // In-host drags only — the re-dock drag never comes through here, its pane already floats.
        const double tearOutThreshold = 32;
        if (_dragItem.AllowFloat && PlatformServices.SupportsFloatingWindows)
        {
            var outside = pointer.X < -tearOutThreshold
                || pointer.Y < -tearOutThreshold
                || pointer.X > _overlayLayer.ActualWidth + tearOutThreshold
                || pointer.Y > _overlayLayer.ActualHeight + tearOutThreshold;
            if (outside)
            {
                TearOutToFloat(pointer);
                return;
            }
        }

        UpdateDragTracking(pointer);
    }

    /// <summary>
    /// Resolves what dropping at <paramref name="pointer"/> (overlay coords) would do, and refreshes
    /// every guide visual to match. Shared by the in-host header/tab drag and the floating-window
    /// re-dock drag. Priority: 1) a visible slot cluster's guides, 2) the outer edge guides,
    /// 3) hovering a docked slot raises its cluster.
    /// </summary>
    private void UpdateDragTracking(Windows.Foundation.Point pointer)
    {
        if (_dragItem is null) return;

        _dragLastPointer = pointer; // where a release would land — the none-drop float uses this

        _dropKind = DropKind.None;
        var hoverEdge = DockEdge.None;
        var hoverBand = DockBand.Outer;

        // 1) Slot-cluster guides (visible only while a slot is hovered): the join-as-group centre
        // and the insert-before/after arrows.
        foreach (var (_, kind, edge, hit) in _clusterGuides)
        {
            if (hit.Contains(pointer))
            {
                _dropKind = kind;
                if (kind == DropKind.Edge) hoverEdge = edge;
                break;
            }
        }

        // 2) The document compass — inner (document-extent) and outer (full-border) arrows.
        if (_dropKind == DropKind.None)
        {
            foreach (var (_, edge, band, hit) in _documentGuides)
            {
                if (hit.Contains(pointer))
                {
                    _dropKind = DropKind.Edge;
                    hoverEdge = edge;
                    hoverBand = band;
                    break;
                }
            }
        }

        // 3) The edge markers at the host borders — always full-border (outer) drops.
        if (_dropKind == DropKind.None)
        {
            foreach (var edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
            {
                if (!_dragItem.AllowedDockEdges.HasFlag(edge)) continue;
                if (GuideRect(edge).Contains(pointer)) { hoverEdge = edge; hoverBand = DockBand.Outer; _dropKind = DropKind.Edge; break; }
            }
        }

        _dragHoverEdge = hoverEdge;
        _dragHoverBand = hoverBand;

        // 4) Which docked slot is under the pointer? Raise/replace/clear the per-slot cluster. The
        // dragged item's own slot is never a target, and neither is a slot on an edge the item may
        // not dock to. Three stabilisers, all bought with an observed flicker loop:
        //   - While the pointer is ON any guide (kind != None), the current slot is PINNED. Guides
        //     can sit over a slot's chrome; resolving the slot afresh there tore down the very
        //     cluster being hovered, which un-hovered the guide and re-raised it — a per-event
        //     flicker Russ's releases kept landing on the "off" frame of.
        //   - The cluster's own FOOTPRINT counts as hover territory: over thin panes the compass
        //     overhangs the pane's bounds, and travelling across its buttons must not read as
        //     "left the pane".
        //   - STICKY bounds: the current slot holds while the pointer stays inside its slightly
        //     inflated rect, so pixel jitter on an exact boundary can't strobe the compass.
        var slotIndex = _hoverSlotIndex;
        if (_dropKind == DropKind.None)
        {
            slotIndex = -1;
            if (_hoverSlotIndex >= 0 && _hoverSlotIndex < _slotRegistry.Count
                && (SlotHitRect(_hoverSlotIndex, inflate: 8).Contains(pointer)
                    || (_clusterBounds.Width > 0 && _clusterBounds.Contains(pointer))))
            {
                slotIndex = _hoverSlotIndex;
            }
            else
            {
                for (var i = 0; i < _slotRegistry.Count; i++)
                {
                    var (edge, items, _, _, _) = _slotRegistry[i];
                    if (items.Contains(_dragItem)) continue;
                    if (!_dragItem.AllowedDockEdges.HasFlag(edge)) continue;
                    if (SlotHitRect(i, inflate: 0).Contains(pointer)) { slotIndex = i; break; }
                }
            }
        }

        if (slotIndex != _hoverSlotIndex)
        {
            _hoverSlotIndex = slotIndex;
            RebuildSlotCluster(slotIndex);
        }

        // Highlight whatever the current drop target is.
        foreach (var zone in _dragZones)
        {
            var isHover = _dropKind == DropKind.Edge && (DockEdge)zone.Tag! == hoverEdge && hoverBand == DockBand.Outer;
            zone.Background = new SolidColorBrush(isHover
                ? Windows.UI.Color.FromArgb(0xE8, 0x2D, 0x5B, 0x8A)
                : Windows.UI.Color.FromArgb(0xD8, 0x2B, 0x2B, 0x2B));
        }

        foreach (var (guide, edge, band, _) in _documentGuides)
        {
            var isHover = _dropKind == DropKind.Edge && edge == hoverEdge && band == hoverBand;
            guide.Background = new SolidColorBrush(isHover
                ? Windows.UI.Color.FromArgb(0xE8, 0x2D, 0x5B, 0x8A)
                : Windows.UI.Color.FromArgb(0xD8, 0x2B, 0x2B, 0x2B));
        }

        foreach (var (guide, kind, edge, _) in _clusterGuides)
        {
            var isHover = _dropKind == kind && (kind != DropKind.Edge || edge == hoverEdge);
            guide.Background = new SolidColorBrush(isHover
                ? Windows.UI.Color.FromArgb(0xE8, 0x2D, 0x5B, 0x8A)
                : Windows.UI.Color.FromArgb(0xD8, 0x2B, 0x2B, 0x2B));
        }

        // Would releasing here float the pane? Drives both float affordances below.
        var wouldFloat = _dropKind == DropKind.None
            && !_redockActive
            && _dragItem.AllowFloat
            && PlatformServices.SupportsFloatingWindows
            && !IsOverOwnSlot(_dragItem, pointer);

        // The shaded landing region for whatever the current target is (every target kind gets one —
        // the DevExpress demo's beige wash). An open-space release is a target too: its zone is the
        // frame the new floating window would occupy, riding the cursor.
        var preview = ComputePreviewRect(hoverEdge, hoverBand, slotIndex);
        if (preview is null && wouldFloat) preview = FloatPreviewRect(pointer);
        ApplyPreview(preview);

        // The ghost's float tag: on exactly when releasing here would float, naming what the zone
        // shows.
        if (_dragGhostFloatHint is not null)
            _dragGhostFloatHint.Visibility = wouldFloat ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The approximate frame a float at <paramref name="pointer"/> would occupy: the pane's current
    /// docked size when it has one, its DockLength otherwise, anchored the way the real tear-out
    /// window is (the cursor lands on the would-be title area).
    /// </summary>
    private Windows.Foundation.Rect FloatPreviewRect(Windows.Foundation.Point pointer)
    {
        var item = _dragItem!;
        var w = Math.Clamp(item.ActualWidth > 40 ? item.ActualWidth : item.DockLength, 200, 520);
        var h = Math.Clamp(item.ActualHeight > 40 ? item.ActualHeight : 260, 160, 420);
        return new Windows.Foundation.Rect(pointer.X - 60, pointer.Y - 16, w, h);
    }

    /// <summary>
    /// Ends the drag by floating the pane at the cursor — the mid-drag gesture for dragging clearly
    /// past the host bounds. Drag state is cleared FIRST: FloatItem's rebuild destroys the header
    /// being dragged, and the pointer events that follow must land in the handlers as no-ops.
    /// </summary>
    private void TearOutToFloat(Windows.Foundation.Point pointer)
    {
        var item = _dragItem!;
        ClearDragState();
        FloatAtPointer(item, pointer);

        // XU-461: the user's button is still down, so hand the gesture to the OS — the window then
        // follows the cursor and lands where they RELEASE, which is what a tear-out should do. Before
        // this the pane simply dropped at the host boundary the moment it crossed, and moving it where
        // you wanted meant grabbing it a second time (Russ: "I never even release the mouse button,
        // but it drops it"). A platform that can't do this returns false and keeps the old behaviour.
        if (_floatingWindows.TryGetValue(item, out var floated))
        {
            var carried = PlatformServices.BeginWindowDrag(floated.Window);
            Console.WriteLine($"[DockPerf] tear-out float committed (osDrag={carried})");
            return;
        }

        Console.WriteLine("[DockPerf] tear-out float committed");
    }

    /// <summary>Floats <paramref name="item"/> and best-effort places its window at the pointer.</summary>
    private void FloatAtPointer(DockItem item, Windows.Foundation.Point pointer)
    {
        FloatItem(item);

        // Best-effort cursor placement: host frame origin + the overlay's offset within the XamlRoot
        // + the pointer, scaled to physical pixels, nudged so the cursor lands on the new title area.
        // The content-to-frame offset (title bar) isn't exposed, so ~32 DIP approximates it — v1
        // accuracy is cosmetic, and a null HostWindow simply leaves the window at the OS default.
        if (_floatingWindows.TryGetValue(item, out var entry) && PlatformServices.HostWindow is { } host)
        {
            try
            {
                var scale = XamlRoot?.RasterizationScale ?? 1.0;
                var overlayOrigin = _overlayLayer.TransformToVisual(null)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
                var basePos = host.AppWindow.Position;
                entry.Window.AppWindow.Move(new Windows.Graphics.PointInt32
                {
                    X = basePos.X + (int)((overlayOrigin.X + pointer.X - 60) * scale),
                    Y = basePos.Y + (int)((overlayOrigin.Y + pointer.Y + 16) * scale),
                });
            }
            catch
            {
                // Positioning is garnish; the float itself already succeeded.
            }
        }
    }

    /// <summary>
    /// Builds (or clears) the guide cluster over the hovered slot, in the DevExpress compass shape
    /// Russ pointed at: a cross of arrow squares around a centre square. Along the region's stacking
    /// axis the arrows INSERT adjacently before/after the slot; across it they dock to the FULL edge
    /// they point at; the centre joins the slot as a tab group.
    /// </summary>
    private void RebuildSlotCluster(int slotIndex)
    {
        foreach (var (guide, _, _, _) in _clusterGuides) _overlayLayer.Children.Remove(guide);
        _clusterGuides.Clear();
        _clusterBounds = default;

        if (slotIndex < 0 || slotIndex >= _slotRegistry.Count) return;

        var (_, _, chrome, _, _) = _slotRegistry[slotIndex];
        var topLeft = chrome.TransformToVisual(_overlayLayer).TransformPoint(new Windows.Foundation.Point(0, 0));
        var cx = topLeft.X + chrome.ActualWidth / 2;
        var cy = topLeft.Y + chrome.ActualHeight / 2;

        const double s = 34;
        const double gap = 6;
        const double step = s + gap;

        // The full per-pane compass (Russ: "we need 4 buttons around the central button"), possible
        // now that regions are two-level (layers of side-by-side slots):
        //   centre = join this slot as a tab group — drawn as a mini TABBED PANE, the DevExpress
        //            folder-tab silhouette Russ liked for targets whose landing zone produces tabs
        //   ↑ / ↓  = a new full-span layer above/below this slot's layer
        //   ← / →  = side-by-side with this slot, inside its layer
        // Docking to a full HOST edge is always and only the edge markers / document compass.
        AddClusterGuide(MakeTabbedPaneIcon(), cx - s / 2, cy - s / 2, s, DropKind.Group, DockEdge.None);
        AddClusterGuide("↑", cx - s / 2, cy - s / 2 - step, s, DropKind.InsertAbove, DockEdge.None);
        AddClusterGuide("↓", cx - s / 2, cy - s / 2 + step, s, DropKind.InsertBelow, DockEdge.None);
        AddClusterGuide("←", cx - s / 2 - step, cy - s / 2, s, DropKind.InsertLeft, DockEdge.None);
        AddClusterGuide("→", cx - s / 2 + step, cy - s / 2, s, DropKind.InsertRight, DockEdge.None);

        // The compass footprint, for the hover-territory stabiliser: the union of every guide's hit
        // rect, so travelling between buttons (or onto ones overhanging a thin slot) stays "inside".
        var bounds = _clusterGuides[0].Hit;
        foreach (var (_, _, _, hit) in _clusterGuides)
        {
            var x1 = Math.Min(bounds.X, hit.X);
            var y1 = Math.Min(bounds.Y, hit.Y);
            var x2 = Math.Max(bounds.X + bounds.Width, hit.X + hit.Width);
            var y2 = Math.Max(bounds.Y + bounds.Height, hit.Y + hit.Height);
            bounds = new Windows.Foundation.Rect(x1, y1, x2 - x1, y2 - y1);
        }

        _clusterBounds = bounds;
    }

    private void AddClusterGuide(string glyph, double x, double y, double size, DropKind kind, DockEdge edge)
        => AddClusterGuide(MakeGuideGlyph(glyph), x, y, size, kind, edge);

    private void AddClusterGuide(UIElement content, double x, double y, double size, DropKind kind, DockEdge edge)
    {
        var guide = MakeGuide(content, x, y, size);
        _overlayLayer.Children.Add(guide);
        _clusterGuides.Add((guide, kind, edge, new Windows.Foundation.Rect(
            x - GuideHitPadding, y - GuideHitPadding,
            size + GuideHitPadding * 2, size + GuideHitPadding * 2)));
    }

    /// <summary>
    /// The tab-target icon: a miniature tabbed pane (tab notch over a body), so a target whose drop
    /// PRODUCES tabs advertises it — the DevExpress folder-tab look Russ pointed at.
    /// </summary>
    private static UIElement MakeTabbedPaneIcon()
    {
        var icon = new Grid { Width = 20, Height = 16, IsHitTestVisible = false };
        icon.Children.Add(new Border
        {
            Width = 9,
            Height = 5,
            CornerRadius = new CornerRadius(2, 2, 0, 0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        });
        icon.Children.Add(new Border
        {
            Height = 12,
            CornerRadius = new CornerRadius(0, 2, 2, 2),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        return icon;
    }

    /// <summary>
    /// The document compass (from the DevExpress demo): anchored at the document's centre, no centre
    /// button, DOUBLED arrows per direction distinguished by distance — INNER docks only the
    /// document's extent (<see cref="DockBand.Inner"/>), OUTER against the entire host border
    /// (<see cref="DockBand.Outer"/>). Same glyph both distances, exactly like the reference.
    /// </summary>
    private void ShowDocumentCompass(DockItem item)
    {
        Windows.Foundation.Point origin;
        double docW, docH;
        try
        {
            origin = _documentHost.TransformToVisual(_overlayLayer)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            docW = _documentHost.ActualWidth;
            docH = _documentHost.ActualHeight;
        }
        catch
        {
            return; // document not measurable yet — the edge markers still cover edge drops
        }

        if (docW <= 0 || docH <= 0) return;

        var cx = origin.X + docW / 2;
        var cy = origin.Y + docH / 2;
        const double s = 34;
        const double gap = 6;
        const double step = s + gap;

        foreach (var edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
        {
            if (!item.AllowedDockEdges.HasFlag(edge)) continue;

            var (dx, dy, glyph) = edge switch
            {
                DockEdge.Left => (-1.0, 0.0, "←"),
                DockEdge.Right => (1.0, 0.0, "→"),
                DockEdge.Top => (0.0, -1.0, "↑"),
                _ => (0.0, 1.0, "↓"),
            };

            AddDocumentGuide(glyph, cx - s / 2 + dx * step, cy - s / 2 + dy * step, s, edge, DockBand.Inner);
            AddDocumentGuide(glyph, cx - s / 2 + dx * step * 2, cy - s / 2 + dy * step * 2, s, edge, DockBand.Outer);
        }
    }

    private void AddDocumentGuide(string glyph, double x, double y, double size, DockEdge edge, DockBand band)
    {
        var guide = MakeGuide(MakeGuideGlyph(glyph), x, y, size);
        _overlayLayer.Children.Add(guide);
        _documentGuides.Add((guide, edge, band, new Windows.Foundation.Rect(
            x - GuideHitPadding, y - GuideHitPadding,
            size + GuideHitPadding * 2, size + GuideHitPadding * 2)));
    }

    /// <summary>One guide square, absolutely positioned in overlay coordinates via RenderTransform.</summary>
    private static Border MakeGuide(UIElement content, double x, double y, double size)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xD8, 0x2B, 0x2B, 0x2B)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform { X = x, Y = y },
            Child = content,
        };
    }

    private static TextBlock MakeGuideGlyph(string glyph) => new()
    {
        Text = glyph,
        FontSize = 18,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The hit rectangle for an edge's guide square, padded to be forgiving on a small target.</summary>
    private Windows.Foundation.Rect GuideRect(DockEdge edge)
    {
        var w = _overlayLayer.ActualWidth;
        var h = _overlayLayer.ActualHeight;
        const double s = GuideSize + GuideHitPadding * 2;

        return edge switch
        {
            DockEdge.Left => new Windows.Foundation.Rect(GuideInset - GuideHitPadding, (h - s) / 2, s, s),
            DockEdge.Right => new Windows.Foundation.Rect(w - GuideInset - GuideSize - GuideHitPadding, (h - s) / 2, s, s),
            DockEdge.Top => new Windows.Foundation.Rect((w - s) / 2, GuideInset - GuideHitPadding, s, s),
            _ => new Windows.Foundation.Rect((w - s) / 2, h - GuideInset - GuideSize - GuideHitPadding, s, s),
        };
    }

    /// <summary>
    /// The region the pane would occupy if released right now, in overlay coordinates — null when
    /// nothing is targeted. The indicator says "a drop is possible"; this wash says "and this is
    /// exactly what it does". Every target kind previews: full-border and document-extent edge
    /// drops, whole-slot joins, and half-slot inserts.
    /// </summary>
    private Windows.Foundation.Rect? ComputePreviewRect(DockEdge edge, DockBand band, int slotIndex)
    {
        switch (_dropKind)
        {
            case DropKind.Edge when edge != DockEdge.None && band == DockBand.Outer:
            {
                var w = _overlayLayer.ActualWidth;
                var h = _overlayLayer.ActualHeight;
                var t = Math.Max(PreviewThickness,
                    Math.Min(_dragItem?.DockLength ?? PreviewThickness, (edge.IsHorizontal() ? w : h) / 2));
                return edge switch
                {
                    DockEdge.Left => new Windows.Foundation.Rect(0, 0, t, h),
                    DockEdge.Right => new Windows.Foundation.Rect(w - t, 0, t, h),
                    DockEdge.Top => new Windows.Foundation.Rect(0, 0, w, t),
                    _ => new Windows.Foundation.Rect(0, h - t, w, t),
                };
            }

            case DropKind.Edge when edge != DockEdge.None:
            {
                // Inner band: the wash hugs the document, sized to ITS extent — the visible proof of
                // "the inner drops just the size of the center content".
                Windows.Foundation.Point o;
                double dw, dh;
                try
                {
                    o = _documentHost.TransformToVisual(_overlayLayer)
                        .TransformPoint(new Windows.Foundation.Point(0, 0));
                    dw = _documentHost.ActualWidth;
                    dh = _documentHost.ActualHeight;
                }
                catch
                {
                    return null;
                }

                var t = Math.Max(PreviewThickness,
                    Math.Min(_dragItem?.DockLength ?? PreviewThickness, (edge.IsHorizontal() ? dw : dh) / 2));
                return edge switch
                {
                    DockEdge.Left => new Windows.Foundation.Rect(o.X, o.Y, t, dh),
                    DockEdge.Right => new Windows.Foundation.Rect(o.X + dw - t, o.Y, t, dh),
                    DockEdge.Top => new Windows.Foundation.Rect(o.X, o.Y, dw, t),
                    _ => new Windows.Foundation.Rect(o.X, o.Y + dh - t, dw, t),
                };
            }

            case DropKind.Group when slotIndex >= 0 && slotIndex < _slotRegistry.Count:
                return SlotHitRect(slotIndex, inflate: 0);

            case DropKind.InsertAbove or DropKind.InsertBelow when slotIndex >= 0 && slotIndex < _slotRegistry.Count:
            {
                // A new layer: the wash takes the top/bottom half of the target's whole LAYER row.
                var r = LayerHitRect(slotIndex);
                var below = _dropKind == DropKind.InsertBelow;
                return new Windows.Foundation.Rect(r.X, below ? r.Y + r.Height / 2 : r.Y, r.Width, r.Height / 2);
            }

            case DropKind.InsertLeft or DropKind.InsertRight when slotIndex >= 0 && slotIndex < _slotRegistry.Count:
            {
                // Side-by-side: the wash takes the left/right half of the target SLOT.
                var r = SlotHitRect(slotIndex, inflate: 0);
                var right = _dropKind == DropKind.InsertRight;
                return new Windows.Foundation.Rect(right ? r.X + r.Width / 2 : r.X, r.Y, r.Width / 2, r.Height);
            }
        }

        return null;
    }

    private void ApplyPreview(Windows.Foundation.Rect? rect)
    {
        if (rect is not { } r)
        {
            if (_dragPreview is not null)
            {
                _overlayLayer.Children.Remove(_dragPreview);
                _dragPreview = null;
            }

            return;
        }

        if (_dragPreview is null)
        {
            // Solid enough to read instantly over any pane colour (Russ: "make the shade more
            // apparent"), with a crisp border so the landing region's exact boundary is visible.
            _dragPreview = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0x2D, 0x5B, 0x8A)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xE0, 0x4A, 0x82, 0xC4)),
                BorderThickness = new Thickness(2),
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform(),
            };

            // Under the guides, over everything else: index 0 keeps the indicator squares and the
            // ghost on top of the preview wash.
            _overlayLayer.Children.Insert(0, _dragPreview);
        }

        var transform = (Microsoft.UI.Xaml.Media.TranslateTransform)_dragPreview.RenderTransform;
        transform.X = r.X;
        transform.Y = r.Y;
        _dragPreview.Width = r.Width;
        _dragPreview.Height = r.Height;
    }

    private int _generatedGroupCounter;

    /// <summary>Resets every drag field. Guide/ghost VISUALS die in the rebuild that always follows.</summary>
    private void ClearDragState()
    {
        _dragItem = null;
        _dragGhost = null;
        _dragGhostFloatHint = null;
        _dragPreview = null;
        _dragZones.Clear();
        _documentGuides.Clear();
        _clusterGuides.Clear();
        _clusterBounds = default;
        _dragHoverEdge = DockEdge.None;
        _dragHoverBand = DockBand.Outer;
        _dropKind = DropKind.None;
        _hoverSlotIndex = -1;
        _redockActive = false;
    }

    private void CommitHeaderDrag()
    {
        // A re-dock drag never commits through here: its end signal is the platform's dropped
        // callback, not an in-host pointer event.
        if (_dragItem is null || _redockActive) return;

        var item = _dragItem;
        var kind = _dropKind;
        var targetEdge = _dragHoverEdge;
        var targetBand = _dragHoverBand;
        var slotIndex = _hoverSlotIndex;
        var releasePoint = _dragLastPointer;

        ClearDragState();

        if (kind == DropKind.None)
        {
            // Released over no guide at all. DevExpress/VS semantics, which Russ reached for and
            // missed ("dragged to here and it does not give the option to float"): dropping a
            // floatable pane in open space means "float it right here", not "snap back". Snap-back
            // remains for panes that cannot float, for platforms without windows, and for a release
            // still over the pane's own slot — that reads as a cancelled drag, not a float request.
            if (item.AllowFloat && PlatformServices.SupportsFloatingWindows && !IsOverOwnSlot(item, releasePoint))
            {
                FloatAtPointer(item, releasePoint);
                Console.WriteLine("[DockPerf] none-drop float committed");
                return;
            }

            Rebuild(); // nothing moved — rebuild only to clear the guides and ghost from the overlay
            return;
        }

        var moved = ApplyDropPlacement(item, kind, targetEdge, targetBand, slotIndex);
        // Items reordering rebuilds already, but property-only paths need one too.
        Rebuild();
        if (moved) PersistLayoutAsync();
    }

    /// <summary>Whether <paramref name="pointer"/> sits over the slot currently housing <paramref name="item"/>.</summary>
    private bool IsOverOwnSlot(DockItem item, Windows.Foundation.Point pointer)
    {
        for (var i = 0; i < _slotRegistry.Count; i++)
        {
            if (_slotRegistry[i].Items.Contains(item))
                return SlotHitRect(i, inflate: 8).Contains(pointer);
        }

        return false;
    }

    /// <summary>
    /// Applies a resolved drop to the model: edge/group/insert placement plus the re-pin rule.
    /// Returns whether anything actually moved. Shared by the in-host drag commit and the
    /// floating-window re-dock drop.
    /// </summary>
    private bool ApplyDropPlacement(DockItem item, DropKind kind, DockEdge targetEdge, DockBand targetBand, int slotIndex)
    {
        var moved = false;
        switch (kind)
        {
            case DropKind.Edge when targetEdge != DockEdge.None:
                // Edge drop: leave any tab group behind and take the edge in the requested band —
                // outer runs against the host border, inner hugs the document. Same-place drops still
                // count as moves so a grouped item can be pulled out to stand alone on its own edge.
                if (targetEdge != item.Edge || targetBand != item.Band || item.TabGroup is not null || item.LayerGroup is not null)
                {
                    item.TabGroup = null;
                    item.LayerGroup = null; // an edge drop is always its own full-span layer
                    item.Edge = targetEdge;
                    item.Band = targetBand;
                    MoveItemBorderMost(item, targetEdge, targetBand);
                    moved = true;
                }
                break;

            case DropKind.Group when slotIndex >= 0 && slotIndex < _slotRegistry.Count:
            {
                var (edge, slotItems, _, _, _) = _slotRegistry[slotIndex];
                var anchor = slotItems[0];

                // Joining a standalone pane forms a NEW group of the two; joining an existing group
                // extends it. The generated name goes into the layout document per item, so an
                // ad-hoc group round-trips through persistence without needing to be authored.
                var name = anchor.TabGroup;
                if (string.IsNullOrEmpty(name))
                {
                    name = !string.IsNullOrWhiteSpace(anchor.DockId)
                        ? $"group-{anchor.DockId}"
                        : $"group-{++_generatedGroupCounter}";
                    anchor.TabGroup = name;
                }

                item.TabGroup = name;
                item.Edge = edge;
                item.Band = anchor.Band;             // joining a slot means joining its band...
                item.LayerGroup = anchor.LayerGroup; // ...and its layer
                MoveItemNear(item, slotItems, after: true);
                _tabSelection[name!] = item; // the just-dropped tab becomes the selected one
                moved = true;
                break;
            }

            case DropKind.InsertAbove or DropKind.InsertBelow when slotIndex >= 0 && slotIndex < _slotRegistry.Count:
            {
                // A new full-span LAYER above/below the target slot's layer.
                var entry = _slotRegistry[slotIndex];
                item.TabGroup = null;
                item.LayerGroup = null; // its own layer
                item.Edge = entry.Edge;
                item.Band = entry.LayerItems[0].Band;
                MoveItemNear(item, entry.LayerItems, after: kind == DropKind.InsertBelow);
                moved = true;
                break;
            }

            case DropKind.InsertLeft or DropKind.InsertRight when slotIndex >= 0 && slotIndex < _slotRegistry.Count:
            {
                // Side-by-side with the target slot, inside its layer. A singleton layer gets a
                // generated name now, stamped on every current member — the Group-case naming
                // pattern one level up, so ad-hoc layers round-trip through persistence too.
                var entry = _slotRegistry[slotIndex];
                var anchor = entry.Items[0];

                var name = anchor.LayerGroup;
                if (string.IsNullOrEmpty(name))
                {
                    name = !string.IsNullOrWhiteSpace(anchor.DockId)
                        ? $"layer-{anchor.DockId}"
                        : $"layer-{++_generatedGroupCounter}";
                    foreach (var member in entry.LayerItems) member.LayerGroup = name;
                }

                item.TabGroup = null;
                item.LayerGroup = name;
                item.Edge = entry.Edge;
                item.Band = anchor.Band;
                MoveItemNear(item, entry.Items, after: kind == DropKind.InsertRight);
                moved = true;
                break;
            }
        }

        // A successful drop always re-pins: dragging an auto-hidden pane somewhere specific is an
        // unambiguous "I want this docked HERE", not "keep hiding it".
        if (moved) item.IsPinned = true;
        return moved;
    }

    /// <summary>
    /// Repositions <paramref name="item"/> in the Items collection adjacent to a slot's members, so
    /// the slot partition (which follows Items order) renders it where it was dropped.
    /// </summary>
    private void MoveItemNear(DockItem item, List<DockItem> slotItems, bool after)
    {
        var list = (ObservableCollection<DockItem>)Items;
        list.Remove(item);
        var anchor = after ? slotItems[^1] : slotItems[0];
        var index = list.IndexOf(anchor);
        if (index < 0) { list.Add(item); return; } // anchor vanished mid-drag; append rather than throw
        list.Insert(after ? index + 1 : index, item);
    }

    /// <summary>
    /// Positions an edge-dropped item BORDER-MOST among its edge+band peers, DevExpress-style: the
    /// new pane lands against the host border and existing content shifts inward. Slot order runs
    /// top-to-bottom, so border-most means FIRST on near-side edges (Left/Top) and LAST on far-side
    /// ones (Right/Bottom). No peers means nothing to order against.
    /// </summary>
    private void MoveItemBorderMost(DockItem item, DockEdge edge, DockBand band)
    {
        var list = (ObservableCollection<DockItem>)Items;
        var peers = list.Where(i => !ReferenceEquals(i, item) && i.Edge == edge && i.Band == band).ToList();
        if (peers.Count == 0) return;

        list.Remove(item);
        if (edge.IsFarSide())
            list.Insert(list.IndexOf(peers[^1]) + 1, item);
        else
            list.Insert(list.IndexOf(peers[0]), item);
    }

    // ---- Floating-window drag-back-to-redock (XU-447 closing half) ------------------------------
    //
    // Grab a floating pane's WINDOW by its title bar and drag it over the host: the same guides an
    // in-host drag shows light up (edges, per-slot clusters), and releasing over one re-docks the
    // pane there. The OS owns the window-move gesture, so the host can't see it through pointer
    // events — IDockPlatformServices.TrackFloatingWindowDrag supplies the cursor stream and the
    // drop signal, and the library converts screen coords into overlay coords to reuse the whole
    // guide pipeline. A platform that can't track (the default) just never lights the guides; the
    // explicit "Dock back" button remains the fallback path everywhere.

    /// <summary>How far outside the host the cursor may sit while still counting as "over" it.</summary>
    private const double RedockEnterMargin = 24;

    private void OnFloatDragMoved(DockItem item, Windows.Graphics.PointInt32 screen)
    {
        if (!item.IsFloating) return; // stale callback racing a dock-back

        var mapped = ScreenToOverlay(screen);
        if (mapped is not { } pointer) return;

        var inside = pointer.X >= -RedockEnterMargin
            && pointer.Y >= -RedockEnterMargin
            && pointer.X <= _overlayLayer.ActualWidth + RedockEnterMargin
            && pointer.Y <= _overlayLayer.ActualHeight + RedockEnterMargin;

        if (_redockActive && ReferenceEquals(_dragItem, item))
        {
            if (inside)
            {
                UpdateDragTracking(pointer);
            }
            else
            {
                // Dragged away again — drop the guides; re-entering re-arms from scratch.
                ClearDragState();
                Rebuild();
            }
        }
        else if (_dragItem is null && inside)
        {
            _dragItem = item;
            _redockActive = true;
            _dragHoverEdge = DockEdge.None;
            _dropKind = DropKind.None;
            _hoverSlotIndex = -1;
            // No ghost: the floating window the user is holding IS the drag visual.
            _dragOrigin = pointer;
            _dragPointer = pointer;
            ShowDragGuides(item);
            UpdateDragTracking(pointer);
        }
    }

    private void OnFloatDragDropped(DockItem item, Windows.Graphics.PointInt32 screen)
    {
        if (!_redockActive || !ReferenceEquals(_dragItem, item)) return;

        var kind = _dropKind;
        var targetEdge = _dragHoverEdge;
        var targetBand = _dragHoverBand;
        var slotIndex = _hoverSlotIndex;
        ClearDragState();

        if (kind == DropKind.None)
        {
            Rebuild(); // dropped outside every guide — the pane stays floating, just clear the visuals
            return;
        }

        // Placement BEFORE the un-float: mutations on a still-floating item skip the docked grid, so
        // the one visible transition is DockItemBack landing the pane directly where it was dropped.
        ApplyDropPlacement(item, kind, targetEdge, targetBand, slotIndex);
        DockItemBack(item, reopen: true);
        PersistLayoutAsync();
        Console.WriteLine("[DockPerf] float-drag redock committed");
    }

    /// <summary>
    /// Maps an OS screen point into overlay coordinates — the inverse of TearOutToFloat's mapping,
    /// sharing its approximation (AppWindow.Position is the FRAME origin and the frame-to-client
    /// offset isn't exposed, so the title bar is estimated at ~32 DIP). The guide targets carry
    /// enough hit padding to absorb the error; exact conversion can ride the platform interface
    /// later if the estimate ever feels off.
    /// </summary>
    private Windows.Foundation.Point? ScreenToOverlay(Windows.Graphics.PointInt32 screen)
    {
        if (PlatformServices.HostWindow is not { } host) return null;

        try
        {
            var scale = XamlRoot?.RasterizationScale ?? 1.0;
            var overlayOrigin = _overlayLayer.TransformToVisual(null)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            var basePos = host.AppWindow.Position;
            return new Windows.Foundation.Point(
                (screen.X - basePos.X) / scale - overlayOrigin.X,
                (screen.Y - basePos.Y) / scale - overlayOrigin.Y - 32);
        }
        catch
        {
            return null; // host window mid-teardown; skip this sample
        }
    }

    // ---- Auto-hide (pushpin, XU-452) ------------------------------------------------------------
    //
    // Unpinning collapses a pane out of the docked grid to a tab on a thin rail at its edge; clicking
    // the tab slides the pane in OVER the document, light-dismissed on tap-away — the same overlay
    // presentation the narrow zone uses, triggered by a click instead of a width threshold. Re-pinning
    // from the slid-in header returns the pane to the grid.

    private DockItem? _autoHideFlyoutItem;

    // The open flyout's moving parts, kept so light-dismiss can play the slide-out before the rebuild
    // tears the overlay down. Any rebuild nulls these; the exit storyboard's Completed checks whether
    // its clip host is still parented before rebuilding, so a superseding flyout is never stomped.
    private Grid? _flyoutClipHost;
    private TranslateTransform? _flyoutSlide;
    private double _flyoutOffset;
    private bool _flyoutAxisX;

    private void AddRail(Grid target, List<DockItem> autoHidden, DockEdge edge, int column = -1, int row = -1)
    {
        var items = autoHidden.Where(i => i.Edge == edge).ToList();
        if (items.Count == 0) return;

        var vertical = edge.IsHorizontal(); // Left/Right rails run vertically
        var rail = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x10, 0x80, 0x80, 0x80)),
            Spacing = 4,
            // Flush on the OUTSIDE: no padding between the tab and the host's true edge, so the tab
            // reads as attached to the border (Russ's styling note), padded everywhere else.
            Padding = edge switch
            {
                DockEdge.Left => new Thickness(0, 2, 2, 2),
                DockEdge.Right => new Thickness(2, 2, 0, 2),
                DockEdge.Top => new Thickness(2, 0, 2, 2),
                _ => new Thickness(2, 2, 2, 0),
            },
        };

        foreach (var item in items)
            rail.Children.Add(BuildRailTab(item, edge));

        Place(rail, column, row);
        target.Children.Add(rail);
    }

    private Button BuildRailTab(DockItem item, DockEdge edge)
    {
        var vertical = edge.IsHorizontal();

        // Styled per Russ: square corners and NO outline on the outside (edge-facing) side — the tab
        // emerges from the host's border like a real auto-hide tab — rounded corners and a thin
        // outline on every other side.
        var tab = new Button
        {
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x18, 0x80, 0x80, 0x80)),
            // Darker than the first pass (0x30-alpha grey was near-invisible against the rail).
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x90, 0x55, 0x55, 0x55)),
            BorderThickness = edge switch
            {
                DockEdge.Left => new Thickness(0, 1, 1, 1),
                DockEdge.Right => new Thickness(1, 1, 0, 1),
                DockEdge.Top => new Thickness(1, 0, 1, 1),
                _ => new Thickness(1, 1, 1, 0),
            },
            CornerRadius = edge switch
            {
                DockEdge.Left => new CornerRadius(0, 8, 8, 0),
                DockEdge.Right => new CornerRadius(8, 0, 0, 8),
                DockEdge.Top => new CornerRadius(0, 0, 8, 8),
                _ => new CornerRadius(8, 8, 0, 0),
            },
        };
        ToolTipService.SetToolTip(tab, item.Title);

        var text = new TextBlock
        {
            Text = item.Title,
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (vertical)
        {
            // Rotated rail text, the way MasterDetailControl does its own rotated caption: hosted in a
            // CANVAS, "so nothing clips it" (its words). A Canvas measures children unconstrained, so
            // the TextBlock reports its NATURAL size — measuring it off-tree returned a bogus size on
            // Uno (fonts not yet resolved), which is why the previous lock-the-measured-size fix still
            // truncated ("nt Ed", twice). Sizing happens in SizeChanged, i.e. from REAL layout values.
            var canvas = new Microsoft.UI.Xaml.Controls.Canvas
            {
                // Provisional footprint until the first real layout pass replaces it.
                Width = 26,
                Height = 80,
            };
            canvas.Children.Add(text);

            text.SizeChanged += (_, e) =>
            {
                var w = e.NewSize.Width;
                var h = e.NewSize.Height;
                if (w <= 0 || h <= 0) return;

                canvas.Width = Math.Max(h + 10, 26);
                canvas.Height = w + 28;

                // Centre the unrotated box, then spin it about its centre — net effect: vertical text
                // centred in the tab, reading bottom-up like a VS auto-hide rail.
                Microsoft.UI.Xaml.Controls.Canvas.SetLeft(text, (canvas.Width - w) / 2);
                Microsoft.UI.Xaml.Controls.Canvas.SetTop(text, (canvas.Height - h) / 2);
                text.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                text.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = -90 };
            };

            tab.Content = canvas;
        }
        else
        {
            text.Margin = new Thickness(12, 3, 12, 3);
            tab.Content = new Grid { Height = 26, Children = { text } };
        }

        tab.Click += (_, _) => OpenAutoHideFlyout(item);
        return tab;
    }

    /// <summary>Slides an auto-hidden pane in over the document, with a light-dismiss scrim.</summary>
    private void OpenAutoHideFlyout(DockItem item)
    {
        // Already presenting this item: NO-OP rather than rebuild. Hosts re-assert "open" on every
        // selection change (the timeline calls SetPaneOpen(true) per click), and rebuilding the
        // flyout re-parents its live content each time — an editor's two-way bindings churn on the
        // reload and false-dirty its unsaved-changes gate (Russ hit a stack of three dialogs).
        if (ReferenceEquals(_autoHideFlyoutItem, item)) return;

        // Reuse the overlay layer; opening one flyout replaces whatever the layer held. Any rebuild
        // (pin toggled, layout change) clears the layer and thus closes the flyout — which is the
        // correct outcome for every one of those events.
        _overlayLayer.Children.Clear();
        _overlayLayer.IsHitTestVisible = true;
        _autoHideFlyoutItem = item;

        var scrim = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0, 0, 0)) };
        scrim.Tapped += (_, _) =>
        {
            // Cancelable: hosts gate the close here (unsaved-changes prompts) or take over the
            // outcome entirely; only an unvetoed tap runs the default slide-out.
            var dismiss = new FlyoutDismissEventArgs(item);
            FlyoutDismissRequested?.Invoke(this, dismiss);
            if (!dismiss.Cancel) CloseAutoHideFlyout();
        };
        _overlayLayer.Children.Add(scrim);

        // The chrome needs an OPAQUE container: docked panes sit on the host's own surface, but the
        // flyout hovers over live content, and the header's translucent wash let it bleed through
        // (Russ's screenshot). Pane-surface colour per the actual theme (the app-resource lookup this
        // replaces painted it white in dark mode); border on every side except the edge the flyout
        // slides from, matching the rail tab's treatment.
        var container = new Border
        {
            Background = ComponentTheme.Surface(ActualTheme),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x90, 0x55, 0x55, 0x55)),
            BorderThickness = item.Edge switch
            {
                DockEdge.Left => new Thickness(0, 1, 1, 1),
                DockEdge.Right => new Thickness(1, 1, 0, 1),
                DockEdge.Top => new Thickness(1, 0, 1, 1),
                _ => new Thickness(1, 1, 1, 0),
            },
            Child = BuildItemChrome(item),
        };

        // A presented pane is a pane the user is working in, so it gets the same drag-to-resize seam a
        // docked one has (Russ: "resize handle missing"). The splitter lives on the INNER side — the
        // edge facing the document — inside a wrapper that owns the extent and the slide transform, so
        // the handle rides in and out with the pane instead of hanging in mid-air over the scrim.
        var horizontal = item.Edge.IsHorizontal();
        var farSide = item.Edge.IsFarSide();
        var hostExtent = horizontal ? ActualWidth : ActualHeight;
        var extent = Math.Min(item.DockLength, hostExtent);

        var panel = new Grid
        {
            HorizontalAlignment = item.Edge switch
            {
                DockEdge.Right => HorizontalAlignment.Right,
                DockEdge.Left => HorizontalAlignment.Left,
                _ => HorizontalAlignment.Stretch,
            },
            VerticalAlignment = item.Edge switch
            {
                DockEdge.Top => VerticalAlignment.Top,
                DockEdge.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Stretch,
            },
        };

        if (horizontal) panel.Width = extent;
        else panel.Height = extent;

        // Track order follows the edge: a far-side pane (Right/Bottom) meets the document on its
        // leading side, a near-side one on its trailing side. The splitter's track measures to
        // nothing — its negative margins cancel its own thickness — so the pane keeps its full extent.
        var contentIndex = farSide ? 1 : 0;
        var splitterIndex = farSide ? 0 : 1;
        var tracks = new GridLength[2];
        tracks[contentIndex] = new GridLength(1, GridUnitType.Star);
        tracks[splitterIndex] = GridLength.Auto;

        foreach (var track in tracks)
        {
            if (horizontal) panel.ColumnDefinitions.Add(new ColumnDefinition { Width = track });
            else panel.RowDefinitions.Add(new RowDefinition { Height = track });
        }

        Place(container, horizontal ? contentIndex : -1, horizontal ? -1 : contentIndex);
        panel.Children.Add(container);

        if (item.CanResize)
        {
            var splitter = new DockSplitter(
                horizontal ? Orientation.Vertical : Orientation.Horizontal, ActualTheme, item.Edge);

            var min = item.MinLength;
            // Never let the clamp invert on a host narrower than the pane's own minimum.
            var max = Math.Max(min, Math.Min(item.MaxLength, hostExtent));

            // Same snapshot discipline as the docked seam: Dragged reports CUMULATIVE translation, so
            // re-reading the live extent each step would double-count the movement.
            var startExtent = extent;
            splitter.DragStarted += (_, _) => startExtent = horizontal ? panel.ActualWidth : panel.ActualHeight;

            splitter.Dragged += cumulative =>
            {
                var applied = farSide ? -cumulative : cumulative;
                var next = Math.Clamp(startExtent + applied, min, max);

                if (horizontal) panel.Width = next;
                else panel.Height = next;
            };

            splitter.DragCompleted += (_, _) =>
            {
                var final = horizontal ? panel.ActualWidth : panel.ActualHeight;
                item.SetDockLengthSilently(final);

                // The exit animation slides the pane its OWN extent back off-edge. Leaving this at the
                // opening width would strand a widened flyout part-way on screen when it closes.
                _flyoutOffset = farSide ? final + 1 : -(final + 1);
                PersistLayoutAsync();
            };

            Place(splitter, horizontal ? splitterIndex : -1, horizontal ? -1 : splitterIndex);
            panel.Children.Add(splitter);
        }

        // The panel starts FULLY off-edge and slides its whole extent (Russ: "animated to slide
        // in and out all the way, not fade"), so it needs a static clip around it — without one the
        // off-edge frames would draw outside the host, over whatever the app placed beside it.
        var clipHost = new Grid();
        clipHost.SizeChanged += (_, e) => clipHost.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
        };
        clipHost.Children.Add(panel);
        _overlayLayer.Children.Add(clipHost);

        var slide = new TranslateTransform();
        panel.RenderTransform = slide;

        var fromOffset = item.Edge switch
        {
            DockEdge.Left => -(extent + 1),  // +1 clears the border hairline too
            DockEdge.Right => extent + 1,
            DockEdge.Top => -(extent + 1),
            _ => extent + 1,
        };

        _flyoutClipHost = clipHost;
        _flyoutSlide = slide;
        _flyoutOffset = fromOffset;
        _flyoutAxisX = item.Edge.IsHorizontal();

        // One-shot entrance on a transient element: the Storyboard holding its final value is fine
        // because the container is rebuilt from scratch every open; the exit storyboard hands off
        // from the live animated value.
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var slideAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = fromOffset,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(250)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
            },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slideAnim, slide);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(
            slideAnim, _flyoutAxisX ? "X" : "Y");

        storyboard.Children.Add(slideAnim);
        storyboard.Begin();

        FlyoutOpened?.Invoke(this, item);
    }

    private void CloseAutoHideFlyout()
    {
        var clipHost = _flyoutClipHost;
        var slide = _flyoutSlide;
        var offset = _flyoutOffset;
        var axisX = _flyoutAxisX;

        _autoHideFlyoutItem = null;
        _flyoutClipHost = null;
        _flyoutSlide = null;

        // Already mid-exit (a second tap on the scrim) or nothing to animate — the running exit's
        // Completed will finish the job.
        if (clipHost is null || slide is null) return;

        // Slide back out the way it came, THEN rebuild. No From: the animation takes over from the
        // current animated value, so a tap-away mid-open reverses smoothly instead of snapping fully
        // open first.
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var slideOut = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = offset,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn,
            },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slideOut, slide);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(slideOut, axisX ? "X" : "Y");
        storyboard.Children.Add(slideOut);

        storyboard.Completed += (_, _) =>
        {
            // Only rebuild if this flyout is still what the overlay shows. A rebuild or a newer
            // flyout in the meantime already tore this one out (clip host orphaned) and owns the
            // overlay now.
            if (clipHost.Parent is not null)
                Rebuild(); // clears the overlay and restores whatever the narrow zone wants there
        };
        storyboard.Begin();
    }

    /// <summary>
    /// Unpins <paramref name="item"/> with a visible exit: a bitmap ghost of its chrome slides off
    /// toward its edge while the rebuild collapses the real pane to its rail tab (Russ: unpinning
    /// "should be animated to slide out of the way", not blink out).
    /// </summary>
    /// <remarks>
    /// A SNAPSHOT rather than the live element, deliberately: the pane's content must be free for the
    /// rail/flyout to reparent the instant the rebuild runs, and a live element riding an exit
    /// animation would still be parented when that happens. The ghost lives on the transient layer,
    /// which rebuilds don't clear. Where the snapshot can't be taken (platform limitation, or a
    /// header living in a floating window whose visual tree this host can't transform against), the
    /// unpin still happens — it just collapses instantly, which is exactly the old behaviour.
    /// </remarks>
    private async void AnimateUnpinSlideOut(DockItem item, FrameworkElement chrome)
    {
        var origin = default(Windows.Foundation.Point);
        double w = 0, h = 0;
        Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap? snapshot = null;
        try
        {
            origin = chrome.TransformToVisual(_transientLayer)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            w = chrome.ActualWidth;
            h = chrome.ActualHeight;
            if (w > 0 && h > 0)
            {
                snapshot = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                await snapshot.RenderAsync(chrome);
                if (snapshot.PixelWidth == 0) snapshot = null;
            }
        }
        catch
        {
            snapshot = null;
        }

        item.IsPinned = false; // triggers the rebuild that collapses the pane to its rail
        PersistLayoutAsync();

        if (snapshot is null) return;

        var ghost = new Image
        {
            Source = snapshot,
            Width = w,
            Height = h,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(origin.X, origin.Y, 0, 0),
        };
        var slide = new TranslateTransform();
        ghost.RenderTransform = slide;
        _transientLayer.Children.Add(ghost);

        // Far enough to fully leave the host from wherever the pane sat, not just its own footprint —
        // a pane docked inside a rail strip is a few pixels short of the border.
        var to = item.Edge switch
        {
            DockEdge.Left => -(origin.X + w),
            DockEdge.Right => Math.Max(0, _transientLayer.ActualWidth - origin.X),
            DockEdge.Top => -(origin.Y + h),
            _ => Math.Max(0, _transientLayer.ActualHeight - origin.Y),
        };

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var slideOut = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn,
            },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slideOut, slide);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(
            slideOut, item.Edge.IsHorizontal() ? "X" : "Y");
        storyboard.Children.Add(slideOut);
        storyboard.Completed += (_, _) => _transientLayer.Children.Remove(ghost);
        storyboard.Begin();
    }

    private void BuildOverlay(List<DockItem> overlaid)
    {
        if (overlaid.Count == 0) return;

        _overlayLayer.IsHitTestVisible = true;

        // Light-dismiss backdrop. Tapping it closes the overlaid panes, matching the tap-away behaviour
        // a narrow list/detail blade has today.
        var scrim = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0, 0, 0)),
        };
        scrim.Tapped += (_, _) =>
        {
            foreach (var i in overlaid) i.IsOpen = false;
        };
        _overlayLayer.Children.Add(scrim);

        foreach (var item in overlaid)
        {
            var chrome = BuildItemChrome(item);
            chrome.HorizontalAlignment = item.Edge switch
            {
                DockEdge.Right => HorizontalAlignment.Right,
                DockEdge.Left => HorizontalAlignment.Left,
                _ => HorizontalAlignment.Stretch,
            };
            chrome.VerticalAlignment = item.Edge switch
            {
                DockEdge.Top => VerticalAlignment.Top,
                DockEdge.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Stretch,
            };

            if (item.Edge.IsHorizontal()) chrome.Width = Math.Min(item.DockLength, ActualWidth);
            else chrome.Height = Math.Min(item.DockLength, ActualHeight);

            _overlayLayer.Children.Add(chrome);
        }
    }
}
