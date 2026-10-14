using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Xtreme.Components.Docking.Controls;

/// <summary>
/// How a <see cref="DockItem"/> behaves when the host becomes too narrow to show it beside the document.
/// </summary>
public enum DockNarrowBehavior
{
    /// <summary>
    /// Slide over the document instead of beside it, dismissing on tap-away (subject to
    /// <see cref="DockHost.FlyoutDismissRequested"/>). The document keeps its full width.
    /// </summary>
    Overlay,

    /// <summary>
    /// Collapse entirely. While the host is narrow, its restore menu lists the pane (unless
    /// <see cref="DockHost.ShowClosedPaneRestore"/> is off) and <see cref="DockHost.OpenFlyout"/> shows
    /// it over the document.
    /// </summary>
    Hide,

    /// <summary>Stay docked and keep taking space. For panes that must never disappear.</summary>
    Keep,

    /// <summary>
    /// Collapse to the pane's auto-hide rail tab — the VS-style narrow story, and the default.
    /// Zone-driven presentation only:
    /// <see cref="DockItem.IsPinned"/> is never mutated, so widening the host restores the pane
    /// docked exactly as it was, and nothing persists an unpin the user didn't ask for. Every pane
    /// stays one rail-tab tap away, which beats a modal overlay that hides them all.
    /// </summary>
    AutoHide,
}

/// <summary>
/// Which band of its edge a docked pane occupies — the outer/inner distinction behind the doubled
/// compass buttons: the outer one docks against the entire border, the inner one only along the
/// document's extent.
/// </summary>
public enum DockBand
{
    /// <summary>
    /// The edge's outer band: the pane runs against the host's border, spanning the full extent
    /// <see cref="Controls.DockHost.EdgePriority"/> grants that edge. The default, and the only band
    /// that existed before the doubled compass.
    /// </summary>
    Outer,

    /// <summary>
    /// The edge's inner band: the pane hugs the center document, sized to the document's extent —
    /// inside every outer band, whatever the edge priorities say.
    /// </summary>
    Inner,
}

