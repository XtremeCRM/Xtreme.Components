# Xtreme.Components.Primitives

Small, app-agnostic [Uno Platform](https://platform.uno) controls built from WinUI primitives
only. Part of [Xtreme.Components](https://github.com/XtremeCRM/Xtreme.Components); no
third-party packages.

**Full guide:** [docs/primitives.md](https://github.com/XtremeCRM/Xtreme.Components/blob/main/docs/primitives.md) covers every option with samples.

| Control | What it does |
|---|---|
| `MaskedTextBox` + `InputMask` | Hand-rolled masked input that works on every Uno head: `#` is a digit slot, `A` a letter, `*` a letter or digit, `\` escapes the next character, anything else is a literal (phone `(###) ###-####`, Canadian postal code `A#A #A#`). Optional `CharacterCasing`. The toolkit's `TextBoxExtensions.Mask` throws per keystroke on Uno; this does not. |
| `TypeableTimeBox` | A time field you can type into (`9`, `930`, `930p`, `9:30p`, `4pm`, `21:30`, `1603`) with `TryParseTime` exposed. |
| `CompactCalendarDatePicker` | `CalendarDatePicker` sized for dense forms. |
| `MonthYearPicker` | Month and year chooser; the value is always the 1st of the month. `SelectedDateChanged` event. |
| `MultiMonthDateNavigator` | Two-click date-range picker across several months, as used by the Scheduler toolbar. Keyboard navigable; first day of week from the culture. |
| `PagingControl` | Page size, page number and first/prev/next/last navigation, with an "All" size and single-page hiding. Responsive at three widths (`LayoutMode`, `ShownLayout`); `CurrentPageChanged` / `PageSizeChanged` events. |
| `ColumnFlowPanel` | Places each child in whichever of its N equal columns is currently shortest (masonry-style). |
| `WrapPanel` | Horizontal wrap with uniform spacing. |
| `ExpanderClipFix` | Attached helper that stops an `Expander`'s content bleeding outside its bounds while animating. |
| `ToastControl` | Transient bottom toast with icon, fade in and out. |
| `EditorActionBar` | The Delete / Save / Cancel bar for editors: trash on the left, accent Save and secondary Cancel on the right, Save gated on `IsDirty`. |
| `CopyButton` | Copy-to-clipboard button with a "Copied" confirmation. |
| `DateTimePicker` | Date and time in one field: multi-month calendar flyout plus a typeable time slot list. |
| `CriteriaPanel` | A titled group of labelled fields whose label column auto-sizes, for search criteria forms. `FieldValueChanged` reports edits in any field. |
| `ChipPickerControl` + `ChipPickerDialog` | Multi-select chips on a button that opens a grouped picker dialog; optional "everything except" exclusions. Items are `ChipItem` records. |
| `OverlayGlyphIcon` | A Fluent glyph (or an app-supplied vector) with a small accent badge glyph overlaid bottom-right. |

## Host hooks (optional)

`Xtreme.Components.Core.ComponentHost` carries two optional hooks an app can set at startup:

```csharp
ComponentHost.CurrentTheme = () => rootElement.ActualTheme;          // live theme switching
ComponentHost.ResolveGeometry = key => MyIcons.TryGet(key);          // OverlayGlyphIcon.BackGeometryKey
```

Without them, code-built dialogs follow `Application.RequestedTheme` and vector icon keys resolve
to nothing.

## Quick start

```xml
<Page xmlns:xc="using:Xtreme.Components.Primitives">
  <StackPanel Spacing="8">
    <xc:MaskedTextBox Mask="(###) ###-####" Header="Phone" />
    <xc:TypeableTimeBox Time="{x:Bind Model.Start, Mode=TwoWay}" />
    <xc:PagingControl x:Name="Pager" PageSize="25" TotalItemCount="{x:Bind Items.Count, Mode=OneWay}" />
    <xc:EditorActionBar IsDirty="{x:Bind Model.IsDirty, Mode=OneWay}"
                        SaveRequested="OnSave" CancelRequested="OnCancel" DeleteRequested="OnDelete" />
  </StackPanel>
</Page>
```

## Theming

Font sizes read the `XcFontSizeBody` and `XcFontSizeCaption` resources. Define them in
`App.xaml` to match your type ramp; a bare app can start with:

```xml
<x:Double x:Key="XcFontSizeBody">14</x:Double>
<x:Double x:Key="XcFontSizeCaption">12</x:Double>
<x:Double x:Key="XcFontSizeTitle">16</x:Double>
```

The package dictionary also defines theme-keyed brushes you can override: `XcExcludedBrush` (the
chip picker's NOT marks), `XcChipBackgroundBrush` and `XcChipForegroundBrush` (pills for a
`ChipItem` with no colors of its own).

Targets `net10.0` only; the consuming Uno head supplies the renderer. MIT license.
