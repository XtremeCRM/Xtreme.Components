# Xtreme.Components.Primitives developer guide

`Xtreme.Components.Primitives` is a set of small, unrelated Uno Platform controls built from WinUI
primitives only: input fields, date pickers, paging, layout panels and editor feedback. It has no
third-party dependencies; it references only `Xtreme.Components.Core` (theme tokens and the
`ComponentHost` hooks). Targets `net10.0`.

- Live gallery: <https://xtremecrm.github.io/Xtreme.Components/> (Primitives page)
- Gallery source: [`samples/Xtreme.Components.Gallery/Pages/PrimitivesPage.xaml`](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/Pages/PrimitivesPage.xaml)
  and its [code-behind](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/Pages/PrimitivesPage.xaml.cs)
- Where this package sits among the others: [architecture.md, Package map](architecture.md#package-map)

## Contents

- [Setup](#setup)
- Input
  - [MaskedTextBox](#maskedtextbox)
  - [TypeableTimeBox](#typeabletimebox)
  - [DateTimePicker](#datetimepicker)
  - [MonthYearPicker](#monthyearpicker)
  - [CompactCalendarDatePicker](#compactcalendardatepicker)
  - [MultiMonthDateNavigator](#multimonthdatenavigator)
  - [ChipPickerControl, ChipItem and ChipPickerDialog](#chippickercontrol-chipitem-and-chippickerdialog)
- Paging
  - [PagingControl](#pagingcontrol)
- Layout
  - [WrapPanel](#wrappanel)
  - [ColumnFlowPanel](#columnflowpanel)
  - [CriteriaPanel](#criteriapanel)
  - [ExpanderClipFix](#expanderclipfix)
- Feedback
  - [EditorActionBar](#editoractionbar)
  - [CopyButton](#copybutton)
  - [ToastControl](#toastcontrol)
- Icons
  - [OverlayGlyphIcon](#overlayglyphicon)
- Helpers
  - [InputMask](#inputmask)
  - [ClipboardHelper](#clipboardhelper)
  - [Value converters](#value-converters)
- [Theming](#theming)

All types live in the `Xtreme.Components.Primitives` namespace. The samples below use this XAML
prefix:

```xml
xmlns:xc="using:Xtreme.Components.Primitives"
```

## Setup

Merge the package dictionary once in `App.xaml`. It merges the Core tokens itself.

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
      <ResourceDictionary Source="ms-appx:///Xtreme.Components.Primitives/Themes/Generic.xaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

This is not optional. Most controls read `XcFontSizeBody`, `XcFontSizeCaption`,
`XcFieldLabelStyle` or `XcListViewItemStyle` as `StaticResource`, and `OverlayGlyphIcon` gets its
only template from this dictionary. Several controls also rely on WinUI resources from
`XamlControlsResources` (`AccentButtonStyle`, `BodyStrongTextBlockStyle`,
`DefaultContentDialogStyle`).

Two optional hooks on `Xtreme.Components.Core.ComponentHost` affect this package:

```csharp
ComponentHost.CurrentTheme = () => rootElement.ActualTheme;   // code-built dialogs follow a root-driven theme
ComponentHost.ResolveGeometry = key => MyIcons.TryGet(key);   // vector keys for OverlayGlyphIcon.BackGeometryKey
```

See [theming.md](theming.md) for the full token list and runtime theme behaviour.

---

## MaskedTextBox

A text box with a fixed input mask (phone numbers, postal codes, IDs) that works on every Uno head.
The Community Toolkit's `TextBoxExtensions.Mask` throws on each keystroke under Uno; this control
replaces it.

The mask grammar is the one implemented by [`InputMask`](#inputmask): `#` is one digit, every other
character is a literal shown as-is. A mask with no `#` is treated as no mask, and the control
behaves as a plain `TextBox`.

```xml
<xc:MaskedTextBox Header="Phone"
                  Mask="(###) ###-####"
                  Text="{x:Bind ViewModel.Phone, Mode=TwoWay}" />
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `Text` | `string` | `""` | The value without placeholders, e.g. `(555) 123-4567`, or `(555) 12` part-way through. Two-way bindable. Setting it re-renders the display and normalises the value to the mask. |
| `Mask` | `string?` | `null` | Mask pattern. `null`, empty or `#`-free means plain text entry. |
| `Header` | `object?` | `null` | Forwarded to the inner `TextBox.Header`. |
| `PlaceholderText` | `string` | `""` | Forwarded to the inner `TextBox`. Only visible when there is no mask, because a masked box always shows its template. |

Behaviour:

- The full template is shown up front with `_` in each empty digit slot, e.g. `(___) ___-____`,
  and the caret sits at the first empty slot.
- Non-digit keystrokes, and digits once every slot is full, are cancelled in `BeforeTextChanging`,
  so they never render.
- Backspace over a literal or placeholder removes the previous digit, so deletion never stalls on a
  separator.
- Pasting formatted or raw text works: input is reduced to its digits and laid out again.
- Only digits are supported. There is no letter slot, so alphanumeric codes (for example Canadian
  postal codes) cannot be masked.

## TypeableTimeBox

A time field that accepts free typing and also offers a drop-down of quarter-hour slots. Built on
`AutoSuggestBox`.

```xml
<xc:TypeableTimeBox x:Name="StartBox"
                    Time="{x:Bind ViewModel.Start, Mode=TwoWay}"
                    TimeCommitted="StartBox_TimeCommitted" />
```

```csharp
private void StartBox_TimeCommitted(object sender, TimeOnly? time)
    => Status.Text = time is { } t ? $"Starts {t:h:mm tt}" : "No start time";
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `Time` | `TimeOnly?` | `null` | The committed value. Setting it rewrites the text as `h:mm tt` in the current culture. |
| `TimeCommitted` | `EventHandler<TimeOnly?>` | | Raised when a commit changes `Time`. Not raised for programmatic sets of `Time`, nor when the commit leaves the value unchanged. |
| `TryParseTime(string input, out TimeOnly result)` | `static bool` | | The parser the control uses. Public so other fields can share it (`DateTimePicker` does). |

Behaviour:

- Typing filters the 96 quarter-hour suggestions by prefix.
- The value commits when a suggestion is chosen or when the box loses focus. Pressing Enter does
  not commit by itself; tab out or pick a suggestion.
- An empty box commits `null`. Text that does not parse reverts to the last good value.
- After a commit the text is rewritten canonically (`4p` becomes `4:00 PM`).
- `TryParseTime` accepts, case-insensitively: `h:mm tt` and `h tt` forms with or without the space
  (`4:03 pm`, `4pm`), a single `a`/`p` marker (`4p`, `9:30p`, `11A`), 24-hour `H:mm` (`16:03`),
  a bare 12-hour hour (`4`, read as 4:00 AM), and a four-digit `HHmm` (`1603`). Anything
  `TimeOnly.TryParse` accepts in the current culture also works. Three-digit input such as `930`
  is not handled by the heuristics.

## DateTimePicker

A single field for a date and an optional time. Collapsed, it is a button showing the value.
Clicking it opens one flyout holding a single-month calendar (a
[`MultiMonthDateNavigator`](#multimonthdatenavigator) in single-date mode) beside a time panel with
a typeable time box, a slot-granularity bar (1, 5, 10, 15, 30, 60 minutes) and a slot list.

The value is `DateOnly?` plus `TimeOnly?`; no `DateTimeOffset` is involved, so values cannot shift
a day across time zones.

```xml
<xc:DateTimePicker Header="Starts"
                   Date="{x:Bind ViewModel.StartDate, Mode=TwoWay}"
                   Time="{x:Bind ViewModel.StartTime, Mode=TwoWay}"
                   MinuteStep="15"
                   ValueChanged="Starts_ValueChanged" />

<!-- Date only -->
<xc:DateTimePicker Header="Due" ShowTime="False" Date="{x:Bind ViewModel.Due, Mode=TwoWay}" />
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `Date` | `DateOnly?` | `null` | Published date. |
| `Time` | `TimeOnly?` | `null` | Published time. |
| `Header` | `string` | `""` | Label shown above the button. |
| `ShowTime` | `bool` | `true` | `false` hides the time panel and drops the time from the button text. |
| `MinuteStep` | `int` | `15` | Granularity of the slot list. The step bar in the flyout writes this property. Values below 1 are treated as 1. |
| `ValueChanged` | `EventHandler` | | Raised once per Save that changed `Date` and/or `Time`. |

Behaviour:

- **Pending edit.** Changes inside the flyout go to a pending copy. Only **Save** publishes them to
  `Date`/`Time` (and raises `ValueChanged`). **Cancel** discards them. **Clear** empties the pending
  copy; it still needs Save.
- **Dirty-gated Save.** Save is enabled only while the pending value differs from the published one.
  Cancel is always enabled because it doubles as the way out.
- **Click-away is blocked while dirty**, so a stray click cannot discard a picked date. A clean
  flyout light-dismisses normally.
- **Keyboard.** Escape anywhere in the flyout cancels and closes (registered with
  `handledEventsToo`, so child controls cannot swallow it). Enter in the time box parses and
  commits the typed time to the pending edit. Save also commits a typed time that was never
  tabbed out of.
- Typed times are parsed with [`TypeableTimeBox.TryParseTime`](#typeabletimebox). A time that
  falls between slots (4:03 with a 15-minute step) is kept as typed and the list shows no selection.
- **Today** in the calendar header selects today.
- **Responsive.** The flyout lays the time panel beside the calendar, or stacks it underneath
  (with a shorter list and vertical scrolling) when the window is narrower than 620 px. Checked on
  every open.
- The button text adapts to the control's width: `ddd MMM d, yyyy` at 320 px and above,
  `MMM d, yyyy` at 220 px and above, otherwise `M/d/yyyy`, followed by the time. An empty value
  shows an em dash; a time without a date shows `(no date)`.
- Setting `Date` or `Time` from code re-baselines the pending edit and does **not** raise
  `ValueChanged`.

Tokens: `XcFontSizeBody`, `XcFontSizeCaption`, `XcFieldLabelStyle`, `XcListViewItemStyle`, and the
calendar roles listed under [MultiMonthDateNavigator](#multimonthdatenavigator).

## MonthYearPicker

A month-and-year chooser with previous/next arrows either side of a centre button. The centre
button opens a `DatePickerFlyout` with the day column hidden.

```xml
<xc:MonthYearPicker SelectedDate="{x:Bind ViewModel.BillingMonth, Mode=TwoWay}"
                    IncrementMonths="1" />
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `SelectedDate` | `DateTimeOffset` | `DateTimeOffset.Now` at construction | The selected month. Shown as `MMM` and `yyyy`. |
| `IncrementMonths` | `int` | `1` | Months moved by each arrow. Values below 1 are treated as 1. |

Behaviour:

- The arrows add or subtract `IncrementMonths` and keep the day and offset of the current value.
- Closing the flyout sets `SelectedDate` to the **first of the month** shown in the flyout, at
  midnight. A month value from the flyout is therefore always normalised to day 1; the initial
  value and arrow steps are not.
- There is no change event; bind `SelectedDate` two-way or use
  `RegisterPropertyChangedCallback(MonthYearPicker.SelectedDateProperty, ...)`.

Tokens: `XcFontSizeBody`.

## CompactCalendarDatePicker

A standard `CalendarDatePicker` whose popup calendar uses smaller fonts, for dense forms. It has no
new members; use it exactly like `CalendarDatePicker`.

```xml
<xc:CompactCalendarDatePicker Date="{x:Bind ViewModel.Date, Mode=TwoWay}" />
```

| Member | Type | Purpose |
|---|---|---|
| `OnApplyTemplate()` | override | Sets the template's `CalendarView` to `DayItemFontSize` 14, `MonthYearItemFontSize` 14, `FirstOfMonthLabelFontSize` 11, `FirstOfYearDecadeLabelFontSize` 11. |

The sizes are fixed in code; they do not read the `XcFontSize*` tokens.

## MultiMonthDateNavigator

A resizable grid of month tiles for picking dates: individual days, scattered sets or ranges. It
shows as many months as fit its size (each tile is about 214 x 206 px). It is the calendar inside
`DateTimePicker` and the Scheduler's date navigator.

```xml
<xc:MultiMonthDateNavigator x:Name="Nav" Width="680" Height="260"
                            SelectionChanged="Nav_SelectionChanged" />
```

```csharp
Nav.SetFirstDayOfWeek(DayOfWeek.Monday);
Nav.SetSelection(new[] { DateTime.Today });

private void Nav_SelectionChanged(object? sender, EventArgs e)
    => Summary.Text = $"{Nav.GetSelection().Count} day(s) picked";
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `SingleDateMode` | `bool` | `false` | Date-field mode: hides the Days/Range switch, a tap replaces the selection, Ctrl/Shift are ignored, and a single visible month fills its leading/trailing cells with the neighbouring months' days. |
| `SelectionChanged` | `EventHandler` | | Raised after a user gesture changes the selection, after **Today** in single-date mode, and by `ClearSelection` when something was selected. Not raised by `SetSelection`. |
| `SetSelection(IReadOnlyList<DateTime> dates)` | method | | Replaces the selection (time parts are dropped) and pages to the earliest date, or to today when the list is empty. |
| `GetSelection()` | `IReadOnlyList<DateTime>` | | The selected days, ascending. |
| `ClearSelection()` | method | | Empties the selection and abandons a half-built range. |
| `SetFirstDayOfWeek(DayOfWeek day)` | method | Sunday | First column of each month grid. Not taken from the culture; set it explicitly. |

Gestures (multi-select mode):

| Gesture | Effect |
|---|---|
| Click | **Days** mode: toggles the day. **Range** mode: first tap selects a start day, second tap fills the range to it, third tap starts again. |
| Ctrl+click | Toggles one day, in either mode. |
| Shift+click | Adds the contiguous range from the last clicked day. |
| ‹ / › | Pages by the number of months currently visible. |
| Today | Pages to today's month. Selects today only in `SingleDateMode`. |

Behaviour notes:

- When more than one month is visible and the opening date is in the first week of its month, the
  grid opens one month earlier so the end of the previous month is visible.
- With two or more months visible, out-of-month cells are left blank so a day never appears twice.
- Today has an accent outline; weekends use the weekend colour.
- Selection is pointer-only. Day cells are not focusable and there is no keyboard navigation inside
  the grid.

Tokens: optional `XcCalendar<Role>Brush` overrides for `SecondaryForeground`, `MinorLine`,
`SelectionFill`, `SelectionForeground`, `OutsideMonthFill`, `OutsideMonthForeground`,
`HeaderForeground` and `WeekendForeground`, resolved per element theme and cached until
`ComponentHost.NotifyResourcesChanged()`. The control redraws on `ActualThemeChanged`. See
[theming.md, Calendar roles](theming.md#calendar-roles-xccalendarrolebrush-optional).

## ChipPickerControl, ChipItem and ChipPickerDialog

A multi-select field. The control is a button that shows the selected items as coloured chips
(pills), or its `Title` when nothing is selected. Clicking it opens `ChipPickerDialog`, a checkbox
list optionally grouped under section headers. Hosts can also replace the dialog with their own
picker.

```xml
<xc:ChipPickerControl x:Name="Tags" Title="Tags" />
```

```csharp
Tags.Items =
[
    new ChipItem { ChipId = 1, Name = "Residential", GroupName = "Customer" },
    new ChipItem { ChipId = 2, Name = "Commercial",  GroupName = "Customer" },
    new ChipItem { ChipId = 3, Name = "Battery plan", GroupName = "Service", IsSelected = true },
    new ChipItem { ChipId = 5, Name = "Past due", GroupName = "Billing",
                   BackgroundColor = Color.FromArgb(0xFF, 0xF4, 0xC7, 0xC3) },
];

Tags.SaveAsync = async items =>
{
    // From the built-in dialog this is the whole working list; filter on IsSelected.
    var picked = items.Where(i => i.IsSelected).Select(i => i.ChipId).ToList();
    await repository.SaveTagsAsync(picked);
};
```

### ChipPickerControl

| Member | Type | Default | Purpose |
|---|---|---|---|
| `Items` | `IReadOnlyList<ChipItem>?` | `null` | All choices with their current `IsSelected` state. Setting it recomputes `SelectedItems` and `ShowTitle`. The dialog does not open while this is `null` (unless `CustomPickAsync` is set). |
| `SelectedItems` | `IReadOnlyList<ChipItem>?` | `null` | Read-only. The items of `Items` with `IsSelected = true`; drives the chips. |
| `Title` | `string` | `""` | Text shown on the button when nothing is selected; also used for the dialog title, `Select {Title}` (trailing `…`, `.` and spaces trimmed), or `Select items` when blank. |
| `ShowTitle` | `bool` | `true` | Whether the title text is visible. Recomputed (true when nothing is selected) every time `Items` changes, so a value you set is overwritten. |
| `AllowExclude` | `bool` | `false` | Adds a per-row NOT toggle to the dialog so a selected item can mean "everything except this". Excluded chips show a red circle-slash badge. Only enable it when the consumer honours `ChipItem.IsExcluded`. |
| `SaveAsync` | `Func<IReadOnlyList<ChipItem>, ValueTask>?` | `null` | Called when the user saves. Built-in dialog: receives the **whole** working list, selected and unselected. `CustomPickAsync` path: receives what the picker returned. |
| `CustomPickAsync` | `Func<IReadOnlyList<ChipItem>, Task<IReadOnlyList<ChipItem>?>>?` | `null` | Replaces the built-in dialog. Receives the current `Items` (empty when `null`), returns the new selection or `null` to cancel. The result goes through `SaveAsync`, then becomes `Items` unless `Items` is data-bound. |
| `IsBusy` | `bool` | `false` | Shows a progress ring in place of the chips. Set by the control while `SaveAsync` runs. |
| `WorkingItems` | `IReadOnlyList<ChipItem>?` | `null` | The dialog's editable copy (each item cloned with `with { }` when the dialog opens). Exposed for the dialog's bindings; not intended for consumers. |
| `CalculatedMaxChipWidth` | `double` | `200` | Maximum width of each chip on the button; longer text is trimmed with an ellipsis. |
| `ChipMaxWidth` | `double` | `0` | Read-only. Not currently written or used by the control. |

Behaviour:

- After a successful dialog Save the control assigns the working list to `Items`, so the chips
  update even when the host pushes `Items` imperatively.
- Each `*Property` field (`ItemsProperty`, `TitleProperty` and so on) is public for binding and
  `RegisterPropertyChangedCallback`.

### ChipItem

An immutable-by-default `record`. `IsSelected` and `IsExcluded` are mutable so the dialog can edit
its working copy.

| Member | Type | Default | Purpose |
|---|---|---|---|
| `ChipId` | `int` (init) | `0` | Your identifier. The dialog's dirty check compares `(ChipId, IsExcluded)` of selected rows, so IDs should be unique. |
| `Name` | `string` (required, init) | | Row text in the dialog, and pill text unless `PillName` is set. |
| `IsSelected` | `bool` | `false` | Selected state. |
| `IsExcluded` | `bool` | `false` | NOT flag; meaningful only while selected and only with `AllowExclude`. |
| `CanRemove` | `bool` (init) | `true` | Carried for consumers; the control does not read it. |
| `GroupName` | `string?` (init) | `null` | Section header in the dialog. Headers are drawn only when at least one item has a group; items with none go under "Other". Groups keep first-appearance order. |
| `PillName` | `string?` (init) | `null` | Alternative pill text, e.g. `Vendor (Company)` when the dialog's group header is not visible on the button. |
| `PillText` | `string` (get) | | `PillName` when set, else `Name`. |
| `BackgroundColor` | `Color?` (init) | `#FFE1E1E1` (light grey) | Pill fill. `null` falls back to `AccentFillColorDefaultBrush`. |
| `ForegroundColor` | `Color?` (init) | `Colors.Black` | Pill text. `null` falls back to `TextOnAccentFillColorPrimaryBrush`. |

### ChipPickerDialog and ChipGroupHeader

`ChipPickerDialog` is the `ContentDialog` the control opens. It has only a public parameterless
constructor and expects a `ChipPickerControl` as its `DataContext`; you normally never create it
yourself.

- **Save is dirty-gated**: enabled only when the set of selected `(ChipId, IsExcluded)` pairs
  differs from what the dialog opened with. Toggling a row off and on again reads as clean.
- Unchecking an excluded row clears its NOT flag. Pressing NOT on an unselected row selects it.
- The list scrolls at 400 px. There is no search box.
- `DefaultButton` is Save, so Enter saves when Save is enabled; Escape cancels.
- The dialog applies `DefaultContentDialogStyle` and follows `ComponentHost.CurrentTheme`.

`ChipGroupHeader(string Name)` is the public record used for section-header rows inside the dialog.

Tokens: `XcFontSizeBody` (button title), `XcListSectionHeaderTextStyle` (group headers). With
`AllowExclude`, the dialog also uses a `XtremeExcludedBrush` theme resource that this package does
not define; an app that enables exclusions must supply it.

## PagingControl

A pager with a page-size selector, first/previous/numbered/next/last buttons, a "Go to" box and an
optional row summary. It reflows across three widths: inline, overflow (size selector and "Go to"
move into a flyout) and compact (previous/next around a `Page n of m` button that opens a flyout).

It does not page anything itself. Bind `TotalItemCount`, then react to `CurrentPage` and `PageSize`.

```xml
<xc:PagingControl x:Name="Pager"
                  TotalItemCount="{x:Bind ViewModel.Total, Mode=OneWay}"
                  CurrentPage="{x:Bind ViewModel.Page, Mode=TwoWay}"
                  PageSize="{x:Bind ViewModel.PageSize, Mode=TwoWay}"
                  ShowAllOption="True"
                  ShowSummary="True" />
```

Without bindings, watch the dependency properties (from the gallery):

```csharp
Pager.TotalItemCount = 137;
Pager.PageSize = 25;
Pager.RegisterPropertyChangedCallback(PagingControl.CurrentPageProperty, (_, _) => LoadPage());
Pager.RegisterPropertyChangedCallback(PagingControl.PageSizeProperty, (_, _) => LoadPage());
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `TotalItemCount` | `uint` | `0` | Number of rows being paged. |
| `CurrentPage` | `uint` | `1` | 1-based current page. Clamped to the page count; `0` is treated as 1. |
| `PageSize` | `uint` | `10` | Rows per page. Setting `0` snaps to `DefaultPageSize` (or 10). On change, `CurrentPage` moves so the first visible row stays on screen. |
| `PageSizeOptions` | `IList<uint>?` | `null` (uses 10, 25, 50, 100) | Choices in the size selector. Changes rebuild all three selectors. |
| `DefaultPageSize` | `uint` | `10` | Fallback used only when `PageSize` is `0`. |
| `ShowAllOption` | `bool` | `false` | Appends **All** to the size selector. While chosen, `PageSize` follows `TotalItemCount` so everything is on one page. |
| `ShowPageSizeSelector` | `bool` | `true` | Shows the "Rows:" selector. |
| `ShowFirstButton` | `bool` | `true` | Shows the first-page button (inline and overflow modes). |
| `ShowLastButton` | `bool` | `true` | Shows the last-page button (inline and overflow modes). |
| `ShowNumberedPages` | `bool` | `true` | Shows numbered page buttons (inline and overflow modes). |
| `MaxNumberedPages` | `int` | `7` | Width of the numbered window, including first and last; values below 5 are raised to 5, `0` or less means 7. Gaps show as `…`. |
| `ShowJumpToPage` | `bool` | `true` | Shows the "Go to:" box. |
| `ShowSummary` | `bool` | `false` | Shows the row summary. |
| `SummaryFormat` | `string` | `"{0}-{1} of {2}"` | Summary format: first row, last row, total. |
| `CompactIndicatorFormat` | `string` | `"Page {0} of {1}"` | Compact-mode button text: current page, page count. |
| `CompactThreshold` | `double` | `360` | Below this width the control switches to compact mode automatically. |
| `OverflowThreshold` | `double` | `560` | Below this width (and above `CompactThreshold`) it switches to overflow mode automatically. Must be greater than `CompactThreshold`. |
| `IsCompact` | `bool` | `false` | Forces compact mode on or off. |
| `IsOverflow` | `bool` | `false` | Forces overflow mode on or off. Compact wins over overflow. |

Behaviour:

- **Auto-hide.** The control collapses itself whenever `TotalItemCount` is no more than the
  smallest page-size option, because there is nothing to page and no smaller size to pick.
- **Forcing a mode.** The first change to `IsCompact` (or `IsOverflow`) turns off automatic
  switching for that mode for the rest of the control's life. Setting the property to its current
  value (for example `IsCompact="False"` in XAML) is not a change and does not disable it.
  These properties are inputs only; they do not report the automatically chosen mode.
- **Keyboard.** Enter in any "Go to" box jumps to that page; out-of-range or non-numeric input is
  ignored. In the flyouts a successful jump also closes the flyout.
- There is no page-changed event; use bindings or property-changed callbacks as above.
- Labels ("Rows:", "Go to:", "All", tooltips) are fixed English strings.

Tokens: `XcFontSizeBody`. The current page button uses `AccentButtonStyle`.

## WrapPanel

A panel that lays children left to right and wraps to a new line when the next child would
overflow. No breakpoints: give children a `MinWidth` and the panel reflows at any width.

```xml
<xc:WrapPanel ColumnSpacing="6" RowSpacing="6">
  <Border Padding="10,4" CornerRadius="12" Background="#30808080"><TextBlock Text="Alpha" /></Border>
  <Border Padding="10,4" CornerRadius="12" Background="#30808080"><TextBlock Text="Beta" /></Border>
</xc:WrapPanel>
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `ColumnSpacing` | `double` | `0` | Horizontal gap between children on a line. |
| `RowSpacing` | `double` | `0` | Vertical gap between lines. |
| `MeasureOverride` / `ArrangeOverride` | overrides | | Wrap layout. Children are measured against the full line width. With infinite width (inside a horizontal `StackPanel` or `ScrollViewer`) everything stays on one line. |

Each line is as tall as its tallest child; children are top-aligned within a line and keep their
desired width.

## ColumnFlowPanel

A masonry panel: equal-width columns, each child placed in whichever column is currently shortest
(ties go left). Use it to pack panels of different heights without the gaps a `WrapPanel` leaves.
Because of the shortest-column rule, markup order is only a rough guide to position.

```xml
<xc:ColumnFlowPanel MinColumnWidth="340" MaxColumns="2" ColumnSpacing="16" RowSpacing="16">
  <xc:CriteriaPanel Title="Name">...</xc:CriteriaPanel>
  <xc:CriteriaPanel Title="Address">...</xc:CriteriaPanel>
  <xc:CriteriaPanel Title="Dates">...</xc:CriteriaPanel>
</xc:ColumnFlowPanel>
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `MinColumnWidth` | `double` | `300` | Narrowest column before the panel drops a column. Column count = how many fit, clamped to 1..`MaxColumns`. |
| `MaxColumns` | `int` | `5` | Upper bound on columns. |
| `ColumnSpacing` | `double` | `0` | Gap between columns. |
| `RowSpacing` | `double` | `0` | Gap between children stacked in a column. |
| `MeasureOverride` / `ArrangeOverride` | overrides | | Column assignment is decided in measure and reused in arrange. With infinite width the panel uses one column of `MinColumnWidth`. |

Children are stretched to the column width.

## CriteriaPanel

A titled, bordered group of labelled fields for search or filter forms. Labels sit in a left column
that auto-sizes to the widest label in the panel; fields share one stretching right column, so every
field lines up.

```xml
<xc:CriteriaPanel Title="Customer">
  <TextBox xc:CriteriaPanel.Label="Name"  PlaceholderText="Starts with" />
  <TextBox xc:CriteriaPanel.Label="Email" PlaceholderText="Any part" />
  <ComboBox xc:CriteriaPanel.Label="Status" SelectedIndex="0">
    <x:String>Any</x:String>
    <x:String>Active</x:String>
  </ComboBox>
  <CheckBox Content="Has open orders" />

  <CalendarDatePicker xc:CriteriaPanel.Heading="Customer since" xc:CriteriaPanel.Label="From" />
  <CalendarDatePicker xc:CriteriaPanel.Label="To" />
</xc:CriteriaPanel>
```

The gallery builds the same thing from code:

```csharp
var name = new TextBox { PlaceholderText = "Starts with" };
CriteriaPanel.SetLabel(name, "Name");
Criteria.Fields.Add(name);
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `Title` | `string` | `""` | Heading above the group. Empty or whitespace collapses the heading row. |
| `Fields` | `ObservableCollection<FrameworkElement>` | empty | The fields in display order. XAML content property. Adding or removing rebuilds the rows. |
| `Label` (attached) | `string` | `""` | `GetLabel` / `SetLabel` / `LabelProperty`. Text in the label column. An empty label makes the field span both columns (the shape a `CheckBox` wants). |
| `Heading` (attached) | `string` | `""` | `GetHeading` / `SetHeading` / `HeadingProperty`. Inserts a full-width sub-heading row above this field, to separate groups inside one panel (two From/To pairs, for example). |

Behaviour:

- Use `CriteriaPanel.Label` instead of the field's own `Header`, or the label appears twice.
- The panel sets each field's `HorizontalAlignment` to `Stretch`.
- Rows are rebuilt on `Loaded` and whenever `Fields` changes. Changing an attached `Label` or
  `Heading` on a field that is already in the panel does not refresh by itself.

Theme resources: `DividerStrokeColorDefaultBrush` (border), `LayerFillColorDefaultBrush`
(background), `BodyStrongTextBlockStyle` (title and sub-headings, looked up in application
resources, so `XamlControlsResources` must be merged), `TextFillColorSecondaryBrush` (labels).

## ExpanderClipFix

An attached helper that fixes Uno's `Expander` clipping its content at a stale height after the
content grows while the expander is open (Uno 6.6 rebuilds the clip from the previous size). It
re-applies the clip after each layout pass; the clip itself is kept so the open/close animation still
hides overflowing content.

```xml
<Expander Header="Order entities" xc:ExpanderClipFix.IsEnabled="True">
  <ItemsRepeater ItemsSource="{x:Bind ViewModel.Entities}" />
</Expander>
```

```csharp
ExpanderClipFix.Apply(myExpander);
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `IsEnabled` (attached) | `bool` | `false` | `GetIsEnabled` / `SetIsEnabled` / `IsEnabledProperty`. Setting `true` on an `Expander` calls `Apply`. Setting it back to `false` does not detach the fix. |
| `Apply(Expander expander)` | `static void` | | Hooks the expander's `Loaded` and `SizeChanged` (idempotent) and the `ExpanderContentClip` template part's `SizeChanged`. |

It relies on the template part named `ExpanderContentClip`; with a custom template lacking that
part it does nothing.

## EditorActionBar

The standard button bar for an editor: a trash-icon **Delete** on the far left, an optional centre
slot, and **Save** (accent) plus **Cancel** pinned to the far right. The layout is fixed so every
editor looks the same.

```xml
<xc:EditorActionBar IsDirty="{x:Bind ViewModel.IsDirty, Mode=OneWay}"
                    CanSave="{x:Bind ViewModel.IsValid, Mode=OneWay}"
                    ShowDelete="True"
                    DeleteConfirmMessage="Delete this customer? This cannot be undone."
                    SaveRequested="OnSave"
                    CancelRequested="OnCancel"
                    DeleteRequested="OnDelete" />
```

From the gallery, driving `IsDirty` by hand:

```csharp
private void DirtyBox_TextChanged(object sender, TextChangedEventArgs e) => Actions.IsDirty = DirtyBox.Text.Length > 0;
private void Actions_SaveRequested(object sender, object e) { ActionEcho.Text = "Saved."; Actions.IsDirty = false; }
private void Actions_CancelRequested(object sender, object e) { DirtyBox.Text = ""; Actions.IsDirty = false; }
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `IsDirty` | `bool` | `false` | Whether the editor has unsaved changes. Gates Save and Cancel. |
| `CanSave` | `bool` | `true` | Extra validation gate, ANDed into Save only. |
| `AlwaysEnableCancel` | `bool` | `false` | Keeps Cancel enabled on a clean editor, for hosts where Cancel is also the way out of edit mode (in-list row editors). |
| `ShowSaveCancel` | `bool` | `true` | `false` collapses the Save/Cancel pair (read-only or locked modes). |
| `SaveText` | `string` | `"Save"` | Save button content (for example `"OK"`). |
| `CancelText` | `string` | `"Cancel"` | Cancel button content. |
| `ShowDelete` | `bool` | `false` | Shows the Delete button. |
| `IsDeleteEnabled` | `bool` | `true` | Disables Delete while keeping it visible. |
| `DeleteText` | `string` | `"Delete"` | Tooltip of the Delete button (it shows only an icon). The tooltip is applied when the value changes, so with the default value the button has no tooltip. |
| `DeleteConfirmTitle` | `string` | `"Delete?"` | Title of the built-in confirmation dialog. |
| `DeleteConfirmMessage` | `string?` | `null` | When non-empty, Delete shows a confirmation dialog first and raises `DeleteRequested` only on confirm. When empty, Delete raises immediately and the host owns confirmation. |
| `CenterContent` | `object?` | `null` | Extra content between Delete and Save/Cancel (an Unlock button, for example), left-aligned. A 12 px gutter is added only when it holds something. |
| `SaveRequested` | `EventHandler` | | Save clicked. |
| `CancelRequested` | `EventHandler` | | Cancel clicked. |
| `DeleteRequested` | `EventHandler` | | Delete clicked (after confirmation, when configured). |

Dirty-gating rules:

| State | Save | Cancel |
|---|---|---|
| `IsDirty = false` | disabled | disabled, unless `AlwaysEnableCancel` |
| `IsDirty = true`, `CanSave = false` | disabled | enabled |
| `IsDirty = true`, `CanSave = true` | enabled | enabled |

Behaviour:

- The bar raises events only. It does not reset `IsDirty`; the host does that after saving or
  reverting.
- The confirmation dialog has **Delete** and **Cancel** buttons with Cancel as the default, so
  Enter does not delete. It uses the application's `DefaultContentDialogStyle` when present.
- There are no keyboard accelerators (no Ctrl+S or Escape handling); buttons are reachable by Tab
  in the order Delete, centre content, Save, Cancel. The centre `ContentControl` itself is not a
  tab stop.

Theme resources: `AccentButtonStyle` (Save), `SymbolThemeFontFamily` (trash glyph U+E74D).

## CopyButton

A small copy-to-clipboard icon button. Clicking copies `Text` and shows a green "Copied" badge
that fades in, holds and fades out (about 1.6 s).

```xml
<TextBlock Text="{x:Bind Order.Number}" />
<xc:CopyButton Text="{x:Bind Order.Number}" ToolTipText="Copy the order number" />
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `Text` | `string?` | `null` | Text placed on the clipboard. When null or empty, clicking does nothing. |
| `Glyph` | `string` | `""` (Copy) | Segoe Fluent icon glyph. |
| `IconSize` | `double` | `12` | Icon font size. |
| `ToolTipText` | `string` | `"Copy to clipboard"` | Tooltip. |

Behaviour:

- Copies through [`ClipboardHelper.CopyText`](#clipboardhelper).
- The badge lives in a non-light-dismiss `Popup`, so it is not clipped by a parent `ScrollViewer`
  and does not swallow the next click. A rapid second click restarts the animation.
- Built on `HyperlinkButton`, so it is focusable and activates with Enter or Space.
- The badge text "Copied" is fixed.

Tokens and resources: `XcFontSizeCaption`, `SystemFillColorSuccessBackgroundBrush`,
`SystemFillColorSuccessBrush`.

## ToastControl

A transient confirmation message at the bottom centre of its host: fades in (150 ms), holds
(1.8 s), fades out (400 ms). It never takes input (`IsHitTestVisible = False`).

Place one instance as the **last child of the host's root `Grid`** so it overlays everything, then
call `Show`.

```xml
<Grid>
  <!-- page content -->
  <xc:ToastControl x:Name="Toast" />
</Grid>
```

```csharp
Toast.Show("Saved 3 changes");
```

| Member | Type | Purpose |
|---|---|---|
| `Show(string message)` | method | Shows the message. Calling it again while a toast is visible replaces the text and restarts the timing. |

The text is single-line, trimmed with an ellipsis at 360 px. The leading check-mark icon is fixed.

Theme resources: `SystemControlBackgroundChromeMediumBrush`, `DividerStrokeColorDefaultBrush`,
`AccentTextFillColorPrimaryBrush`.

## OverlayGlyphIcon

An icon made of a large back glyph with a small round accent badge overlaid at the bottom-right
("add customer", "edit document", and so on). The back icon is either a Fluent glyph or a vector
the app resolves from a key.

```xml
<StackPanel Orientation="Horizontal" Spacing="16">
  <xc:OverlayGlyphIcon BackGlyph="&#xE77B;" FrontGlyph="&#xE710;" Size="32" />
  <xc:OverlayGlyphIcon BackGlyph="&#xE8A5;" FrontGlyph="&#xE70F;" Size="32" />
  <xc:OverlayGlyphIcon BackGeometryKey="star" FrontGlyph="&#xE710;" Size="32" />
</StackPanel>
```

```csharp
// The third icon above resolves "star" through the host hook.
ComponentHost.ResolveGeometry = key => key == "star"
    ? (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry),
        "M12,2 L15,9 L22,9.5 L16.5,14.5 L18,22 L12,18 L6,22 L7.5,14.5 L2,9.5 L9,9 Z")
    : null;
```

| Member | Type | Default | Purpose |
|---|---|---|---|
| `BackGlyph` | `string` | `""` | Fluent glyph for the main icon. |
| `BackGeometryKey` | `string` | `""` | Key resolved through `ComponentHost.ResolveGeometry` into `BackData`. Resolved when the key changes, so set the resolver before the control is created. |
| `BackData` | `Geometry?` | `null` | Read-only. The resolved vector, drawn uniformly stretched. |
| `FrontGlyph` | `string` | `""` (Add) | Badge glyph. |
| `Size` | `double` | `24` | Overall icon size. The badge is half this size and its glyph 0.4 of it. |
| `FrontSizeRatio` | `double` | `0.45` | Declared, but the default template does not use it; the badge ratios above are fixed. |
| `BackForeground` | `Brush` | style: `TextFillColorPrimaryBrush` | Colour of the back glyph or vector. |
| `FrontBackground` | `Brush` | style: `AccentFillColorDefaultBrush` | Badge fill. |
| `FrontForeground` | `Brush` | style: `TextOnAccentFillColorPrimaryBrush` | Badge glyph colour. |

Behaviour:

- This is a templated `Control`; its template comes from the package `Generic.xaml`. Without that
  dictionary merged it renders nothing.
- Both back layers are always in the template; set either `BackGlyph` or `BackGeometryKey`, not
  both.
- The icon is not hit-testable and not focusable; put it inside a `Button` for interaction.

---

## InputMask

Static, side-effect-free helpers behind `MaskedTextBox`, usable on their own (for example to
format a stored phone number for display). Grammar: `#` is one digit, anything else is a literal.
None of the methods throw or reject; input is reduced to its digits and laid out against the mask.

```csharp
InputMask.Format("5551234567", "(###) ###-####");             // "(555) 123-4567"
InputMask.Format("55512", "(###) ###-####");                  // "(555) 12"
InputMask.FormatTemplate("55512", "(###) ###-####", '_');     // "(555) 12_-____"
```

| Method | Returns | Purpose |
|---|---|---|
| `HasMask(string? mask)` | `bool` | True when the mask is non-blank and contains at least one `#`. |
| `PlaceholderCount(string? mask)` | `int` | Number of `#` slots. |
| `ExtractDigits(string? raw, int max)` | `string` | The first `max` digits of `raw`, in order. |
| `Format(string? raw, string? mask)` | `string` | The value to store: digits laid into the mask, literals only up to the last entered digit, no placeholders. Returns `raw` unchanged when the mask is unusable, and `""` when `raw` has no digits. |
| `FormatTemplate(string? raw, string? mask, char placeholder)` | `string` | The full display template: digits fill slots left to right, the rest show `placeholder`, literals always shown. |

## ClipboardHelper

```csharp
ClipboardHelper.CopyText(shareLink);
```

| Method | Purpose |
|---|---|
| `static void CopyText(string? text)` | Puts `text` on the clipboard as a `DataPackage`. Null or empty is a no-op. On WebAssembly the browser requires a user gesture, so call it from a click handler. |

## Value converters

Public `IValueConverter` implementations, used by the package's own templates and available to
apps. Declare them in your own resources under your own keys.

```xml
<xc:BoolToVisibilityConverter x:Key="BoolToVisibility" />
<TextBlock Visibility="{x:Bind ViewModel.HasError, Mode=OneWay, Converter={StaticResource BoolToVisibility}}" />
<TextBlock Visibility="{x:Bind ViewModel.HasError, Mode=OneWay, Converter={StaticResource BoolToVisibility}, ConverterParameter=invert}" />
```

| Converter | Members | Convert | ConvertBack |
|---|---|---|---|
| `BoolToVisibilityConverter` | | `true` to `Visible`, anything else to `Collapsed`. `ConverterParameter="invert"` (case-insensitive) flips it. | `Visible` to `true`. Ignores the invert parameter. |
| `BoolToBrushConverter` | `TrueBrush`, `FalseBrush` (`Brush?`, default `null`) | Picks the brush for the bool. If that brush is null, the other one is used; if both are null, an empty `SolidColorBrush`. | Throws `NotSupportedException`. |
| `ColorToBrushConverter` | | A `Color` becomes a `SolidColorBrush`. A non-colour value falls back to the brush resource named by `ConverterParameter`, resolved for the effective theme through `ComponentHost.TryGetBrush`; if that is missing too, hot pink so the gap is visible. | `SolidColorBrush` to its `Color`, else `Transparent`. |
| `MultiplyConverter` | | A `double` times the numeric `ConverterParameter` (invariant culture). Other values pass through. Registered in `Generic.xaml` as `XcMultiplyConverter`. | Throws `NotSupportedException`. |

---

## Theming

Every control in this package takes its colours from WinUI theme resources (`ThemeResource`) and
follows light, dark and live theme switches without extra code. The package-specific knobs are:

- **Font sizes**: `XcFontSizeBody` and `XcFontSizeCaption` (from Core), used by `PagingControl`,
  `ChipPickerControl`, `MonthYearPicker`, `DateTimePicker` and `CopyButton`. Override them in
  `App.xaml` after the merge.
- **Styles**: `XcFieldLabelStyle` and `XcListViewItemStyle` (`DateTimePicker`),
  `XcListSectionHeaderTextStyle` (`ChipPickerDialog`).
- **Calendar colours**: optional `XcCalendar<Role>Brush` overrides for `MultiMonthDateNavigator`
  and `DateTimePicker`, theme-specific when placed in `ThemeDictionaries`.
- **App-supplied**: `XtremeExcludedBrush`, needed only when `ChipPickerControl.AllowExclude` is on.
- **Code-built dialogs** (`ChipPickerDialog`, the `EditorActionBar` delete confirmation) pick up
  `DefaultContentDialogStyle`; set `ComponentHost.CurrentTheme` if your app drives its theme from a
  root element rather than the OS.

Some values are fixed in code rather than tokenised: `CompactCalendarDatePicker` font sizes, the
`ChipItem` default pill colours, the NOT badge red in the chip button, and the English UI strings
in `PagingControl`, `DateTimePicker`, `CopyButton` and `EditorActionBar`.

For merge order, the full token tables, high-contrast surfaces and
`ComponentHost.NotifyResourcesChanged()`, see [theming.md](theming.md).
