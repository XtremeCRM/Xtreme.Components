# Xtreme.Components

App-agnostic, cross-platform [Uno Platform](https://platform.uno) controls, built from WinUI
primitives only: no WebView, no third-party control licences. One `net10.0` assembly per
package runs on Desktop, Android, iOS and WebAssembly through the Uno Skia renderer.

They grew up inside [XtremeCRM](https://github.com/XtremeCRM/XtremeCRM.Uno) and were
extracted so anyone can pick up a single control in the standard way:

```bash
dotnet add package Xtreme.Components.Docking --prerelease
```

## Packages

| Package | What it is | Depends on |
|---|---|---|
| `Xtreme.Components.Core` | Theme tokens and the `ActualTheme`-driven surface helper every family uses. No controls. | – |
| `Xtreme.Components.Primitives` | Input, layout and list helpers: masked text, time and date pickers, paging, panels, toast, action bar, chip picker. | Core |
| `Xtreme.Components.Scheduler` | Day / Week / Month / Agenda and multi-resource Planner views with drag-to-move, resize, click-to-create and external drag-drop. | Primitives |
| `Xtreme.Components.Docking` | Dock manager: dockable panes, tab groups, auto-hide rails, float to a real OS window and re-dock, layout persistence. | Core |
| `Xtreme.Components.Cards` | `SectionCard` frame with tab strip, and `CardDockerControl` for arranging cards across a column grid by dragging. | Core |
| `Xtreme.Components.TableView` | `RowFlagDecorator` for [WinUI.TableView](https://github.com/w-ahmad/WinUI.TableView) rows. | Core |
| `Xtreme.Components` | Meta-package that pulls everything above. | all |

All packages share one version. Pre-release (`0.x-preview`) until the Docking layout document
and the Scheduler item model stop changing shape.

## Quick start

```xml
<Page xmlns:dock="using:Xtreme.Components.Docking.Controls">
  <dock:DockHost PersistenceKey="Main">
    <dock:DockHost.Document>
      <TextBox AcceptsReturn="True" />
    </dock:DockHost.Document>
    <dock:DockItem Title="Explorer" Edge="Left">
      <TreeView />
    </dock:DockItem>
  </dock:DockHost>
</Page>
```

Each package README has its own quick start. Host behaviour (where a layout is saved, how a
pane floats to an OS window) enters through small interfaces with no-op defaults, so nothing
is required to get going:

| Seam | Interface | What the app supplies |
|---|---|---|
| Layout persistence | `IDockPersistence` | Where the dock layout document is saved |
| OS windows | `IDockPlatformServices` | Float-to-window, drag tracking, window pool (desktop) |

## Theming

Merge the dictionary of each family you use once in `App.xaml` (a family's dictionary brings
the Core tokens with it), then override any token after the merge:

```xml
<ResourceDictionary.MergedDictionaries>
  <ResourceDictionary Source="ms-appx:///Xtreme.Components.Primitives/Themes/Generic.xaml" />
</ResourceDictionary.MergedDictionaries>
<x:Double x:Key="XcFontSizeBody">15</x:Double>
<SolidColorBrush x:Key="XcSchedulerSelectionFillBrush" Color="#0A84FF" />
```

Every token, the high-contrast keys and the runtime hooks are listed in
[docs/theming.md](docs/theming.md).

## Guides

One guide per package, covering every option with samples taken from the gallery:

- [Scheduler](docs/scheduler.md): views, data, interaction events, context menus, host-built
  editors, tiles, icons, adornments, tooltips and templates
- [Docking](docs/docking.md): dock host, panes, pinning, floating windows, persistence
- [Cards](docs/cards.md): SectionCard and the card docker
- [Primitives](docs/primitives.md): pickers, masked input, editor action bar, chips, panels
- [TableView](docs/tableview.md): helpers for WinUI.TableView
- [Core](docs/core.md): ComponentHost and the shared theme hooks
- [Theming](docs/theming.md) and [Architecture](docs/architecture.md)

## Gallery

Every family has a live page in the gallery, published from `main` to
**https://xtremecrm.github.io/Xtreme.Components/**. Run it locally on desktop with:

```bash
dotnet run --project samples/Xtreme.Components.Gallery -f net10.0-desktop
```

`--selftest` opens every page in turn and exits non-zero if one throws.

## Repository layout

```
src/        one project per package
samples/    Xtreme.Components.Gallery (desktop + browserwasm)
tests/      headless unit tests
docs/       architecture decisions and one page per family
```

- `ci.yml` builds, tests and packs `0.0.0-ci.<run>` on every pull request.
- `publish.yml` publishes to nuget.org from a `v*` tag through Trusted Publishing (OIDC, no
  stored API key). See [docs/releasing.md](docs/releasing.md).

## Developing against XtremeCRM

XtremeCRM consumes these packages from nuget.org. For same-day work on a control and its
consumer, clone this repo as a sibling of `XtremeCRM.Uno`
(`source\repos\Xtreme.Components`): the app's build detects the checkout and switches to
`ProjectReference` automatically, so Hot Reload keeps working. CI never has the sibling, so it
always restores the pinned package.

## Licence

MIT. See [LICENSE](LICENSE).
