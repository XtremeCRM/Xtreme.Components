# Xtreme.Components.Docking

An IDE-style dock manager for [Uno Platform](https://platform.uno), built from WinUI
primitives only. Part of [Xtreme.Components](https://github.com/XtremeCRM/Xtreme.Components).

**Full guide:** [docs/docking.md](https://github.com/XtremeCRM/Xtreme.Components/blob/main/docs/docking.md) covers every option with samples.

## Features

- **Center document + edge panes.** `DockItem`s dock to any edge; each edge holds layers
  stacked outward and slots side by side within a layer, with tab groups anywhere.
- **Drag with guides.** Header and tab drags show edge markers and a document compass
  (inner and outer bands, insert before/after, tab into), with a shaded landing preview.
- **Auto-hide.** Pushpin collapses a pane to its edge rail; the flyout slides fully in and out.
  Below a configurable width panes auto-unpin (`DockNarrowBehavior.AutoHide`).
- **Float to an OS window.** On Windows desktop a pane tears out into a real window and drags back
  to re-dock; elsewhere (WASM, mobile, macOS, Linux) it stays in-surface. All of it goes through
  `IDockPlatformServices`. `Win32DockPlatformServices` is the shipped Windows implementation
  (window pool, owned windows, drag tracking, drag translucency), runtime-guarded so the same
  assembly runs everywhere; without it the host is docked-only.
- **Persistence.** The whole layout (edges, layers, tab groups and their selected tab, ratios,
  pinned/open state, floating windows and their bounds) is a versioned document saved through
  `IDockPersistence`; closes are saved too, and the store can be assigned at any time.
- **Theme-aware.** Follows `ActualTheme` live; high-contrast palettes override through the
  `Xc*` keys in `Xtreme.Components.Core`.

## Quick start

```xml
<Page xmlns:dock="using:Xtreme.Components.Docking.Controls">
  <dock:DockHost PersistenceKey="Main">
    <dock:DockHost.Document>
      <TextBox AcceptsReturn="True" />
    </dock:DockHost.Document>
    <dock:DockItem Title="Explorer" Edge="Left" AllowFloat="True">
      <TreeView />
    </dock:DockItem>
    <dock:DockItem Title="Properties" Edge="Right">
      <ListView />
    </dock:DockItem>
  </dock:DockHost>
</Page>
```

Optional seams, each with a no-op default:

| Seam | Interface | What the app supplies |
|---|---|---|
| Layout persistence | `IDockPersistence` | Load/save the layout document (local settings, a server, nothing) |
| OS windows | `IDockPlatformServices` | Create and track floating windows, window pool, drag tracking |

Set them on the host: `DockHost.Persistence` and `DockHost.PlatformServices`.

```csharp
// App.OnLaunched, once the main window exists:
var platform = new Win32DockPlatformServices { HostWindow = MainWindow };
host.PlatformServices = platform;
platform.CaptureShellWindowHandle(MainWindow.DispatcherQueue);   // owned float windows
platform.PrimePoolWhenIdle(MainWindow.DispatcherQueue);          // pre-built windows, off the click
```

Targets `net10.0` only; the consuming Uno head supplies the renderer. MIT license.
