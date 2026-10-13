# Docking

`Xtreme.Components.Docking` is an IDE-style dock manager for Uno Platform, built from WinUI
primitives. A `DockHost` holds one center document; `DockItem` panes dock around it, group into
tabs, sit side by side, collapse to auto-hide rails, and (on desktop) tear out into OS windows.
The layout can be saved and restored through an interface the app implements.

- Live gallery: <https://xtremecrm.github.io/Xtreme.Components/> (Docking page)
- Sample source: [DockingPage.cs](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/Pages/DockingPage.cs)
- Packaging and design rules: [architecture.md](architecture.md)
- Colors and high-contrast overrides: [theming.md](theming.md)

The package targets `net10.0` only; the consuming Uno head supplies the renderer.

Namespaces:

| Namespace | Contents |
|---|---|
| `Xtreme.Components.Docking` | `DockEdge`, `DockEdgeExtensions`, `IDockPersistence`, `IDockPlatformServices`, `DockedOnlyPlatformServices` |
| `Xtreme.Components.Docking.Controls` | `DockHost`, `DockItem`, `DockBand`, `DockNarrowBehavior`, `DockSplitter`, `FlyoutDismissEventArgs`, `DockItemCloseRequestedEventArgs` |
| `Xtreme.Components.Docking.Platform` | `Win32DockPlatformServices` |

## Contents

