# Changelog

All notable changes to Xtreme.Components are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and the packages use
[Semantic Versioning](https://semver.org/). Every package in a release carries the same
version. The publish workflow refuses a tag whose version has no section below.

## [Unreleased]

Every package now has a full guide under `docs/`. Closing the API gaps found while writing them
(XU-898..902):

### Added
- Scheduler: `AppointmentModel.Clone()`, `EmptySlotSelectedArgs.IsAllDay`. The Planner gains
  click and Ctrl multi-select, double-click edit and create, `EmptySlotSelected`, hover cards,
  context menus (also read-only), `HideIdleResources` and `GroupMode.ByDay` interaction. Month
  and Agenda raise select, edit and context events; Month double-click creates an all-day item.
  Tiles honor `Position`, `Alignment`, offsets, `Glyph`, colors and `Tooltip`; the adornment
  toolbar group honors `IsToggle`, `IsChecked`, `Command` and `MenuItems`.
- Docking: `DockItemCloseRequestedEventArgs` (cancelable, also raised by a floating window's
  close button), `DockHost.PersistenceProperty` and `EdgePriorityProperty`. Layout document v6
  persists floating state and bounds, the selected tab per group and in-layer column ratios
  (v1 to v5 still read). Panes hidden by `DockNarrowBehavior.Hide` are listed in the restore menu.
- Primitives: `InputMask` letter (`A`), alphanumeric (`*`) and escape (`\`) slots plus
  `ExtractSlots`; `MaskedTextBox.CharacterCasing`; `PagingControl.CurrentPageChanged`,
  `PageSizeChanged`, `LayoutMode`, `ShownLayout` and `PagingLayoutMode`;
  `MonthYearPicker.SelectedDateChanged`; `CriteriaPanel.FieldValueChanged`;
  `MultiMonthDateNavigator.FirstDayOfWeek` and keyboard navigation; `XcExcludedBrush`,
  `XcChipBackgroundBrush` and `XcChipForegroundBrush`.
- Cards: `SectionCard.SelectedIndex`, `SelectedTab`, `SelectionChanged`; `SectionCardTab.Icon`;
  `CardDockerPanel.PreferredColumn`; `CardDockerControl` text and brush properties; panel drag
  and keyboard moves (Ctrl+arrows, Delete).
- Core: `ComponentHost.PrepareDialog(ContentDialog, XamlRoot?)`.
- TableView: `RowFlagOptions`, `RowFlagDecorator.Detach` and `IsAttached`.

### Changed
- Scheduler: the Planner's zoned lanes report the host's original appointment; a single click
  on an empty Planner lane selects instead of creating. `TimeIncrement` defaults per control.
  The time contract is documented: wall-clock, except Planner lanes with a `TimeZoneId` (UTC).
- Docking: every `IsOpen` change persists; `DockItem.CloseRequested` carries
  `DockItemCloseRequestedEventArgs`; floating windows are Windows-only; `DockSplitter` follows
  live theme changes; diagnostics go through the `Log` hook.
- Primitives: `TypeableTimeBox` parses `930`, `4` and `4p` (the bare-hour format threw
  before); `ChipPicker.SaveAsync` always receives the whole list; `DateTimePicker.Header` is
  `object` and `ValueChanged` fires on code changes; `MonthYearPicker` values are always the
  1st of the month; inert members (`FrontSizeRatio`, `ChipMaxWidth`, `ShowTitle`, `CanRemove`,
  `DefaultPageSize`) now work.
- Cards: `SectionCard` follows later changes to tabs, title and headers; the
  `CardDockerControl.Layout` getter returns a copy; `LayoutChanged` no longer fires for no-op
  drops; lowering then raising `ColumnCount` restores panels.
- Core: `TryGetBrush` searches merged theme dictionaries for the requested theme and only
  swallows lookup failures; surface brushes are cached per theme.
- TableView: the row flag frame is an overlay, so rows keep their own border and height.
- US English spelling throughout the docs and comments.

### Removed
- Scheduler: public `TimeGridView`, `PlannerView`, `MonthView`, `AgendaView`, `DayColumnPanel`,
  `TimelineLanePanel`, `AllDayBanner` and `ResizeGrip` (now internal; `SchedulerControl` is the
  entry point) and the unimplemented default-editor fallback.
- Primitives: public `ChipPickerControl.WorkingItems`.

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
- `samples/Xtreme.Components.Gallery`: desktop + WebAssembly gallery with one page per family,
  published to GitHub Pages from `main`; `--selftest` on desktop opens every page.
- `Xtreme.Components.Docking`: `Win32DockPlatformServices`, the Windows implementation of
  `IDockPlatformServices` (window pool, owned float windows, drag tracking, drag translucency),
  ported from the XtremeCRM app. `HostWindow` is a property, diagnostics go to an optional `Log`
  hook, and the floating window's HWND comes from Uno's `WindowHelper.GetNativeWindow` with the
  foreground-window heuristic as fallback.
- Public API baseline: every package carries `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`
  under `Microsoft.CodeAnalysis.PublicApiAnalyzers`, so an API change is a visible diff.
- `Xtreme.Components.Scheduler`: `SchedulerControl` with Day, Week, Month, Agenda and
  multi-resource Planner views; drag-to-move, resize and click-to-create reported as events;
  external drag-drop via `ISchedulerDroppable`; per-resource IANA time zones; recurring-event
  indicators; templatable appointments.
- `Xtreme.Components.Docking`: `DockHost`, `DockItem`, `DockSplitter`;
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