/// <summary>
/// One dockable pane: content plus the rules about where it may live.
/// </summary>
/// <remarks>
/// <para><b>Capability vs. state.</b> Properties that grant a permission read as <c>Allow*</c>
/// (<see cref="AllowFloat"/>, <see cref="AllowedDockEdges"/>, <see cref="CanResize"/>); properties that
/// report the current situation read as <c>Is*</c> (<see cref="IsFloating"/>, <see cref="IsOpen"/>).
/// Keeping those apart matters here because a pane can be permitted to float while not currently
/// floating, and the two must never collapse into one flag.</para>
/// <para><b>⚠️ "Float" means an OS window and nothing else.</b> The control this replaces used
/// <c>FloatPane</c> to mean "overlay the pane over the content", which is a completely different idea
/// and is spelled <see cref="DockNarrowBehavior.Overlay"/> here. That old meaning is exactly the
/// narrow/mobile responsive mode, so conflating the two would be actively dangerous — on WASM, where
/// floating is impossible, overlay is the mode that must still work.</para>
/// </remarks>
public partial class DockItem : ContentControl
{
    public DockItem()
    {
        // No default style/template of its own yet: DockHost composes the header chrome around the item
        // and the inherited ContentControl template renders Content. When docking grows tab groups, this
        // is the natural place for a real templated header.
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    // ---- Identity -------------------------------------------------------------------------------

    /// <summary>
    /// Stable identity for layout save/restore — AvalonDock's <c>ContentId</c>. Must survive the item
    /// moving between edges, groups and windows, so it is authored, never derived from position.
    /// </summary>
    public string? DockId
    {
        get => (string?)GetValue(DockIdProperty);
        set => SetValue(DockIdProperty, value);
    }

    public static readonly DependencyProperty DockIdProperty = DependencyProperty.Register(
        nameof(DockId), typeof(string), typeof(DockItem), new PropertyMetadata(null));

    /// <summary>Text shown in the pane header and, later, on its tab.</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DockItem), new PropertyMetadata(null));

    // ---- Placement ------------------------------------------------------------------------------

    /// <summary>Which edge the item currently occupies. Exactly one edge, never a set.</summary>
    public DockEdge Edge
    {
        get => (DockEdge)GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    public static readonly DependencyProperty EdgeProperty = DependencyProperty.Register(
        nameof(Edge), typeof(DockEdge), typeof(DockItem),
        new PropertyMetadata(DockEdge.Left, OnLayoutAffectingPropertyChanged));

    /// <summary>
    /// The edges this item may be moved to. Defaults to <see cref="DockEdge.All"/>. A pane that only
    /// makes sense beside the document — a list in a list/detail blade — should narrow this.
    /// </summary>
    public DockEdge AllowedDockEdges
    {
        get => (DockEdge)GetValue(AllowedDockEdgesProperty);
        set => SetValue(AllowedDockEdgesProperty, value);
    }

    public static readonly DependencyProperty AllowedDockEdgesProperty = DependencyProperty.Register(
        nameof(AllowedDockEdges), typeof(DockEdge), typeof(DockItem),
        new PropertyMetadata(DockEdge.All));

    /// <summary>Which band of its edge the pane occupies — see <see cref="DockBand"/>.</summary>
    public DockBand Band
    {
        get => (DockBand)GetValue(BandProperty);
        set => SetValue(BandProperty, value);
    }

    public static readonly DependencyProperty BandProperty = DependencyProperty.Register(
        nameof(Band), typeof(DockBand), typeof(DockItem),
        new PropertyMetadata(DockBand.Outer, OnLayoutAffectingPropertyChanged));

    /// <summary>
    /// Optional layer name. A region stacks LAYERS vertically; slots sharing a name on the same edge
    /// sit SIDE-BY-SIDE inside one layer (the compass's ←/→ drops). Null (the default) gives the
    /// slot its own full-span layer — which is exactly the pre-layer behavior, so existing layouts
    /// read unchanged. Same flat-list encoding idea as <see cref="TabGroup"/>, one level up.
    /// </summary>
    public string? LayerGroup
    {
        get => (string?)GetValue(LayerGroupProperty);
        set => SetValue(LayerGroupProperty, value);
    }

    public static readonly DependencyProperty LayerGroupProperty = DependencyProperty.Register(
        nameof(LayerGroup), typeof(string), typeof(DockItem),
        new PropertyMetadata(null, OnLayoutAffectingPropertyChanged));

    /// <summary>
    /// Optional tab-group name. Items on the SAME edge sharing a name render as one tabbed slot — a tab
    /// strip selects the visible item — instead of stacking adjacently. Null (the default) keeps the
    /// item standalone.
    /// </summary>
    /// <remarks>
    /// A string on the item rather than a nested group element in XAML, deliberately: the flat `Items`
    /// collection is what keeps a plain list/detail blade a one-liner, and tabbing must stay opt-in
    /// without restructuring the markup. The recursive-tree model lives in the layout logic, not the
    /// authoring surface.
    /// </remarks>
    public string? TabGroup
    {
        get => (string?)GetValue(TabGroupProperty);
        set => SetValue(TabGroupProperty, value);
    }

    public static readonly DependencyProperty TabGroupProperty = DependencyProperty.Register(
        nameof(TabGroup), typeof(string), typeof(DockItem),
        new PropertyMetadata(null, OnLayoutAffectingPropertyChanged));

    // ---- Sizing ---------------------------------------------------------------------------------

    /// <summary>
    /// Size along the edge's axis: a WIDTH on Left/Right, a HEIGHT on Top/Bottom. One property rather
    /// than Width+Height because an item keeps its extent conceptually as it moves between edges.
    /// </summary>
    public double DockLength
    {
        get => (double)GetValue(DockLengthProperty);
        set => SetValue(DockLengthProperty, value);
    }

    public static readonly DependencyProperty DockLengthProperty = DependencyProperty.Register(
        nameof(DockLength), typeof(double), typeof(DockItem),
        new PropertyMetadata(280d, OnLayoutAffectingPropertyChanged));

    public double MinLength
    {
        get => (double)GetValue(MinLengthProperty);
        set => SetValue(MinLengthProperty, value);
    }

    public static readonly DependencyProperty MinLengthProperty = DependencyProperty.Register(
        nameof(MinLength), typeof(double), typeof(DockItem), new PropertyMetadata(120d));

    public double MaxLength
    {
        get => (double)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength), typeof(double), typeof(DockItem), new PropertyMetadata(720d));

    /// <summary>Whether the user may drag the splitter between this item and the document.</summary>
    public bool CanResize
    {
        get => (bool)GetValue(CanResizeProperty);
        set => SetValue(CanResizeProperty, value);
    }

    public static readonly DependencyProperty CanResizeProperty = DependencyProperty.Register(
        nameof(CanResize), typeof(bool), typeof(DockItem),
        new PropertyMetadata(true, OnLayoutAffectingPropertyChanged));

    // ---- State ----------------------------------------------------------------------------------

    /// <summary>
    /// Whether the pane is currently showing. Toggling this is the normal show/hide path. The owning host
    /// saves the layout after every change (debounced), so a pane closed from code stays closed.
    /// </summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(DockItem),
        new PropertyMetadata(true, OnIsOpenChanged));

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        OnLayoutAffectingPropertyChanged(d, e);
        ((DockItem)d).IsOpenChanged?.Invoke(d, EventArgs.Empty);
    }

    /// <summary>What happens to this pane when the host is too narrow to show it beside the document.</summary>
    public DockNarrowBehavior NarrowBehavior
    {
        get => (DockNarrowBehavior)GetValue(NarrowBehaviorProperty);
        set => SetValue(NarrowBehaviorProperty, value);
    }

    public static readonly DependencyProperty NarrowBehaviorProperty = DependencyProperty.Register(
        nameof(NarrowBehavior), typeof(DockNarrowBehavior), typeof(DockItem),
        new PropertyMetadata(DockNarrowBehavior.AutoHide, OnLayoutAffectingPropertyChanged));

    /// <summary>Whether the header offers a close button.</summary>
    public bool CanClose
    {
        get => (bool)GetValue(CanCloseProperty);
        set => SetValue(CanCloseProperty, value);
    }

    public static readonly DependencyProperty CanCloseProperty = DependencyProperty.Register(
        nameof(CanClose), typeof(bool), typeof(DockItem), new PropertyMetadata(true));

    /// <summary>CAPABILITY: whether the header offers the pushpin (auto-hide) toggle.</summary>
    public bool ShowPinToggle
    {
        get => (bool)GetValue(ShowPinToggleProperty);
        set => SetValue(ShowPinToggleProperty, value);
    }

    public static readonly DependencyProperty ShowPinToggleProperty = DependencyProperty.Register(
        nameof(ShowPinToggle), typeof(bool), typeof(DockItem),
        new PropertyMetadata(true, OnLayoutAffectingPropertyChanged));

    /// <summary>
    /// STATE: pinned (default) keeps the pane in the docked grid; unpinned collapses it to a tab on a
    /// thin rail at its edge — clicking the tab slides it in over the document, light-dismissed on
    /// tap-away (VS-style auto-hide).
    /// </summary>
    public bool IsPinned
    {
        get => (bool)GetValue(IsPinnedProperty);
        set => SetValue(IsPinnedProperty, value);
    }

    public static readonly DependencyProperty IsPinnedProperty = DependencyProperty.Register(
        nameof(IsPinned), typeof(bool), typeof(DockItem),
        new PropertyMetadata(true, OnLayoutAffectingPropertyChanged));

    /// <summary>Whether the header shows a title bar at all. Off gives a chromeless docked region.</summary>
    public bool ShowHeader
    {
        get => (bool)GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.Register(
        nameof(ShowHeader), typeof(bool), typeof(DockItem),
        new PropertyMetadata(true, OnLayoutAffectingPropertyChanged));

    // ---- Floating -------------------------------------------------------------------------------

    /// <summary>
    /// CAPABILITY: may the user tear this pane out into its own OS window? Honored only where
    /// <see cref="IDockPlatformServices.SupportsFloatingWindows"/> is true — on WASM and mobile this is
    /// ignored and no float affordance appears.
    /// </summary>
    public bool AllowFloat
    {
        get => (bool)GetValue(AllowFloatProperty);
        set => SetValue(AllowFloatProperty, value);
    }

    public static readonly DependencyProperty AllowFloatProperty = DependencyProperty.Register(
        nameof(AllowFloat), typeof(bool), typeof(DockItem),
        new PropertyMetadata(false, OnLayoutAffectingPropertyChanged));

    /// <summary>
    /// STATE: is the pane currently living in its own window? Read-only to callers — the transition is
    /// owned by <see cref="DockHost"/> as a single state machine, precisely so float and re-dock cannot
    /// drift into two half-aware code paths that disagree about whether the docked slot is open.
    /// </summary>
    public bool IsFloating
    {
        get => (bool)GetValue(IsFloatingProperty);
        internal set => SetValue(IsFloatingProperty, value);
    }

    public static readonly DependencyProperty IsFloatingProperty = DependencyProperty.Register(
        nameof(IsFloating), typeof(bool), typeof(DockItem),
        new PropertyMetadata(false, OnLayoutAffectingPropertyChanged));

    // ---- Events ---------------------------------------------------------------------------------

    /// <summary>
    /// Raised when the user asks to close the pane: its header or tab-strip close button, the header
    /// menu's Close, or its floating window's own close button. Set
    /// <see cref="System.ComponentModel.CancelEventArgs.Cancel"/> to keep the pane open. Not raised when
    /// code sets <see cref="IsOpen"/>.
    /// </summary>
    public event EventHandler<DockItemCloseRequestedEventArgs>? CloseRequested;

    /// <returns>True when no handler canceled the close.</returns>
    internal bool RaiseCloseRequested(bool fromFloatingWindow = false)
    {
        var args = new DockItemCloseRequestedEventArgs(this, fromFloatingWindow);
        CloseRequested?.Invoke(this, args);
        return !args.Cancel;
    }

    /// <summary>
    /// Raised after the item has crossed into a different Window — floated out, or docked back. Read
    /// <see cref="IsFloating"/> for the direction. NOT raised on ordinary layout rebuilds (tab switch,
    /// responsive zone change), which keep the item under the same <c>XamlRoot</c>.
    /// </summary>
    /// <remarks>
    /// This is the formal hook for content that caches per-<c>XamlRoot</c> facts and needs a resync
    /// after a move. A map control is the typical case: its navigator can keep the old viewport size,
    /// because moving between windows doesn't reliably raise <c>SizeChanged</c>, and a cached
    /// rasterization scale carried from the old window would convert pointer DIPs against the wrong
    /// factor. Reparenting deliberately preserves view state
    /// (that is its point); this event is where content refreshes the state it should NOT have kept.
    /// </remarks>
    public event EventHandler? Reparented;

    internal void RaiseReparented() => Reparented?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The pane's floating window frame the last time it floated (physical screen pixels), kept after it
    /// docks again so the next float reopens where and how big the user left it. Saved with the layout.
    /// </summary>
    internal DockWindowBounds? LastFloatBounds { get; set; }

    // ---- Layout invalidation --------------------------------------------------------------------

    /// <summary>
    /// Anything the owning host must re-lay-out for routes through here. The host subscribes rather than
    /// the item calling back directly, so an item is usable standalone (in a designer, or a test).
    /// </summary>
    internal event EventHandler? LayoutAffectingPropertyChanged;

    /// <summary>Raised on every <see cref="IsOpen"/> change; the host saves the layout from it.</summary>
    internal event EventHandler? IsOpenChanged;

    private bool _suppressLayoutNotification;

    private static void OnLayoutAffectingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var item = (DockItem)d;
        if (item._suppressLayoutNotification) return;
        item.LayoutAffectingPropertyChanged?.Invoke(d, EventArgs.Empty);
    }

    /// <summary>
    /// Records a new <see cref="DockLength"/> without asking the host to rebuild.
    /// </summary>
    /// <remarks>
    /// Used when the host has ALREADY applied the size directly to the live region — at the end of a
    /// splitter drag, for instance. Going through the normal setter there would rebuild the grid to
    /// reach a layout it is already in, discarding the element the user just interacted with.
    /// </remarks>
    internal void SetDockLengthSilently(double length)
    {
        _suppressLayoutNotification = true;
        try
        {
            DockLength = length;
        }
        finally
        {
            _suppressLayoutNotification = false;
        }
    }
}
