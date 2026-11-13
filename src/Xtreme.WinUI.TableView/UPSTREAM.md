# Upstream tracking

This folder holds a vendored copy of the `src/` folder of
[w-ahmad/WinUI.TableView](https://github.com/w-ahmad/WinUI.TableView) (MIT; see `LICENSE.md`).

| | |
|---|---|
| Base | tag `v1.5.1`, commit `6de5f1cf388747e7ed046d5180815ca28c59f525` |
| Package | `Xtreme.WinUI.TableView` `1.5.1-uno7.1` (assembly and namespace `WinUI.TableView`) |
| Why | Upstream ships an Uno.WinUI 6.5 build only. Uno 7 cannot load libraries built for Uno 6.x (XU-1203). |

## Changes from upstream

Upstream's `WinUI.TableView.csproj` (Microsoft.NET.Sdk with a `Uno.WinUI` package reference,
multi-targeting net8/9/10 plus WinAppSDK) is replaced by `Xtreme.WinUI.TableView.csproj`. It uses
Uno.Sdk like the other libraries in this repository, targets `net11.0` only, and inherits the Uno
version from `global.json`.

Source changes are limited to Uno-only APIs that Uno 7 removed. All of them are in `#if !WINDOWS`
branches:

1. `TableView.cs`, `ShowRowContext` / `ShowCellContext`: Uno 7 removed `FlyoutBase.DataContext`.
   The new `SetFlyoutDataContext` helper sets the row item as the `DataContext` of each
   `MenuFlyout` item, or of the `Flyout`'s content, instead.
2. `TableViewCell.cs`, the two `TableViewTemplateColumn` measure paths: Uno 7 removed
   `ContentPresenter.ContentTemplateRoot`. The template root is now the presenter's first visual
   child (`VisualTreeHelper.GetChild(presenter, 0)`).
3. `TableViewColumnHeader.cs`, `CreateDragVisualsAsync`: this works around an Uno 7 bug rather than a
   removed API. On Uno.WinUI 7.0.32, a `RenderTargetBitmap.RenderAsync` of a zero-pixel element kills
   the process from the finalizer thread, because of `GC.RemoveMemoryPressure(0)`. The method now
   returns null, so there is no drag visual, when the header rounds to under one physical pixel.
   Drop this change once Uno fixes the bug.

No other file differs from the base tag.

## Updating

1. Copy the new upstream `src/` over this folder. Leave out `WinUI.TableView.csproj`, and keep this
   file, `README.md`, `LICENSE.md` and `Xtreme.WinUI.TableView.csproj`.
2. Re-apply the changes above if upstream still has the Uno-only calls.
3. Set `<Version>` in the csproj to the new upstream version plus `-uno7.1`. Bump the suffix instead
   when only our changes move.
4. Update the base commit above.

Once upstream publishes an Uno 7 build, delete this project. Then point
`Xtreme.Components.TableView` back at the upstream `WinUI.TableView` package.
