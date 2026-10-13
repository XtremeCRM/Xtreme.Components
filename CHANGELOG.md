# Changelog

All notable changes to Xtreme.Components are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and the packages use
[Semantic Versioning](https://semver.org/). Every package in a release carries the same
version. The publish workflow refuses a tag whose version has no section below.

## [Unreleased]

## [0.1.0-preview.1] - 2026-10-13

First release from the public repository. The code was extracted from
[XtremeCRM.Uno](https://github.com/XtremeCRM/XtremeCRM.Uno) `ClientApps/Xtreme.Components`
(itself the former standalone `Uno.Scheduler` 0.1.0 package) and split into one package per
family on a shared version.

### Added
- `Xtreme.Components.Core`: `ComponentTheme`, the `ActualTheme`-driven surface and outline
  brushes shared by Docking and Cards, with high-contrast overrides read from the
  `XcHighContrast` sentinel key.
- `Xtreme.Components.Primitives`: `MaskedTextBox` + `InputMask`, `TypeableTimeBox`,
  `CompactCalendarDatePicker`, `MonthYearPicker`, `MultiMonthDateNavigator` (moved out of the
  Scheduler, which now depends on Primitives), `PagingControl`, `ColumnFlowPanel`, `WrapPanel`,
  `ExpanderClipFix`, `ToastControl`, `EditorActionBar`, `CopyButton`, `DateTimePicker`
  (was `XtremeDateTimePicker`), `CriteriaPanel`, `ChipPickerControl` + `ChipPickerDialog`
  (items are `ChipItem`, was `ChipVM`), `OverlayGlyphIcon`. Font sizes and field styles read
  the `XcFontSizeBody` / `XcFontSizeCaption` / `XcFontSizeTitle` / `XcFieldLabelStyle` /
  `XcListViewItemStyle` / `XcListSectionHeaderTextStyle` resources.
- `Xtreme.Components.Core`: `ComponentHost` with the optional `CurrentTheme` and
  `ResolveGeometry` hooks, `TryGetBrush` and `PrepareDialog`.
- `Xtreme.Components.TableView`: `RowFlagDecorator` + `RowFlag` for `WinUI.TableView` rows,
  with a `BrushResolver` seam (default: the row's `ActualTheme` through `ComponentHost`).
- Theming: Core ships `Themes/Generic.xaml` with the `Xc*` font and style tokens; the Scheduler
  and calendar palettes resolve `XcScheduler<Role>Brush` / `XcCalendar<Role>Brush` tokens before
  their built-in values; `ComponentHost.NotifyResourcesChanged()` drops the cached lookups.
  See `docs/theming.md`.
- `Xtreme.Components.Scheduler`: `SchedulerControl` with Day, Week, Month, Agenda and
  multi-resource Planner views; drag-to-move, resize and click-to-create reported as events;
  external drag-drop via `ISchedulerDroppable`; per-resource IANA time zones; recurring-event
  indicators; templatable appointments.
- `Xtreme.Components.Docking`: `DockHost`, `DockItem`, `DockItemHeader`, `DockSplitter`;
  tab groups and side-by-side layers; auto-hide rails with slide-in flyouts; float to an OS
  window and drag back to re-dock through `IDockPlatformServices`; layout document v5 through
  `IDockPersistence`.
- `Xtreme.Components.Cards`: `CardDockerControl` (drag cards into panels across a column grid)
  and `SectionCard` / `SectionCardTab` (card frame with a top-pane tab strip).
- `Xtreme.Components`: meta-package depending on every family.

### Changed
- `Xtreme.Components.CardDocking` namespace renamed to `Xtreme.Components.Cards` to match the
  package id.
- `SectionCard` moved from `Xtreme.Shell.Controls` into `Xtreme.Components.Cards`.
- High-contrast surface keys renamed from `XtremeDockSurfaceBrush` /
  `XtremeDockSurfaceOutlineBrush` (sentinel `XtremeHcCardColor`) to `XcSurfaceBrush` /
  `XcSurfaceOutlineBrush` (sentinel `XcHighContrast`).

[Unreleased]: https://github.com/XtremeCRM/Xtreme.Components/compare/v0.1.0-preview.1...HEAD
[0.1.0-preview.1]: https://github.com/XtremeCRM/Xtreme.Components/releases/tag/v0.1.0-preview.1