- [Model](#model)
- [Quick start](#quick-start)
- [Declaring items](#declaring-items)
- [Tab groups, layers and bands](#tab-groups-layers-and-bands)
- [Edge priority](#edge-priority)
- [Pinning, auto-hide rails and flyouts](#pinning-auto-hide-rails-and-flyouts)
- [Drag to re-dock](#drag-to-re-dock)
- [Floating windows](#floating-windows)
- [Narrow and responsive behavior](#narrow-and-responsive-behavior)
- [Persistence](#persistence)
- [Host and item events](#host-and-item-events)
- [Rebuilds reparent content](#rebuilds-reparent-content)
- [Closing panes and the restore pill](#closing-panes-and-the-restore-pill)
- [DockSplitter in your own layouts](#docksplitter-in-your-own-layouts)
- [Theming](#theming)
- [Member reference](#member-reference)

## Model

```
+-------------------------------------------------------------+
| Top (outer band)                                            |
+------+---------------------------------------------+--------+
| Left | Top (inner band)                            | Right  |
|      +---------------------------------------------+        |
|      |                                             |        |
|      |              Document (fixed slot)          |        |
|      |                                             |        |
+------+---------------------------------------------+--------+
| Bottom                                                      |
+-------------------------------------------------------------+
```

- **Document.** `DockHost.Document` is the center content. Its slot never moves; panes are arranged
  around it.
- **Items.** `DockHost.Items` is a flat list of `DockItem`s. Each item names one `Edge`
  (`Left`, `Right`, `Top`, `Bottom`). The structure is encoded on the items as plain properties, not
  as nested XAML elements, so a simple layout stays a flat list.
- **Region.** All open, pinned items on one edge and band form that edge's region. The region's
  extent along the edge axis is the largest `DockLength` among its items.
- **Slot.** A region is divided into slots. A slot is either one standalone item or a whole
  **tab group** (items sharing a `TabGroup` name).
- **Layer.** Slots are stacked in layers from the border inward. By default every slot is its own
  full-span layer. Slots sharing a `LayerGroup` name sit **side by side** in one layer.
- **Band.** Each edge has an `Outer` band (runs against the host border) and an `Inner` band (hugs
  the document and is sized to the document's extent). See `DockItem.Band`.
- **Splitters.** A seam with a grab handle sits between a region and the document, between layers,
  and between side-by-side slots.

## Quick start

XAML (`Items` is the content property, so `DockItem`s go directly inside the host):

```xml
<Page xmlns:dock="using:Xtreme.Components.Docking.Controls">
  <dock:DockHost x:Name="Host" PersistenceKey="Main">
    <dock:DockHost.Document>
      <TextBox AcceptsReturn="True" />
    </dock:DockHost.Document>

    <dock:DockItem DockId="explorer" Title="Explorer" Edge="Left" AllowFloat="True">
      <TreeView />
    </dock:DockItem>
    <dock:DockItem DockId="properties" Title="Properties" Edge="Right" DockLength="280">
      <ListView />
    </dock:DockItem>
  </dock:DockHost>
</Page>
```

The same in code, trimmed from the gallery page:

```csharp
using Xtreme.Components.Docking;
using Xtreme.Components.Docking.Controls;

var host = new DockHost
{
    PersistenceKey = "Gallery.Docking",
    Document = documentView,
    EdgePriority = [DockEdge.Right, DockEdge.Top, DockEdge.Bottom, DockEdge.Left],
};

host.Items.Add(new DockItem { DockId = "explorer", Title = "Explorer", Edge = DockEdge.Left,
    AllowedDockEdges = DockEdge.Horizontal, DockLength = 220, Content = explorerView });
host.Items.Add(new DockItem { DockId = "properties", Title = "Properties", Edge = DockEdge.Right,
    DockLength = 280, AllowFloat = true, Content = propertiesView });
host.Items.Add(new DockItem { DockId = "output", Title = "Output", Edge = DockEdge.Bottom,
    DockLength = 110, NarrowBehavior = DockNarrowBehavior.Hide, Content = outputView });
```

Two optional seams change behavior, each with a safe default:

| Property | Type | Default | Supply it to get |
|---|---|---|---|
| `DockHost.Persistence` | `IDockPersistence` | `null` (nothing saved) | Layout save/restore. See [Persistence](#persistence). |
| `DockHost.PlatformServices` | `IDockPlatformServices` | `DockedOnlyPlatformServices.Instance` | OS-window floating. See [Floating windows](#floating-windows). |

`PersistenceKey` alone does nothing; a `Persistence` implementation must also be set.

## Declaring items

`DockItem` derives from `ContentControl`; its `Content` is the pane body. Properties are grouped
below by purpose. Capability properties (`Allow*`, `Can*`, `Show*`) say what the user may do; state
properties (`Is*`, `Edge`, `DockLength`) say where the pane is now.

### Identity

| Property | Type | Default | Notes |
|---|---|---|---|
| `DockId` | `string?` | `null` | Stable identity for persistence. Author it; never derive it from position. Items without a `DockId` are not saved. |
| `Title` | `string?` | `null` | Header text, tab text, rail-tab text, float-window title, and restore-menu entry. |

### Placement

| Property | Type | Default | Notes |
|---|---|---|---|
| `Edge` | `DockEdge` | `Left` | Current edge. Exactly one value, not a combination. |
| `AllowedDockEdges` | `DockEdge` (flags) | `All` | Edges the user may move the pane to. Disallowed edges never show a drop guide, and a persisted edge outside this set is ignored on restore. |
| `Band` | `DockBand` | `Outer` | `Outer` runs against the host border; `Inner` hugs the document. |
| `TabGroup` | `string?` | `null` | Items on the same edge with the same name render as one tabbed slot. |
| `LayerGroup` | `string?` | `null` | Slots on the same edge with the same name sit side by side in one layer. |

### Size

| Property | Type | Default | Notes |
|---|---|---|---|
| `DockLength` | `double` | `280` | Width on `Left`/`Right`, height on `Top`/`Bottom`. One value so the extent survives a move between edges. |
| `MinLength` | `double` | `120` | Lower clamp for splitter drags and restored lengths. |
| `MaxLength` | `double` | `720` | Upper clamp (also limited by the host's own extent). |
| `CanResize` | `bool` | `true` | Whether a splitter is offered between the region and the document. A region shows one if any of its items allows it. |

### State and chrome

| Property | Type | Default | Notes |
|---|---|---|---|
| `IsOpen` | `bool` | `true` | Show/hide. Setting it is the normal programmatic open/close path. |
| `IsPinned` | `bool` | `true` | `false` collapses the pane to its edge rail (auto-hide). |
| `ShowPinToggle` | `bool` | `true` | Whether the header offers the pushpin. |
| `CanClose` | `bool` | `true` | Whether the header offers a close button. |
| `ShowHeader` | `bool` | `true` | `false` gives a chromeless region: no title bar, so no header drag, pin or close. |
| `NarrowBehavior` | `DockNarrowBehavior` | `AutoHide` | What the pane does below the narrow threshold. See [Narrow and responsive behavior](#narrow-and-responsive-behavior). |
| `AllowFloat` | `bool` | `false` | Whether the pane may tear out into an OS window. Ignored unless the platform supports floating. |
| `IsFloating` | `bool` (read-only) | `false` | Whether the pane currently lives in its own window. Only the host changes it. |

Every property except `DockId`, `Title`, `AllowedDockEdges`, `MinLength`, `MaxLength` and `CanClose`
triggers a host rebuild when it changes (see [Rebuilds reparent content](#rebuilds-reparent-content)).

### DockEdge

`DockEdge` is a `[Flags]` enum: `None = 0`, `Left = 1`, `Right = 2`, `Top = 4`, `Bottom = 8`, plus
the combinations `Horizontal` (`Left | Right`), `Vertical` (`Top | Bottom`) and `All`.

```csharp
// A list pane that only makes sense beside the document:
new DockItem { Title = "Explorer", Edge = DockEdge.Left, AllowedDockEdges = DockEdge.Horizontal };
```

`DockEdgeExtensions` has three helpers: `IsHorizontal()` (true for `Left`/`Right`, meaning
`DockLength` is a width), `IsFarSide()` (true for `Right`/`Bottom`, which lay out after the
document), and `FirstOrDefault(fallback)`, which reduces a flags value to a single edge.

## Tab groups, layers and bands

### Tab groups

Give items on the same edge the same `TabGroup` name. They render as one slot with a tab strip; the
tabs replace the per-item headers, and the pin and close buttons at the end of the strip act on the
selected tab.

```csharp
host.Items.Add(new DockItem { DockId = "filters", Title = "Filters", Edge = DockEdge.Left,
    TabGroup = "left-tools", DockLength = 220, Content = filtersView });
host.Items.Add(new DockItem { DockId = "map", Title = "Map", Edge = DockEdge.Left,
    TabGroup = "left-tools", DockLength = 220, AllowFloat = true, Content = mapView });
```

Clicking a tab selects it. Dragging a tab out of the strip ungroups it (see
[Drag to re-dock](#drag-to-re-dock)). Grouping is by name across the whole edge, so item order in
`Items` does not matter for membership. The selected tab is persisted; a group with no saved
selection shows its first visible tab.

### Layers and side-by-side slots

Without a `LayerGroup`, each slot on an edge is its own full-span layer, and layers stack (as rows,
on every edge) from the border inward with a splitter between each pair. Slots that share a
`LayerGroup` sit side by side within one layer, with splitters between them.

```csharp
// Editor and Map share one layer on the right edge, side by side.
host.Items.Add(new DockItem { DockId = "editor", Title = "Editor", Edge = DockEdge.Right,
    LayerGroup = "right-tools", Content = editorView });
host.Items.Add(new DockItem { DockId = "map", Title = "Map", Edge = DockEdge.Right,
    LayerGroup = "right-tools", Content = mapView });
```

Ratios between layers and column ratios between side-by-side slots inside a layer are both
persisted.

### Bands

`Band = DockBand.Outer` (the default) places the pane against the host border, spanning the extent
that `EdgePriority` grants its edge. `Band = DockBand.Inner` places it inside every outer band, sized
to the document. The host lays out inner bands first, then outer bands around them. Auto-hide rails
always sit on the true border, in the outer pass.

```csharp
// A find bar that spans only the document's width, inside the Left/Right panes:
new DockItem { Title = "Find", Edge = DockEdge.Top, Band = DockBand.Inner, DockLength = 48 };
```

## Edge priority

`DockHost.EdgePriority` (an `IReadOnlyList<DockEdge>`) decides which edges own the corners. The first
edge is laid out outermost and spans the host's full extent on its axis; each later edge fits
inside the earlier ones. Default: `[Top, Bottom, Left, Right]`, so toolbars and output panes span the
full width.

```csharp
// Give the Right pane full height, with Top and Bottom yielding to it:
host.EdgePriority = [DockEdge.Right, DockEdge.Top, DockEdge.Bottom, DockEdge.Left];
```

`EdgePriority` is a dependency property (`EdgePriorityProperty`), so it can be bound from XAML. There
is no string syntax for a literal list: set it from code, or bind it to an `IReadOnlyList<DockEdge>`.
Setting it rebuilds the layout; setting it to null restores the default.

## Pinning, auto-hide rails and flyouts

A pinned pane (`IsPinned = true`) occupies the docked grid. Clicking the header pushpin (or **Unpin
(auto-hide)** in the header's right-click menu) sets `IsPinned = false`: the pane slides off and
collapses to a tab on a thin rail at its edge.

Clicking a rail tab opens the pane as a **flyout**: it slides in over the document from its edge,
with a translucent scrim behind it. Tapping the scrim dismisses it (it slides back out to the rail),
subject to `FlyoutDismissRequested` (see [Host and item events](#host-and-item-events)). Clicking the
pushpin inside the flyout re-pins the pane into the grid. A resizable flyout (`CanResize`) has its
own splitter; the new length is written to `DockLength` and saved.

Open a flyout from code with `OpenFlyout`. It does nothing unless the item is open, not floating, and
currently presented as auto-hidden (unpinned, or collapsed by the narrow zone):

```csharp
host.OpenFlyout(editorItem);
```

Any rebuild closes an open flyout. Opening the flyout that is already showing is a no-op.

Set `ShowPinToggle = false` to remove the pushpin from the header, tab strip and context menu; you
can still change `IsPinned` from code.

## Drag to re-dock

Pane headers and tab-strip tabs are drag handles. A drag starts after 6 px of pointer movement, so a
plain click on a header button is never a drag. While dragging:

- A ghost of the header follows the pointer. The real pane does not move until release.
- **Edge markers** appear at the host border for each edge in the item's `AllowedDockEdges`.
- A **document compass** appears at the document's center with doubled arrows per direction: the
  inner arrow docks to the document's extent (`DockBand.Inner`), the outer arrow docks against the
  whole border (`DockBand.Outer`).
- Hovering a docked pane raises a **pane compass** over it: the center button joins it as a tab
  group; up and down insert a new layer above or below; left and right place the pane side by side
  in that pane's layer.
- Hovering any target shows a shaded landing preview.

On release:

| Released over | Result |
|---|---|
| An edge marker or compass button | The item moves there (`Edge`, `Band`, `TabGroup`, `LayerGroup` and item order are updated) and the layout is saved. |
| Open space, item has `AllowFloat` and the platform supports floating | The pane floats at the pointer. The ghost shows a "float" tag while this is the outcome. |
| Open space otherwise, or the pane's own slot | Nothing changes (the drag is canceled). |

Dragging a header or tab more than 32 DIP past the host's bounds also floats a floatable pane.

The right-click menu on a pane header offers **Float** (when floating is available and allowed),
**Pin**/**Unpin (auto-hide)** (when `ShowPinToggle`), and **Close** (when `CanClose`). Float is
reachable from the menu because a pane docked against the outer edge of the app cannot be dragged
32 DIP past the host.

A rebuild during an in-host drag (for example an `IsOpen` change from code) cancels that drag.

## Floating windows

### What the host needs

Floating is off unless you supply an `IDockPlatformServices` whose `SupportsFloatingWindows` is
true. The library ships one: `Win32DockPlatformServices`.

```csharp
using Xtreme.Components.Docking.Platform;

var platform = new Win32DockPlatformServices
{
    Log = line => System.Diagnostics.Debug.WriteLine(line), // optional diagnostics sink
};

var host = new DockHost { PlatformServices = platform /* , ... */ };

// Once the main window exists (App.OnLaunched, or the page's Loaded handler):
platform.HostWindow = mainWindow;                          // owner + screen-coordinate mapping
platform.CaptureShellWindowHandle(mainWindow.DispatcherQueue); // owned float windows
platform.PrimePoolWhenIdle(mainWindow.DispatcherQueue);        // pre-build windows off the click path
```

| Member | What it does | If you skip it |
|---|---|---|
| `HostWindow` | The app's main window. Used to map between in-host and screen coordinates (placing a torn-out window at the cursor, lighting guides during drag-back). | Floats still work but open at the OS default position, and drag-back-to-redock cannot map the cursor onto the host. |
| `CaptureShellWindowHandle(dispatcher)` | Polls until the main window's native handle exists, then caches it. Call while the main window is the only window. | Float windows are not owned by the main window, so clicking the main window can bury them. |
| `PrimePoolWhenIdle(dispatcher)` | Starts filling a pool of three spare windows once the user is idle. | The host also calls `WarmFloatSupport` whenever a floatable item is hosted, so the pool still fills, just later. |
| `Log` | Receives `[DockPerf]` / `[DockWin]` diagnostic lines, including the host's own float and re-dock timings. | Silent. The library never writes to the console. |

Set `PlatformServices` before the host loads. The gallery sets it in the constructor and assigns
`HostWindow` in `Loaded`.

### Behavior

- A pane floats by drag (past the host bounds, or released in open space) or from the header menu.
  The same `DockItem` instance moves into the new window, so its view state is kept. `IsFloating`
  becomes `true` and `Reparented` is raised.
- With `Win32DockPlatformServices` on Windows, a drag tear-out continues under the held button: the
  window follows the cursor until release.
- Dragging a floating window by its title bar back over the host lights the same edge markers and
  compasses; releasing over one re-docks the pane there. On Windows the window turns 30% opaque
  during the drag.
- Closing the floating window (its title-bar close) first raises `DockItem.CloseRequested` with
  `FromFloatingWindow = true`; setting `Cancel` keeps the window open. Otherwise the pane returns
  to the host closed (`IsOpen = false`) and the layout is saved; reopen it from the restore pill or
  by setting `IsOpen = true`.
- Setting `IsOpen = false` on a floating pane from code closes its window the same way.
- Floating state and the window's position and size are persisted (best effort). After a restart a
  previously floating pane floats again at its saved bounds; the position is skipped if it no
  longer lands on a connected display, and only the size is restored. A window closed by app
  shutdown rather than by its close button does not save, so the last saved document still has the
  pane floating.
- Float windows are owned by the main window (with `CaptureShellWindowHandle`), so a floating pane
  always stays above the main window.

### Platform support

`Win32DockPlatformServices` is safe to construct on every head; the Win32 calls are guarded at
runtime.

| Runtime | `SupportsFloatingWindows` | Behavior |
|---|---|---|
| Windows desktop | true | Full support: pool, owned windows, drag-carried tear-out, drag-back-to-redock, drag translucency, saved window bounds. |
| macOS / Linux desktop | false | In-surface only, like WebAssembly. Windows could open there, but without drag tracking a floating pane could only come back by closing its window, so floating is not offered. |
| WebAssembly, Android, iOS | false | Docked only. `AllowFloat` is ignored, no Float menu item appears, and a drag released in open space is canceled. Docking, tabs, layers, pinning, flyouts, responsive behavior and persistence all work. |

With no `PlatformServices` set, `DockedOnlyPlatformServices.Instance` is used: docked only on every
platform.

### Implementing IDockPlatformServices

Only `SupportsFloatingWindows` is required; every other member has a default interface
implementation that does nothing or returns null/false.

| Member | Contract |
|---|---|
| `SupportsFloatingWindows` | Whether a pane can live in its own OS window. |
| `HostWindow` | The window hosting the dock manager, or null. |
| `TryAcquireFloatWindow()` | Return a ready window, or null to make the host construct one. |
| `TryReleaseFloatWindow(window)` | Take back a freed window (content already detached). Return true only if you own it and it is verifiably off-screen; false makes the host close it. |
| `TrackFloatingWindowDrag(window, dragMoved, dropped)` | Report the OS title-bar drag in screen coordinates on the UI thread; dispose to stop. Do not report the gesture already in progress when the window spawns. Null disables drag-back. |
| `WarmFloatSupport(dispatcher)` | Hint that floating may happen soon. Must be safe to call repeatedly. |
| `BeginWindowDrag(window)` | Carry a just-spawned window under the still-held button. Return true only if you did, and then arm the drag tracker for it immediately. Do not use a native modal move loop. |
| `OnWindowSpawned(window)` | Apply window policy (ownership, placement) after each float window is activated. |

```csharp
public sealed class MyPlatform : IDockPlatformServices
{
    public bool SupportsFloatingWindows => OperatingSystem.IsWindows();
    public Window? HostWindow { get; set; }
}
```

## Narrow and responsive behavior

The host has two width zones with hysteresis:

| Transition | Threshold |
|---|---|
| Wide to narrow | host width below 580 |
| Narrow to wide | host width above 625 |
| First measurement | narrow if below 602.5 (the midpoint), otherwise wide |

Between 580 and 625 the host keeps its current zone, so a host resting on the boundary does not
flicker. `IsNarrow` reports the zone and `NarrowChanged` fires on each transition. The thresholds
are fixed.

In the narrow zone each pinned, open pane applies its `NarrowBehavior`:

| `DockNarrowBehavior` | Narrow-zone result |
|---|---|
| `AutoHide` (default) | Collapses to its rail tab, as if unpinned, without changing `IsPinned`. Widening restores it docked, and nothing about the change is persisted. |
| `Overlay` | Shown over the document, aligned to its edge, with a scrim. Tapping the scrim raises `FlyoutDismissRequested` once per overlaid pane (cancelable), then closes each pane that was not canceled (`IsOpen = false`, saved). |
| `Hide` | Not shown. The restore pill lists it (as "Show *Title*") while the host is narrow; choosing it, or calling `OpenFlyout`, shows the pane over the document as a flyout. Widening docks it again. |
| `Keep` | Stays docked and keeps taking space. |

Unpinned panes are on their rails in both zones; `NarrowBehavior` does not apply to them.

```csharp
host.Items.Add(new DockItem { Title = "Toolbar", Edge = DockEdge.Top, DockLength = 80,
    NarrowBehavior = DockNarrowBehavior.Keep, CanClose = false, Content = toolbar });
host.Items.Add(new DockItem { Title = "Output", Edge = DockEdge.Bottom, DockLength = 110,
    NarrowBehavior = DockNarrowBehavior.Hide, Content = output });

host.NarrowChanged += (_, _) => status.Text = host.IsNarrow ? "Narrow" : "Wide";
```

## Persistence

### Contract

```csharp
public interface IDockPersistence
{
    Task<string?> LoadAsync(string key, CancellationToken ct = default);
    Task SaveAsync(string key, string document, CancellationToken ct = default);
}
```

The host saves and loads one whole-layout document per `PersistenceKey`. Treat the document as an
opaque string. `LoadAsync` returns null when nothing is stored. Save is best-effort: the host
swallows exceptions from both methods, and an unreadable document falls back to the authored layout.

```csharp
public sealed class LocalSettingsDockPersistence : IDockPersistence
{
    private readonly Windows.Storage.ApplicationDataContainer _settings =
        Windows.Storage.ApplicationData.Current.LocalSettings;

    public Task<string?> LoadAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_settings.Values[$"Dock.{key}"] as string);

    public Task SaveAsync(string key, string document, CancellationToken ct = default)
    {
        _settings.Values[$"Dock.{key}"] = document;
        return Task.CompletedTask;
    }
}

var host = new DockHost
{
    PersistenceKey = "Orders.Dock",
    Persistence = new LocalSettingsDockPersistence(),
};
```

Use a distinct `PersistenceKey` per host; two hosts sharing a key overwrite each other's layouts.

### When it loads and saves

- **Load** happens once per store, in the host's `Loaded` handler, after the first build. Both
  `Persistence` and `PersistenceKey` are dependency properties: setting either after the host has
  loaded loads that store's layout at that point. A host that is unloaded and loaded again (a tab
  switch) keeps its in-session layout instead of reloading.
- Nothing is saved until the store's document has been read, so the authored layout can never
  overwrite a stored one first.
- **Save** happens immediately after a gesture that ends a deliberate action: a drop that moves a
  pane, a splitter drag (between the region and the document, between layers, or between
  side-by-side slots), pin or unpin, and re-docking a floating window.
- **Debounced save** (300 ms after the last change) follows every `IsOpen` change, whoever made
  it (the close button, a scrim dismiss, the restore menu, or code), a tab selection, floating a
  pane, and moving or resizing a floating window. A pending debounced save is flushed when the host
  unloads.
- Other programmatic property changes (`Edge`, `DockLength`, ...) do not save on their own; their
  state is written with the next save.

### What is stored

Per item with a `DockId`, in `Items` order: edge, `DockLength`, `IsOpen`, `TabGroup`, `IsPinned`,
the layer ratio, `Band`, `LayerGroup`, whether it is floating and its window bounds (physical
pixels), whether it is the selected tab of its group, and its column ratio inside a side-by-side
layer. Item order is restored too (it is the slot and layer order). The current format is version
6; documents written by versions 1 to 5 are still read, and the fields they lack keep the authored
values.

On restore:

- Records whose `DockId` matches no item are ignored. Items the document does not mention keep their
  relative order after the known ones.
- An edge outside the item's `AllowedDockEdges` is ignored.
- Lengths are clamped to `MinLength`..`MaxLength`.
- A floating record re-floats the pane only if it is open, still `AllowFloat`, and the platform
  supports floating windows; otherwise the pane is docked.

### RestoreItemOpenState

`DockHost.RestoreItemOpenState` (default `true`) controls whether the saved `IsOpen` is applied. Set it
to `false` for hosts whose panes are opened only by code (an editor opened by selecting a row, a map
toggled from a toolbar), so a pane that was open at shutdown does not open by itself at startup.
Edges, sizes, pin state and grouping still restore.

```csharp
var host = new DockHost
{
    PersistenceKey = "Scheduler.Dock",
    Persistence = persistence,
    RestoreItemOpenState = false,
    ShowClosedPaneRestore = false,
};
```

## Host and item events

| Event | Raised when | Notes |
|---|---|---|
| `DockHost.NarrowChanged` | The host crosses the narrow/wide threshold. | Read `IsNarrow`. |
| `DockHost.FlyoutOpened` (`EventHandler<DockItem>`) | An auto-hide flyout opens, from a rail tap or `OpenFlyout`. | List-style hosts use it to reset selection so re-picking the same row still registers. |
| `DockHost.FlyoutDismissRequested` (`EventHandler<FlyoutDismissEventArgs>`) | The user taps the scrim behind an auto-hide flyout or behind narrow-zone `Overlay` panes, before they close. | Cancelable. Raised once per overlaid pane. |
| `DockItem.CloseRequested` (`EventHandler<DockItemCloseRequestedEventArgs>`) | The user asks to close the item: the close button on its header or tab strip, the header menu's Close, or its floating window's own close button. | Cancelable: set `Cancel = true` to keep it open. Otherwise the host sets `IsOpen = false` and saves. `FromFloatingWindow` says which kind of close it was. Not raised when code sets `IsOpen`. |
| `DockItem.Reparented` | The item has moved into a different window: floated out, or docked back (including via the float window's close). | Read `IsFloating` and `IsOpen` for where it landed. Not raised by ordinary rebuilds. |
| `DockSplitter.DragStarted` / `Dragged` / `DragCompleted` | See [DockSplitter in your own layouts](#docksplitter-in-your-own-layouts). | |

### Gating a flyout dismiss

`FlyoutDismissEventArgs` derives from `CancelEventArgs`. Set `Cancel = true` to keep the flyout open,
for example while an unsaved-changes prompt is shown, or to replace the default slide-out with your
own outcome:

```csharp
host.FlyoutDismissRequested += async (_, e) =>
{
    if (e.Item != editorItem || !editor.HasUnsavedChanges) return;

    e.Cancel = true; // must be set synchronously, before the first await
    if (await ConfirmDiscardAsync())
        editorItem.IsOpen = false; // close the pane instead of sliding it back to the rail
};
```

### Gating a pane close

`DockItemCloseRequestedEventArgs` derives from `CancelEventArgs`, the same pattern:

```csharp
editorItem.CloseRequested += async (_, e) =>
{
    if (!editor.HasUnsavedChanges) return;

    e.Cancel = true; // synchronously, before the first await
    if (await ConfirmDiscardAsync())
        editorItem.IsOpen = false; // also closes its floating window, if it has one
};
```

### Resyncing content after a window move

Content that caches facts per `XamlRoot` (viewport size, rasterization scale) should refresh them in
`Reparented`:

```csharp
mapItem.Reparented += (_, _) => mapView.ResyncViewport();
```

## Rebuilds reparent content

The host rebuilds its visual tree wholesale on any layout-affecting change: a property change on an
item (see [Declaring items](#declaring-items)), an `Items` change, a tab switch, a pin toggle, a
drop, a zone change, a theme change, `ShowClosedPaneRestore` or `EdgePriority` changes. Each rebuild
detaches every `DockItem` (and the document) from its old parent panel and adds it to a freshly
built one. The instances are the same, so view state is kept, but the content is unloaded and
reloaded in the tree.

Consequences for content inside panes and the document:

- `Loaded` and `Unloaded` fire on every rebuild. Do not treat them as "the user opened/closed this".
- Two-way bindings can re-push values on reload, and selection controls can raise a spurious
  null-selection change. Do not treat these as user edits. A dirty flag set by such churn produces
  false "unsaved changes" prompts; compare current values against a saved baseline instead.
- `Reparented` is not raised for these rebuilds; it marks only moves between windows.
- Any rebuild closes an open auto-hide flyout and cancels an in-host drag in progress.

## Closing panes and the restore pill

The close button sets `IsOpen = false` (unless a `CloseRequested` handler cancels), and the close
is saved. Because `IsOpen` is persisted, a closed pane would otherwise stay closed across restarts.
While any item is closed, the host shows a small menu button at the top-right corner of the
document; it lists the closed panes by `Title` (or `DockId`), and choosing one reopens it where it
was and saves the layout.

Set `ShowClosedPaneRestore = false` when the app opens and closes panes from code and provides its own
way back (a toolbar toggle, a selection-driven editor):

```xml
<dock:DockHost ShowClosedPaneRestore="False" />
```

While the host is narrow, the same menu also lists panes hidden by `DockNarrowBehavior.Hide`, below
a separator, as "Show *Title*": they are still open, so choosing one shows the pane over the
document as a flyout rather than reopening it.

## DockSplitter in your own layouts

`DockSplitter` is public so app screens can use the same seam between their own resizable sections.
It renders as a thin line with a grab handle; its hit area straddles the seam through negative
margins, so place it in a zero-width (or zero-height) track and keep content clear of the handle.

```csharp
var splitter = new DockSplitter(Orientation.Vertical, ActualTheme, attachEdge: DockEdge.Left);
double start = 0;
splitter.DragStarted += (_, _) => start = leftColumn.ActualWidth;
splitter.Dragged += cumulative =>
    leftColumn.Width = Math.Clamp(start + cumulative, 120, 600); // cumulative since drag start
splitter.DragCompleted += (_, _) => SaveWidth(leftColumn.ActualWidth);
```

- `Orientation.Vertical` draws a vertical seam that resizes horizontally; `Horizontal` the reverse.
- `theme` is the owner's `ActualTheme`, used for the first frame because the splitter is built
  before it enters the tree. After that the splitter follows its own `ActualThemeChanged`, so a live
  light/dark switch repaints it without recreating it.
- `attachEdge` is the side of the pane that owns the seam; the handle sits flush against that pane.
  `DockEdge.None` (the default) centers the handle on the seam.
- `Dragged` reports cumulative movement since `DragStarted`, in DIPs. Apply `start + cumulative`;
  the sign is yours to interpret (a far-side pane inverts it).

## Theming

Merge the Core dictionary (or Primitives, which includes it) as described in
[theming.md, section 1](theming.md#1-merge-the-dictionaries). The dock chrome follows the host's
`ActualTheme` and rebuilds on a live light/dark switch.

- Pane surfaces, the flyout background and the restore pill use `ComponentTheme.Surface` /
  `SurfaceOutline`. A high-contrast palette overrides them through `XcHighContrast`,
  `XcSurfaceBrush` and `XcSurfaceOutlineBrush`; see
  [theming.md, Surface](theming.md#surface-code-painted-chrome-componenttheme).
- Guide overlays, drag ghosts, scrims, rail tints and hover washes are translucent colors painted in
  code and are not tokenized yet; see [theming.md, Still hard-coded](theming.md#4-still-hard-coded).
- Header and rail glyphs use `FontIcon` with the theme's symbol font.

## Member reference

Generated plumbing (`*Property` dependency-property fields) is omitted; every listed dependency
property has a matching `<Name>Property` field.

### DockHost (`Xtreme.Components.Docking.Controls`, derives from `ContentControl`)

Content property: `Items`.

| Member | Kind | Type / signature | Default |
|---|---|---|---|
| `Document` | DP | `object?` | `null` |
| `Items` | property (get) | `IList<DockItem>` | empty |
| `PersistenceKey` | DP | `string?` | `null` |
| `Persistence` | DP | `IDockPersistence?` | `null` |
| `PlatformServices` | property | `IDockPlatformServices` | `DockedOnlyPlatformServices.Instance` |
| `EdgePriority` | DP | `IReadOnlyList<DockEdge>` | `[Top, Bottom, Left, Right]` |
| `IsNarrow` | property (get) | `bool` | |
| `ShowClosedPaneRestore` | DP | `bool` | `true` |
| `RestoreItemOpenState` | property | `bool` | `true` |
| `OpenFlyout(DockItem item)` | method | `void` | |
| `NarrowChanged` | event | `EventHandler?` | |
| `FlyoutOpened` | event | `EventHandler<DockItem>?` | |
| `FlyoutDismissRequested` | event | `EventHandler<FlyoutDismissEventArgs>?` | |

### DockItem (`Xtreme.Components.Docking.Controls`, derives from `ContentControl`)

| Member | Kind | Type | Default |
|---|---|---|---|
| `DockId` | DP | `string?` | `null` |
| `Title` | DP | `string?` | `null` |
| `Edge` | DP | `DockEdge` | `Left` |
| `AllowedDockEdges` | DP | `DockEdge` | `All` |
| `Band` | DP | `DockBand` | `Outer` |
| `TabGroup` | DP | `string?` | `null` |
| `LayerGroup` | DP | `string?` | `null` |
| `DockLength` | DP | `double` | `280` |
| `MinLength` | DP | `double` | `120` |
| `MaxLength` | DP | `double` | `720` |
| `CanResize` | DP | `bool` | `true` |
| `IsOpen` | DP | `bool` | `true` |
| `IsPinned` | DP | `bool` | `true` |
| `ShowPinToggle` | DP | `bool` | `true` |
| `CanClose` | DP | `bool` | `true` |
| `ShowHeader` | DP | `bool` | `true` |
| `NarrowBehavior` | DP | `DockNarrowBehavior` | `AutoHide` |
| `AllowFloat` | DP | `bool` | `false` |
| `IsFloating` | DP (get; set is internal) | `bool` | `false` |
| `CloseRequested` | event | `EventHandler<DockItemCloseRequestedEventArgs>?` | |
| `Reparented` | event | `EventHandler?` | |

### DockSplitter (`Xtreme.Components.Docking.Controls`, sealed, derives from `Grid`)

| Member | Kind | Type / signature |
|---|---|---|
| `DockSplitter(Orientation orientation, ElementTheme theme, DockEdge attachEdge = DockEdge.None)` | constructor | |
| `DragStarted` | event | `EventHandler?` |
| `Dragged` | event | `Action<double>?` (cumulative DIPs) |
| `DragCompleted` | event | `EventHandler?` |

### FlyoutDismissEventArgs (`Xtreme.Components.Docking.Controls`, sealed, derives from `CancelEventArgs`)

| Member | Kind | Type |
|---|---|---|
| `FlyoutDismissEventArgs(DockItem item)` | constructor | |
| `Item` | property (get) | `DockItem` |
| `Cancel` | property (inherited) | `bool` |

### DockItemCloseRequestedEventArgs (`Xtreme.Components.Docking.Controls`, sealed, derives from `CancelEventArgs`)

| Member | Kind | Type |
|---|---|---|
| `DockItemCloseRequestedEventArgs(DockItem item, bool fromFloatingWindow)` | constructor | |
| `Item` | property (get) | `DockItem` |
| `FromFloatingWindow` | property (get) | `bool` |
| `Cancel` | property (inherited) | `bool` |

### Enums

| Enum | Values |
|---|---|
| `DockEdge` (`Xtreme.Components.Docking`, `[Flags]`) | `None = 0`, `Left = 1`, `Right = 2`, `Top = 4`, `Bottom = 8`, `Horizontal = Left \| Right`, `Vertical = Top \| Bottom`, `All` |
| `DockBand` (`...Docking.Controls`) | `Outer = 0`, `Inner = 1` |
| `DockNarrowBehavior` (`...Docking.Controls`) | `Overlay = 0`, `Hide = 1`, `Keep = 2`, `AutoHide = 3` |

### DockEdgeExtensions (`Xtreme.Components.Docking`, static)

| Member | Signature |
|---|---|
| `IsHorizontal` | `bool IsHorizontal(this DockEdge edge)` |
| `IsFarSide` | `bool IsFarSide(this DockEdge edge)` |
| `FirstOrDefault` | `DockEdge FirstOrDefault(this DockEdge edges, DockEdge fallback = DockEdge.Left)` |

### IDockPersistence (`Xtreme.Components.Docking`)

| Member | Signature |
|---|---|
| `LoadAsync` | `Task<string?> LoadAsync(string key, CancellationToken ct = default)` |
| `SaveAsync` | `Task SaveAsync(string key, string document, CancellationToken ct = default)` |

### IDockPlatformServices (`Xtreme.Components.Docking`)

| Member | Signature | Default implementation |
|---|---|---|
| `SupportsFloatingWindows` | `bool { get; }` | required |
| `HostWindow` | `Window? { get; }` | `null` |
| `TryAcquireFloatWindow` | `Window? TryAcquireFloatWindow()` | `null` |
| `TryReleaseFloatWindow` | `bool TryReleaseFloatWindow(Window window)` | `false` |
| `TrackFloatingWindowDrag` | `IDisposable? TrackFloatingWindowDrag(Window window, Action<PointInt32> dragMoved, Action<PointInt32> dropped)` | `null` |
| `WarmFloatSupport` | `void WarmFloatSupport(DispatcherQueue dispatcher)` | no-op |
| `BeginWindowDrag` | `bool BeginWindowDrag(Window window)` | `false` |
| `OnWindowSpawned` | `void OnWindowSpawned(Window window)` | no-op |

### DockedOnlyPlatformServices (`Xtreme.Components.Docking`, sealed)

| Member | Kind | Type |
|---|---|---|
| `Instance` | static readonly field | `DockedOnlyPlatformServices` |
| `SupportsFloatingWindows` | property (get) | `bool` (always `false`) |

### Win32DockPlatformServices (`Xtreme.Components.Docking.Platform`, sealed, implements `IDockPlatformServices`)

| Member | Kind | Type / signature |
|---|---|---|
| `Win32DockPlatformServices()` | constructor | |
| `HostWindow` | property | `Window?` (settable) |
| `Log` | property | `Action<string>?` |
| `SupportsFloatingWindows` | property (get) | `bool` (true on Windows only) |
| `CaptureShellWindowHandle` | method | `void CaptureShellWindowHandle(DispatcherQueue dispatcher)` |
| `PrimePoolWhenIdle` | method | `void PrimePoolWhenIdle(DispatcherQueue dispatcher)` |
| `WarmFloatSupport` | method | `void WarmFloatSupport(DispatcherQueue dispatcher)` |
| `TryAcquireFloatWindow` | method | `Window? TryAcquireFloatWindow()` |
| `TryReleaseFloatWindow` | method | `bool TryReleaseFloatWindow(Window window)` |
| `TrackFloatingWindowDrag` | method | `IDisposable? TrackFloatingWindowDrag(Window, Action<PointInt32>, Action<PointInt32>)` (null off Windows) |
| `BeginWindowDrag` | method | `bool BeginWindowDrag(Window window)` (false off Windows) |
| `OnWindowSpawned` | method | `void OnWindowSpawned(Window window)` (no-op off Windows) |
