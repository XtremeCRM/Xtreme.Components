# Theming

Every colour, brush and font size a control uses is either a `ThemeResource` in its XAML or a
named token the app can override. Nothing is reachable only by editing library code.

## 1. Merge the dictionaries

Each family ships a `Themes/Generic.xaml`. Merge the ones you use once, in `App.xaml`; a
family's dictionary merges Core's tokens itself.

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
      <ResourceDictionary Source="ms-appx:///Xtreme.Components.Primitives/Themes/Generic.xaml" />
      <!-- Core alone, if you use only Scheduler / Docking / Cards / TableView: -->
      <!-- <ResourceDictionary Source="ms-appx:///Xtreme.Components.Core/Themes/Generic.xaml" /> -->
    </ResourceDictionary.MergedDictionaries>

    <!-- Overrides go AFTER the merges; a later definition wins. -->
    <x:Double x:Key="XcFontSizeBody">15</x:Double>
  </ResourceDictionary>
</Application.Resources>
```

Uno does not discover a library's `Generic.xaml` on every platform by itself, which is why the
merge is explicit.

## 2. Tokens

### Core (`Xtreme.Components.Core/Themes/Generic.xaml`)

| Key | Type | Default | Used by |
|---|---|---|---|
| `XcFontSizeCaption` | double | 12 | Primitives (captions, pickers, copy button) |
| `XcFontSizeBody` | double | 14 | Primitives (paging, chip picker, pickers) |
| `XcFontSizeTitle` | double | 16 | Cards (`SectionCard` title) |
| `XcFieldLabelStyle` | TextBlock style | secondary caption, 4px bottom margin | `DateTimePicker` |
| `XcListSectionHeaderTextStyle` | TextBlock style | semibold secondary caption | `ChipPickerDialog` |
| `XcListViewItemStyle` | ListViewItem style | 32px min height, 8/4 padding | `DateTimePicker` time list |

### Surface (code-painted chrome, `ComponentTheme`)

`ComponentTheme.Surface(theme)` and `SurfaceOutline(theme)` paint the dock chrome and card
docker from code. They follow the element's actual theme. A **high-contrast** palette the app
merges on demand overrides them by defining:

| Key | Meaning |
|---|---|
| `XcHighContrast` | Sentinel (any value). Present = a high-contrast palette is active. |
| `XcSurfaceBrush` | Pane / document surface |
| `XcSurfaceOutlineBrush` | Hairline outline on that surface |

These are deliberately not defaulted; their presence is the switch.

### Scheduler roles (`XcScheduler<Role>Brush`, optional)

The scheduler renders from code, so its palette is resolved per theme and cached. Define any of
these to override the built-in value; put them under `ThemeDictionaries` `Light` / `Dark` to get
theme-specific values, or flat for both.

`SurfaceBackground`, `HeaderBackground`, `HeaderForeground`, `SecondaryForeground`, `MinorLine`,
`MajorLine`, `TodayHighlight`, `OffHoursBackground`, `EventDefaultBack`, `EventDefaultBar`,
`EventForeground`, `EventBorder`, `OutsideMonthForeground`, `AgendaRule`, `AccentForeground`,
`WeekendForeground`, `SelectionFill`, `SelectionForeground`, `OutsideMonthFill`.

```xml
<ResourceDictionary.ThemeDictionaries>
  <ResourceDictionary x:Key="Light">
    <SolidColorBrush x:Key="XcSchedulerSelectionFillBrush" Color="#0A84FF" />
  </ResourceDictionary>
  <ResourceDictionary x:Key="Dark">
    <SolidColorBrush x:Key="XcSchedulerSelectionFillBrush" Color="#409CFF" />
  </ResourceDictionary>
</ResourceDictionary.ThemeDictionaries>
```

### Calendar roles (`XcCalendar<Role>Brush`, optional)

Used by `MultiMonthDateNavigator` and the pickers built on it:
`SecondaryForeground`, `MinorLine`, `SelectionFill`, `SelectionForeground`, `OutsideMonthFill`,
`OutsideMonthForeground`, `HeaderForeground`, `WeekendForeground`.

### TableView

`RowFlag.BrushKey` names any brush resource; it resolves for the row's actual theme. Name your
own keys (for example `XcFlagChangedBrush`) and define them in `ThemeDictionaries`.

## 3. Runtime changes

- **Theme switch**: nothing to call. Palettes are keyed by theme and controls re-render on
  `ActualThemeChanged`. If your app drives its theme from a root element's `RequestedTheme`
  rather than the OS, set `ComponentHost.CurrentTheme = () => root.ActualTheme` so code-built
  dialogs and resource lookups follow it.
- **Merging or removing a dictionary at runtime** (a high-contrast palette, a brand switch):
  call `ComponentHost.NotifyResourcesChanged()` so cached palette lookups re-resolve.

## 4. Still hard-coded

The dock manager's chrome washes (guide overlays, drag ghosts, scrims, hover tints in
`DockHost`, `DockItemHeader`, `DockSplitter`) are translucent greys and blues painted from code.
They read correctly in both themes and under the surface overrides above, so they have not been
tokenised; a `XcDock<Role>Brush` set following the scheduler pattern is the next step when a
consumer needs it.
