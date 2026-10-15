# Cards

Package: `Xtreme.Components.Cards` (namespace `Xtreme.Components.Cards`). Depends on
`Xtreme.Components.Core` only. Targets `net10.0`; the consuming Uno head supplies the renderer.

Two independent controls:

- `SectionCard` (with `SectionCardTab`) draws the boxed frame used to group fields in an editor
  or detail pane, optionally with a tab strip.
- `CardDockerControl` (with `CardDockerCard`, `CardDockerPanel`, `CardDockerLayout`) lets the user
  arrange cards into panels across a column grid by dragging. It edits a layout; it does not
  render card bodies.

Live demo: [gallery, Cards page](https://xtremecrm.github.io/Xtreme.Components/).
Sample source: [`CardsPage.cs`](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/Pages/CardsPage.cs)
and the appointment editor in [`SchedulerPage.cs`](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/Pages/SchedulerPage.cs).

## Contents

- [Setup](#setup)
- [SectionCard](#sectioncard)
  - [Modes](#modes)
  - [XAML](#xaml)
  - [C#](#c)
  - [Composed example: an appointment editor](#composed-example-an-appointment-editor)
  - [SectionCard members](#sectioncard-members)
  - [Header fitting](#header-fitting)
  - [LabelAbbreviation](#labelabbreviation)
  - [SectionCardTab members](#sectioncardtab-members)
  - [SectionCard behavior](#sectioncard-behavior)
- [CardDockerControl](#carddockercontrol)
  - [Model](#model)
  - [Usage](#usage)
  - [CardDockerControl members](#carddockercontrol-members)
  - [Model members](#model-members)
  - [Normalization rules](#normalization-rules)
  - [Interaction](#interaction)
  - [Keyboard](#keyboard)
  - [Persisting a layout](#persisting-a-layout)
- [Theming](#theming)
- [Known limitations](#known-limitations)
- [Member reference](#member-reference)

## Setup

```bash
dotnet add package Xtreme.Components.Cards --prerelease
```

Merge the Core token dictionary once in `App.xaml` (see [theming.md](theming.md#1-merge-the-dictionaries)).
The Cards package has no `Generic.xaml` of its own; it reads Core's `XcFontSizeTitle` and the
`ComponentTheme` surface brushes. Package layout and dependency rules are in
[architecture.md](architecture.md#package-map).

```xml
xmlns:cards="using:Xtreme.Components.Cards"
```

## SectionCard

A `UserControl` that owns the card chrome: card fill, themed 1px stroke, 6px corner, 12px inner
padding, and a right/bottom edge drawn 1.5px deeper in a darker stroke for a slightly raised
look. Every card built with it matches, so hosts supply content only.

### Modes

The mode follows what the card holds: it is decided when the card first loads, and changes later
if tabs are added to a card that had none or the last tab is removed.

| Mode | Set | Header | Body shown |
|---|---|---|---|
| Tabbed | one or more `Tabs` | top-pane `NavigationView` strip, one item per tab (shown even for a single tab) | the selected tab's `Content` |
| Titled | `Title` and `Body`, no tabs | plain semibold `TextBlock` (no tab stops) | `Body` |
| Headerless | `Body` only | none | `Body` |

`Title` and `Body` are not shown in tabbed mode, but they are kept: remove the last tab and the
card shows them again.

### XAML

`Body` is the XAML content property, so a direct child becomes the body. Tabs must go inside the
`SectionCard.Tabs` property element:

```xml
<!-- Tabbed -->
<cards:SectionCard>
  <cards:SectionCard.Tabs>
    <cards:SectionCardTab Header="Details">
      <StackPanel Spacing="4">
        <TextBlock Text="Name: Alex Rivera" />
        <TextBlock Text="Type: Customer" />
      </StackPanel>
    </cards:SectionCardTab>
    <cards:SectionCardTab Header="Notes">
      <cards:SectionCardTab.Icon>
        <SymbolIcon Symbol="Comment" />
      </cards:SectionCardTab.Icon>
      <TextBox AcceptsReturn="True" />
    </cards:SectionCardTab>
  </cards:SectionCard.Tabs>
</cards:SectionCard>

<!-- Titled -->
<cards:SectionCard Title="Billing">
  <TextBlock Text="Net 30" />
</cards:SectionCard>

<!-- Headerless -->
<cards:SectionCard>
  <TextBlock Text="Body only, no tab strip." />
</cards:SectionCard>
```

`SectionCardTab` also declares `Content` as its content property, so the tab's single child is
its body.

### C#

Trimmed from `CardsPage.cs`:

```csharp
var tabbed = new SectionCard();
tabbed.Tabs.Add(new SectionCardTab { Header = "Details", Icon = new SymbolIcon(Symbol.Contact), Content = Lines("Name: Alex Rivera", "Type: Customer") });
tabbed.Tabs.Add(new SectionCardTab { Header = "Notes",   Icon = new SymbolIcon(Symbol.Comment), Content = Lines("Prefers morning visits.") });
tabbed.SelectionChanged += (_, _) => status.Text = $"Selected: {tabbed.SelectedTab?.Header}";

// Later, at any time: the strip and the selection follow.
tabbed.Tabs.Add(new SectionCardTab { Header = "Extra", Content = Lines("Added at runtime.") });
tabbed.SelectedIndex = tabbed.Tabs.Count - 1;

var headerless = new SectionCard
{
    Body = Lines("Headerless card: Body only, no tab strip."),
};
```

`Tabs`, `Title`, `Body` and every tab property can be changed before or after the card loads (see
[behavior](#sectioncard-behavior)).

### Composed example: an appointment editor

The Scheduler gallery page builds its side editor from a headerless `SectionCard` wrapping a
`ScrollViewer` of fields, with `DateTimePicker` and `EditorActionBar` from
`Xtreme.Components.Primitives`. Trimmed:

```csharp
var fields = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
fields.Children.Add(_editorTitle);                                   // TextBlock, SubtitleTextBlockStyle
fields.Children.Add(_subject = new TextBox { Header = "Subject" });
fields.Children.Add(_allDay  = new CheckBox { Content = "All day" });
fields.Children.Add(_start   = new Primitives.DateTimePicker { Header = "Start", MinuteStep = 15 });
fields.Children.Add(_end     = new Primitives.DateTimePicker { Header = "End",   MinuteStep = 15 });
fields.Children.Add(_memo    = new TextBox { Header = "Memo", AcceptsReturn = true, MinHeight = 80 });
fields.Children.Add(_actions = new Primitives.EditorActionBar());   // Save / Cancel / Delete

_editorPane.Child = new SectionCard
{
    Body = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
};
```

The card is created once and its pane's `Visibility` toggled to open and close the editor; the
fields are reset in place rather than rebuilding the card. Because the body is a `ScrollViewer`,
the chrome stays put while long forms scroll inside it.

### SectionCard members

`public sealed partial class SectionCard : UserControl`, `[ContentProperty(Name = "Body")]`.

| Member | Kind | Type | Default | Purpose |
|---|---|---|---|---|
| `SectionCard()` | constructor | | | Builds the chrome; the header is first built on `Loaded`. |
| `Tabs` | property (get-only, not a DP) | `IList<SectionCardTab>` | empty list | Tabs for tabbed mode. Observable: adding or removing tabs at any time rebuilds the strip. |
| `Body` | dependency property | `object?` | `null` | Content shown while there are no tabs. |
| `BodyProperty` | static readonly field | `DependencyProperty` | | Identifier for `Body`. |
| `Title` | dependency property | `string?` | `null` | Plain header label shown while there are no tabs. |
| `TitleProperty` | static readonly field | `DependencyProperty` | | Identifier for `Title`. |
| `SelectedIndex` | dependency property | `int` | `0` | Index of the selected tab. Two-way: follows the user's pick. Reads `-1` once loaded with no tabs. |
| `SelectedIndexProperty` | static readonly field | `DependencyProperty` | | Identifier for `SelectedIndex`. |
| `SelectedTab` | property (get-only) | `SectionCardTab?` | `null` | The selected tab; `null` before load or with no tabs. |
| `SelectionChanged` | event | `EventHandler?` | | Raised when `SelectedTab` changes. Read `SelectedTab` / `SelectedIndex` for the result. |
| `HeaderFit` | dependency property | `SectionCardHeaderFit` | `Overflow` | What the strip does when the headers do not fit: move tabs into the overflow menu (`Overflow`), or shorten the headers first (`Abbreviate`). See [header fitting](#header-fitting). |
| `HeaderFitProperty` | static readonly field | `DependencyProperty` | | Identifier for `HeaderFit`. |

### Header fitting

By default a strip whose headers do not fit behaves like any top-pane `NavigationView`: the tabs
that do not fit move into its "More" overflow menu. Set `HeaderFit="Abbreviate"` and the card
shortens the headers instead, keeping every tab one click away:

```xml
<cards:SectionCard HeaderFit="Abbreviate">
  <cards:SectionCard.Tabs>
    <cards:SectionCardTab Header="Contact Details"> ... </cards:SectionCardTab>
    <cards:SectionCardTab Header="Payment Accounts"> ... </cards:SectionCardTab>
  </cards:SectionCard.Tabs>
</cards:SectionCard>
```

```csharp
card.HeaderFit = SectionCardHeaderFit.Abbreviate;
```

How it works:

- **Measured, not tabulated.** After every layout that changes the strip's width (and after tabs,
  headers, icons or the selection change) the card measures each header at the font the strip
  really uses, reads each tab's chrome (padding, icon, gap) and the strip's overhead from the
  laid-out `NavigationView`, and picks the **largest letter count, shared by all tabs, at which the
  strip fits**. A different font size, theme or `NavigationViewItem` style needs no retuning.
- **Shared count, short headers whole.** Every header is cut to the same number of letters with
  [`LabelAbbreviation`](#labelabbreviation); a header already that short stays whole, so the long
  ones give way first ("Contact Details" becomes "Contact Det.." while "Notes" stays "Notes").
- **Icon only last.** At zero letters a tab with an `Icon` shows its icon alone. A tab without an
  icon keeps at least one letter.
- **Overflow as the final step.** Only when even the shortest form does not fit do tabs move into
  NavigationView's overflow menu.
- **Full name stays available.** A shortened tab gets its full header as a tooltip, and every tab's
  automation name is its full header, so screen readers are unaffected.
- **One pass per change.** The fit runs at low priority once layout has settled (so the first frame
  after a resize can still show the previous headers) and lays the strip out again on the spot. If
  NavigationView had already moved tabs into its overflow menu (it only brings them back once
  there is room for its "More" button too), the card re-adds the same items so the strip is laid
  out from scratch with the shortened headers.
- **No wasted column.** The card's NavigationView sets `TopNavigationViewPaneCustomContentMinWidth`
  to 0, so the 48px the top pane normally reserves for `PaneCustomContent` (which a card never
  sets) goes to the tabs. This applies in both modes.

Switching `HeaderFit` back to `Overflow` restores the full headers.

### LabelAbbreviation

`public static class LabelAbbreviation`: the shortening rule the card uses, public so a host can
shorten its own buttons and labels the same way.

| Member | Kind | Purpose |
|---|---|---|
| `Suffix` | `const string` (`".."`) | Appended to a shortened label. |
| `Abbreviate(string? text, int maxLetters)` | static method, returns `string` | The first `maxLetters` letters (trailing spaces trimmed) plus `Suffix`. Empty when `maxLetters` is zero or less (show the icon alone). The text comes back whole when cutting would not make it shorter: `"Notes"` at 3 letters stays `"Notes"`, not `"Not.."`. |

```csharp
LabelAbbreviation.Abbreviate("Payment Accounts", 8);   // "Payment.."
LabelAbbreviation.Abbreviate("Addresses", 0);          // "" (icon only)
```

### SectionCardTab members

`public sealed partial class SectionCardTab : DependencyObject`, `[ContentProperty(Name = "Content")]`.

| Member | Kind | Type | Default | Purpose |
|---|---|---|---|---|
| `SectionCardTab()` | constructor | | | |
| `Header` | dependency property | `string?` | `null` | Label in the tab strip (`null` shows an empty label). |
| `HeaderProperty` | static readonly field | `DependencyProperty` | | Identifier for `Header`. |
| `Icon` | dependency property | `IconElement?` | `null` | Icon before the label, passed to `NavigationViewItem.Icon`. One element per tab (an element has one parent). |
| `IconProperty` | static readonly field | `DependencyProperty` | | Identifier for `Icon`. |
| `Content` | dependency property | `object?` | `null` | The tab body. |
| `ContentProperty` | static readonly field | `DependencyProperty` | | Identifier for `Content`. |

### SectionCard behavior

- **Built on load, live after.** The header is first built in the `Loaded` handler (XAML fills
  `Tabs` after the constructor, so building earlier would miss them). Changes made before load are
  simply picked up then. After load:
  - adding, removing or replacing `Tabs` rebuilds the strip. The selected tab stays selected if it
    is still present; if it was removed, the tab now at its index (clamped) is selected. Removing
    the last tab switches the card to titled/headerless mode, adding one to a card without tabs
    switches it to tabbed mode;
  - changing `Title` updates the label (shown only while there are no tabs);
  - changing a tab's `Header` or `Icon` updates that item in place;
  - changing the selected tab's `Content` replaces the body immediately; changing another tab's
    `Content` shows when that tab is selected;
  - changing `Body` updates the card only while it has no tabs. With tabs, `Body` is stored and
    shown again if the tabs go away; it is never shown alongside tabs (the tabs already own the
    body area, and mixing the two would make the selected tab's content ambiguous).

  The card is built once: moving it in the tree (unload and reload) keeps its state and selection.
- **Selection.** `SelectedIndex` defaults to `0`, so the first tab is selected on load; a value set
  before load is applied then (clamped into range). Setting it after load selects that tab; a
  value outside `Tabs` is ignored and snaps back to the current tab. `SelectionChanged` fires
  whenever `SelectedTab` changes, including the initial selection on load and a move caused by
  removing the selected tab, but not when only the index shifts because a tab was inserted or
  removed before the selected one.
- **Tabs via NavigationView.** The strip is a `NavigationView` with `PaneDisplayMode="Top"`,
  back/settings/pane-toggle buttons hidden and a transparent background. Each tab is a
  `NavigationViewItem` whose `Tag` is the `SectionCardTab`. NavigationView was chosen over
  `SelectorBar` for its built-in top overflow, hierarchy and per-tab icon support; overflow (items
  that do not fit move into NavigationView's own overflow menu) and icons are used today. With
  `HeaderFit="Abbreviate"` the headers shorten before overflow is used (see
  [header fitting](#header-fitting)).
- **Lazy tab bodies.** `SectionCardTab` is a `DependencyObject`, not a control, so its `Content`
  is held as data and parented into the card only while selected. Unselected tab bodies are out of
  the visual tree; switching tabs unloads one body and loads the other (control state such as
  text is kept, since the same instances are reused). Do not put the same element in two tabs or
  in a tab and elsewhere. The same applies to `Icon`.
- **Bindings.** Tab content inherits the card's `DataContext` once parented. A `{Binding}` set on
  `SectionCardTab.Header` itself has no inherited `DataContext` (the tab is not in the tree); set
  `Header` from code (or bind with an explicit `Source`) if it must change.
- **Layout.** `HorizontalAlignment` defaults to `Stretch`. Header and body sit in two `Auto` rows
  with 8px spacing, so the card sizes to its content; give the body its own `ScrollViewer` for
  long content.
- **Actions.** Per-list actions (an Add button and so on) belong at the bottom of the tab's own
  content; the card header has no action slot.

## CardDockerControl

A `ContentControl` that renders a layout of cards as chips: panels stacked in columns, each panel
holding one or more cards side by side, plus a "Set aside" tray. The user rearranges by dragging
chips, moves whole panels by dragging their top handle, resizes panels by dragging a grip, and can
do the moves from the keyboard. The control never sees card bodies: a card is an
opaque string id with a title and an icon factory. The host owns what an id means, renders the
real cards elsewhere from the resulting layout, and stores the layout.

### Model

```
CardDockerLayout
 ├── Panels: List<CardDockerPanel>
 │     └── Id, Column (0-based), Sort (top to bottom), PreferredColumn, MinHeight, CardIds (left to right)
 └── TrayCardIds: List<string>       cards set aside, in no panel
```

A layout describes one arrangement at one column count. A host that keeps a different
arrangement per window width owns that mapping and hands the control whichever one is being
edited.

### Usage

Trimmed from `CardsPage.cs`:

```csharp
var docker = new CardDockerControl();

docker.Cards =
[
    new CardDockerCard { Id = "name",      Title = "Name",      CreateIcon = () => new SymbolIcon(Symbol.Contact) },
    new CardDockerCard { Id = "addresses", Title = "Addresses", CreateIcon = () => new SymbolIcon(Symbol.Map) },
    new CardDockerCard { Id = "notes",     Title = "Notes",     CreateIcon = () => new SymbolIcon(Symbol.Comment) },
    new CardDockerCard { Id = "contacts",  Title = "Contacts",  CreateIcon = () => new SymbolIcon(Symbol.Mail) },
    new CardDockerCard { Id = "custom",    Title = "Custom Fields", CreateIcon = () => new SymbolIcon(Symbol.Setting) },
];
docker.ColumnCount = 3;
docker.Layout = new CardDockerLayout
{
    Panels =
    [
        new CardDockerPanel { Id = "p1", Column = 0, Sort = 0, CardIds = { "name" } },
        new CardDockerPanel { Id = "p2", Column = 0, Sort = 1, CardIds = { "addresses" } },
        new CardDockerPanel { Id = "p3", Column = 1, Sort = 0, CardIds = { "notes", "contacts" } },
    ],
    TrayCardIds = { "custom" },
};
docker.LayoutChanged += (_, _) => Save(docker.Layout);

// The control grows with its panels; host it in a vertical scroller.
var scroller = new ScrollViewer { Content = docker, HorizontalScrollMode = ScrollMode.Disabled };
```

`Cards`, `Layout` and `ColumnCount` can be assigned in any order; each assignment re-normalizes
and redraws. `Layout` is a value: the setter copies what it is given and the getter returns a
fresh copy, so to change the arrangement in code, get it, edit it, and set it back:

```csharp
var layout = docker.Layout;                         // a copy
layout.Panels.First(p => p.Id == "p2").MinHeight = 240;
docker.Layout = layout;                             // applied, normalized, redrawn
```

The fixed English strings and the drag colors are properties:

```csharp
docker.TrayHeader = "Hidden cards";
docker.TrayPlaceholderText = "Drop a card here to hide it.";
docker.NewPanelText = "Drop here for a new panel";
docker.DropTargetBackground = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x78, 0xD4));
```

Per-panel action button (for example "hide this panel at this width"):

```csharp
docker.ShowPanelAction = true;
docker.PanelActionLabel = "Hide";
docker.PanelActionInvoked += (_, panelId) => HideAtCurrentWidth(panelId);
```

The control only raises the event; whatever the action means lives in the host's own model.

XAML works for the scalar properties; `Cards` and `Layout` are plain CLR properties holding model
objects and are normally set from code:

```xml
<cards:CardDockerControl x:Name="Docker" ColumnCount="2" ShowPanelAction="True" PanelActionLabel="Hide" />
```

### CardDockerControl members

`public partial class CardDockerControl : ContentControl`. Sets its own `Content`; do not replace it.

| Member | Kind | Type | Default | Purpose |
|---|---|---|---|---|
| `CardDockerControl()` | constructor | | | Builds the column grid, tray and drag overlay. |
| `Cards` | property | `IReadOnlyList<CardDockerCard>` | empty | Every card that can be arranged. `null` is treated as empty. Setting it re-normalizes and redraws. |
| `Layout` | property | `CardDockerLayout` | new empty layout | The arrangement, as a value. The setter stores a normalized **copy** (`null` becomes an empty layout); the getter returns a new **copy** on every read. |
| `ColumnCount` | dependency property | `int` | `3` | Number of columns. Changing it re-normalizes and redraws; lowering it and raising it again restores the arrangement (see [normalization](#normalization-rules)). |
| `ColumnCountProperty` | static readonly field | `DependencyProperty` | | Identifier for `ColumnCount`. |
| `ShowPanelAction` | dependency property | `bool` | `false` | Shows a small text button at the top of every panel. |
| `ShowPanelActionProperty` | static readonly field | `DependencyProperty` | | Identifier for `ShowPanelAction`. |
| `PanelActionLabel` | dependency property | `string` | `"Hide"` | Text on the per-panel button. |
| `PanelActionLabelProperty` | static readonly field | `DependencyProperty` | | Identifier for `PanelActionLabel`. |
| `TrayHeader` | dependency property | `string` | `"Set aside"` | Heading of the tray. |
| `TrayHeaderProperty` | static readonly field | `DependencyProperty` | | Identifier for `TrayHeader`. |
| `TrayPlaceholderText` | dependency property | `string` | `"Drag a card here to set it aside."` | Hint shown while the tray is empty. |
| `TrayPlaceholderTextProperty` | static readonly field | `DependencyProperty` | | Identifier for `TrayPlaceholderText`. |
| `NewPanelText` | dependency property | `string` | `"New panel"` | Label of the empty slot at the foot of each column. |
| `NewPanelTextProperty` | static readonly field | `DependencyProperty` | | Identifier for `NewPanelText`. |
| `DropTargetBackground` | dependency property | `Brush?` | `null` (built-in `#802D5B8A`) | Fill of the wash over the drop target while dragging. |
| `DropTargetBackgroundProperty` | static readonly field | `DependencyProperty` | | Identifier for `DropTargetBackground`. |
| `DropTargetBorderBrush` | dependency property | `Brush?` | `null` (built-in `#E04A82C4`) | Outline of that wash. |
| `DropTargetBorderBrushProperty` | static readonly field | `DependencyProperty` | | Identifier for `DropTargetBorderBrush`. |
| `InsertionMarkerBrush` | dependency property | `Brush?` | `null` (built-in `#FF4A82C4`) | The bar marking the exact seam a card or panel lands in. |
| `InsertionMarkerBrushProperty` | static readonly field | `DependencyProperty` | | Identifier for `InsertionMarkerBrush`. |
| `LayoutChanged` | event | `EventHandler?` | | Raised after a user edit (drop, panel move, keyboard move, panel resize) that changed the arrangement. Never raised by assigning `Cards`, `Layout` or `ColumnCount`. Read `Layout` for the result. |
| `PanelActionInvoked` | event | `EventHandler<string>?` | | Raised when a panel's action button is clicked; the argument is the panel id. |
| `MinPanelHeight` | `const int` | | `60` | Floor for `CardDockerPanel.MinHeight`. |
| `MaxPanelHeight` | `const int` | | `1200` | Ceiling for `CardDockerPanel.MinHeight`. |

### Model members

`CardDockerCard` (`sealed class`)

| Member | Type | Default | Purpose |
|---|---|---|---|
| `Id` | `string` (`required`, `init`) | | Opaque id, compared ordinally. Meaning is the host's. |
| `Title` | `string` (`required`, `init`) | | Text on the chip. Trimmed with an ellipsis. |
| `CreateIcon` | `Func<IconElement?>?` (`init`) | `null` | Factory for the chip icon, drawn at 16x16. Called on every redraw, so it must return a **new** element each time; returning a cached instance would leave it parented elsewhere. |

`CardDockerPanel` (`sealed class`)

| Member | Type | Default | Purpose |
|---|---|---|---|
| `Id` | `string` (`required`, `init`) | | Panel id. Panels the control creates are named `p1`, `p2`, ... (first unused). |
| `Column` | `int` (`get; set`) | `0` | Zero-based column. Clamped to `0..ColumnCount-1`. |
| `Sort` | `int` (`get; set`) | `0` | Top-to-bottom position in the column. Rewritten densely (0, 1, 2, ...) on every normalization. |
| `PreferredColumn` | `int?` (`get; set`) | `null` | The column a panel parked by a lower `ColumnCount` returns to when the count rises again. Set and cleared by the control; `null` means the panel is where it belongs. |
| `CardIds` | `List<string>` (`init`) | empty | Card ids, left to right. |
| `MinHeight` | `int` (`get; set`) | `150` | Panel minimum height in DIPs. Clamped to `MinPanelHeight..MaxPanelHeight`. |

`CardDockerLayout` (`sealed class`)

| Member | Type | Default | Purpose |
|---|---|---|---|
| `Panels` | `List<CardDockerPanel>` (`init`) | empty | The panels. |
| `TrayCardIds` | `List<string>` (`init`) | empty | Cards set aside. |
| `Clone()` | method, returns `CardDockerLayout` | | Deep copy of panels (including `CardIds`) and tray. |

### Normalization rules

Every assignment to `Cards`, `Layout` or `ColumnCount`, and every user change, runs the same
pass on the control's layout before redrawing:

1. Ids not present in `Cards` are removed from panels and the tray. A card listed more than once
   (two panels, or a panel and the tray) keeps only its first occurrence, panels before tray.
2. Panels left with no cards are removed.
3. Cards that appear nowhere are appended to the tray, in `Cards` order. The control never invents
   a panel for a new card; the host decides where new cards go by placing them in the layout it
   assigns.
4. `MinHeight` is clamped to `60..1200` and a negative `Column` to `0`.
5. Columns past the last are folded into the last column (see below), with `PreferredColumn`
   recording where each such panel came from. A panel whose `PreferredColumn` fits the current
   count goes back there and the property is cleared.
6. `Sort` is rewritten densely per column.

Consequences worth knowing:

- **Lowering and raising `ColumnCount` is a round trip.** Lowering it parks panels from the removed
  columns at the bottom of the last column, in their original column-then-position order, and sets
  their `PreferredColumn`. Raising it again sends each one back to its column. Going down in steps
  (3 to 2 to 1) parks in the same order as going down at once, and going part of the way back up
  restores what fits and keeps the rest parked. Moving a parked panel by hand (drag or keyboard)
  clears its `PreferredColumn`: the user has placed it, so it stays put. Dragging cards in or out
  of a parked panel does not.
- **No event for assignments.** Normalization during an assignment does **not** raise
  `LayoutChanged`, even when it rewrote the layout. The host made that change; if it needs the
  normalized form (for example to save it), it reads `Layout` right after assigning. This is
  deliberate: a host that edits one arrangement per width assigns `ColumnCount`, then `Cards`, then
  `Layout`, and an event at each step would report the previous width's layout normalized against
  the next width as if the user had edited it.
- **No event for no-ops.** A user edit whose normalized result equals the layout before it (a chip
  dropped on its own seam, a panel dragged back to its own slot, a lone card dropped on the "New
  panel" slot of the column it already ends, a resize released at the starting height) redraws but
  does not raise `LayoutChanged`.
- **Empty clears.** Assigning empty `Cards` and an empty `Layout` redraws to empty columns (each
  with its "New panel" slot) and an empty tray; nothing from the previous arrangement remains.

### Interaction

- **Drag a chip.** Press a chip and move at least 6px (either axis) to start a drag; a shorter
  press does nothing (chips raise no click). Drag runs on raw pointer handlers with pointer
  capture, not XAML drag-and-drop or manipulation events, so it behaves the same on every head
  and mouse/touch/pen. Drop targets are snapshotted once at drag start; the smallest target under
  the pointer wins.
- **Card drop targets.**

  | Target | Result |
  |---|---|
  | Left or right half of a chip in a panel | Insert before or after that chip (reorders within a panel, or moves between panels). An insertion bar marks the seam. |
  | "New panel" slot at the foot of each column | Puts the card in a new panel at the bottom of that column. If the card was alone in its panel, that panel moves instead (same id and height). |
  | "Set aside" tray | Moves the card to the end of `TrayCardIds`. |
  | Anywhere else | Cancels; nothing changes. |

  A translucent blue wash (the same as the Docking package's landing preview, recolorable with
  `DropTargetBackground` / `DropTargetBorderBrush`) covers the target while hovering.
- **Drag a panel.** Each panel's top strip (the short bar above its cards) is a handle. Drag it the
  same way (6px threshold) to move the whole panel: drop on the top or bottom half of any panel to
  land above or below it, or on a column's "New panel" slot to land at that column's foot. A
  horizontal insertion bar marks the seam. Panels cannot be dropped on the tray. When
  `ShowPanelAction` is on, the action button sits at the right end of the same strip.
- **Resize a panel.** Each panel has a grip along its bottom edge. Dragging it changes the panel's
  `MinHeight` live, clamped to `60..1200`; the layout is committed on release, and `LayoutChanged`
  is raised only if the height changed.
- **Capture loss.** If pointer capture is lost mid-drag, the drag is abandoned with no change.
- **Events.** `LayoutChanged` fires once per completed user edit that changed the arrangement (see
  [normalization](#normalization-rules)). Debounce in the host if saving is expensive.
- **Theme.** A theme change redraws the control (arrangement unchanged).

### Keyboard

Every chip and every panel handle is a tab stop (with the system focus visual) and carries an
automation name (the card title; for a handle, the titles of its cards). After a keyboard move the
moved card or panel keeps focus.

| Focus | Keys | Result |
|---|---|---|
| Card in a panel | Ctrl+Left / Ctrl+Right | Swap with the neighboring card in the panel. |
| Card in a panel | Ctrl+Up / Ctrl+Down | Move to the end of the panel above / below in the same column. Ctrl+Down from the bottom panel puts it in a new panel at the column foot. |
| Card in a panel | Delete | Set it aside (tray). |
| Card in the tray | Ctrl+Up | Put it in a new panel at the foot of the first column. |
| Panel handle | Ctrl+Up / Ctrl+Down | Move the panel up / down within its column. |
| Panel handle | Ctrl+Left / Ctrl+Right | Move the panel to the neighboring column at the same height (or its foot). |

Ctrl+arrow keys are marked handled even when the move is impossible (at an edge), so a host
`ScrollViewer` does not scroll instead.

### Persisting a layout

There is no persistence interface in this package; the layout is a plain object graph the host
stores however it likes. The classes have public setters or `init` accessors and default
constructors, so they round-trip through `System.Text.Json`:

```csharp
docker.LayoutChanged += (_, _) =>
    settings["CardLayout"] = JsonSerializer.Serialize(docker.Layout);

docker.Layout = settings.TryGetValue("CardLayout", out var json)
    ? JsonSerializer.Deserialize<CardDockerLayout>((string)json!)!
    : DefaultLayout();
```

Saving a stale layout is harmless: on the next load, unknown card ids are dropped and new cards
go to the tray. Every read of `Layout` is a fresh copy, so a saved object is never changed by later
edits; `PreferredColumn` is part of it, so a parked panel still goes home after a reload.

## Theming

Token and override mechanics are in [theming.md](theming.md); this section lists only what the
Cards controls read.

| Control | Element | Source |
|---|---|---|
| `SectionCard` | outer edge layer | `ControlStrokeColorSecondaryBrush` (ThemeResource) |
| `SectionCard` | card fill | `CardBackgroundFillColorDefaultBrush` (ThemeResource) |
| `SectionCard` | card stroke | `CardStrokeColorDefaultBrush` (ThemeResource) |
| `SectionCard` | title and tab label size | `XcFontSizeTitle` from application resources; 16 in Core's dictionary, 14 if the key is absent |
| `CardDockerControl` | panel, chip and tray fill / outline | `ComponentTheme.Surface` / `SurfaceOutline` (override with `XcSurfaceBrush` / `XcSurfaceOutlineBrush`, see [theming.md](theming.md#surface-code-painted-chrome-componenttheme)) |
| `CardDockerControl` | drag wash and insertion bar | `DropTargetBackground` / `DropTargetBorderBrush` / `InsertionMarkerBrush`; when `null` (default), the Docking package's translucent blue (ARGB `#802D5B8A` fill, `#E04A82C4` border, `#FF4A82C4` bar). Core has no accent token for this, so the colors are control properties rather than resource keys. |

The `SectionCard` brushes are standard WinUI theme resources, so redefining those keys in the
app's `ThemeDictionaries` restyles every card. `XcFontSizeTitle` is read from
`Application.Current.Resources` whenever the strip or the title is (re)built; a later change to
the token shows on the next rebuild, not immediately.
Corner radii, padding and spacing are fixed in the control.

`CardDockerControl` redraws on `ActualThemeChanged`. After merging or removing a dictionary at
runtime, call `ComponentHost.NotifyResourcesChanged()` as described in
[theming.md](theming.md#3-runtime-changes).

## Known limitations

- `XcFontSizeTitle` is not tracked live by `SectionCard` (it is read when the header is built).
- With `HeaderFit="Abbreviate"`, tabs that still end up in the overflow menu (the strip is too
  narrow even for icons) show their shortened header there too.
- Header fitting needs the card to be given a width. A card in an auto-width host (a horizontal
  `StackPanel`, say) is as wide as its full headers, so nothing is shortened.
- A `{Binding}` on `SectionCardTab.Header` has no inherited `DataContext`.
- `CardDockerControl` keyboard moves cover reordering, moving between panels, panels between
  columns, and set aside / bring back; there is no keyboard path to resize a panel.
- Panel drag and drop is within one control only; there is no cross-control drag.
## Member reference

All types are in namespace `Xtreme.Components.Cards`. This list matches `PublicAPI.Shipped.txt`
and `PublicAPI.Unshipped.txt`.

**`SectionCard`** : `UserControl` (sealed)

- `SectionCard()`
- `IList<SectionCardTab> Tabs { get; }`
- `object? Body { get; set; }` / `static readonly DependencyProperty BodyProperty`
- `string? Title { get; set; }` / `static readonly DependencyProperty TitleProperty`
- `int SelectedIndex { get; set; }` / `static readonly DependencyProperty SelectedIndexProperty`
- `SectionCardTab? SelectedTab { get; }`
- `event EventHandler? SelectionChanged`
- `SectionCardHeaderFit HeaderFit { get; set; }` / `static readonly DependencyProperty HeaderFitProperty`

**`SectionCardHeaderFit`** : `enum`

- `Overflow = 0`
- `Abbreviate = 1`

**`LabelAbbreviation`** : static class

- `const string Suffix = ".."`
- `static string Abbreviate(string? text, int maxLetters)`

**`SectionCardTab`** : `DependencyObject` (sealed)

- `SectionCardTab()`
- `string? Header { get; set; }` / `static readonly DependencyProperty HeaderProperty`
- `IconElement? Icon { get; set; }` / `static readonly DependencyProperty IconProperty`
- `object? Content { get; set; }` / `static readonly DependencyProperty ContentProperty`

**`CardDockerControl`** : `ContentControl`

- `CardDockerControl()`
- `const int MinPanelHeight = 60`
- `const int MaxPanelHeight = 1200`
- `IReadOnlyList<CardDockerCard> Cards { get; set; }`
- `CardDockerLayout Layout { get; set; }`
- `int ColumnCount { get; set; }` / `static readonly DependencyProperty ColumnCountProperty`
- `bool ShowPanelAction { get; set; }` / `static readonly DependencyProperty ShowPanelActionProperty`
- `string PanelActionLabel { get; set; }` / `static readonly DependencyProperty PanelActionLabelProperty`
- `string TrayHeader { get; set; }` / `static readonly DependencyProperty TrayHeaderProperty`
- `string TrayPlaceholderText { get; set; }` / `static readonly DependencyProperty TrayPlaceholderTextProperty`
- `string NewPanelText { get; set; }` / `static readonly DependencyProperty NewPanelTextProperty`
- `Brush? DropTargetBackground { get; set; }` / `static readonly DependencyProperty DropTargetBackgroundProperty`
- `Brush? DropTargetBorderBrush { get; set; }` / `static readonly DependencyProperty DropTargetBorderBrushProperty`
- `Brush? InsertionMarkerBrush { get; set; }` / `static readonly DependencyProperty InsertionMarkerBrushProperty`
- `event EventHandler? LayoutChanged`
- `event EventHandler<string>? PanelActionInvoked`

**`CardDockerCard`** (sealed)

- `CardDockerCard()`
- `required string Id { get; init; }`
- `required string Title { get; init; }`
- `Func<IconElement?>? CreateIcon { get; init; }`

**`CardDockerPanel`** (sealed)

- `CardDockerPanel()`
- `required string Id { get; init; }`
- `int Column { get; set; }`
- `int Sort { get; set; }`
- `int? PreferredColumn { get; set; }`
- `List<string> CardIds { get; init; }`
- `int MinHeight { get; set; }`

**`CardDockerLayout`** (sealed)

- `CardDockerLayout()`
- `List<CardDockerPanel> Panels { get; init; }`
- `List<string> TrayCardIds { get; init; }`
- `CardDockerLayout Clone()`
