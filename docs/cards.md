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
  - [SectionCardTab members](#sectioncardtab-members)
  - [SectionCard behaviour](#sectioncard-behaviour)
- [CardDockerControl](#carddockercontrol)
  - [Model](#model)
  - [Usage](#usage)
  - [CardDockerControl members](#carddockercontrol-members)
  - [Model members](#model-members)
  - [Normalization rules](#normalization-rules)
  - [Interaction](#interaction)
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

The mode is decided by what the card holds when it first loads.

| Mode | Set | Header | Body shown |
|---|---|---|---|
| Tabbed | one or more `Tabs` | top-pane `NavigationView` strip, one item per tab (shown even for a single tab) | the selected tab's `Content` |
| Titled | `Title` and `Body`, no tabs | plain semibold `TextBlock` (no tab stops) | `Body` |
| Headerless | `Body` only | none | `Body` |

`Title` is ignored in tabbed mode; `Body` is ignored in tabbed mode.

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
tabbed.Tabs.Add(new SectionCardTab { Header = "Details", Content = Lines("Name: Alex Rivera", "Type: Customer") });
tabbed.Tabs.Add(new SectionCardTab { Header = "Notes",   Content = Lines("Prefers morning visits.") });

var headerless = new SectionCard
{
    Body = Lines("Headerless card: Body only, no tab strip."),
};
```

Populate `Tabs`, `Title` and `Body` before the card is loaded (see
[behaviour](#sectioncard-behaviour)).

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
| `SectionCard()` | constructor | | | Builds the chrome; tabs/title are applied on first `Loaded`. |
| `Tabs` | property (get-only, not a DP) | `IList<SectionCardTab>` | empty list | Tabs for tabbed mode. |
| `Body` | dependency property | `object?` | `null` | Content when there are no tabs. |
| `BodyProperty` | static readonly field | `DependencyProperty` | | Identifier for `Body`. |
| `Title` | dependency property | `string?` | `null` | Plain header label when there are no tabs. |
| `TitleProperty` | static readonly field | `DependencyProperty` | | Identifier for `Title`. |

No events, no selection API.

### SectionCardTab members

`public sealed partial class SectionCardTab : DependencyObject`, `[ContentProperty(Name = "Content")]`.

| Member | Kind | Type | Default | Purpose |
|---|---|---|---|---|
| `SectionCardTab()` | constructor | | | |
| `Header` | dependency property | `string?` | `null` | Label in the tab strip (`null` shows an empty item). |
| `HeaderProperty` | static readonly field | `DependencyProperty` | | Identifier for `Header`. |
| `Content` | dependency property | `object?` | `null` | The tab body. |
| `ContentProperty` | static readonly field | `DependencyProperty` | | Identifier for `Content`. |

### SectionCard behaviour

- **Built once.** The header is built in the first `Loaded` handler, which then unsubscribes.
  After that:
  - adding or removing `Tabs` has no effect;
  - changing `Title` or a tab's `Header` has no effect (`Title` has no change callback);
  - changing `Body` updates the card only when it has no tabs;
  - changing the selected tab's `Content` shows only after the tab is reselected.

  To change structure, build a new `SectionCard`.
- **Tabs via NavigationView.** The strip is a `NavigationView` with `PaneDisplayMode="Top"`,
  back/settings/pane-toggle buttons hidden and a transparent background. Each tab is a
  `NavigationViewItem` whose `Tag` is the `SectionCardTab`. The first tab is selected on load.
  NavigationView was chosen over `SelectorBar` for its built-in top overflow, hierarchy and
  per-tab icon support; of these, only overflow is used today (items that do not fit move into
  NavigationView's own overflow menu). `SectionCardTab` has no icon property.
- **Lazy tab bodies.** `SectionCardTab` is a `DependencyObject`, not a control, so its `Content`
  is held as data and parented into the card only while selected. Unselected tab bodies are out of
  the visual tree; switching tabs unloads one body and loads the other (control state such as
  text is kept, since the same instances are reused). Do not put the same element in two tabs or
  in a tab and elsewhere.
- **Bindings.** Tab content inherits the card's `DataContext` once parented. A `{Binding}` set on
  `SectionCardTab.Header` itself has no inherited `DataContext` (the tab is not in the tree) and is
  read only once anyway.
- **Layout.** `HorizontalAlignment` defaults to `Stretch`. Header and body sit in two `Auto` rows
  with 8px spacing, so the card sizes to its content; give the body its own `ScrollViewer` for
  long content.
- **Actions.** Per-list actions (an Add button and so on) belong at the bottom of the tab's own
  content; the card header has no action slot.

## CardDockerControl

A `ContentControl` that renders a layout of cards as chips: panels stacked in columns, each panel
holding one or more cards side by side, plus a "Set aside" tray. The user rearranges by dragging
chips and resizes panels by dragging a grip. The control never sees card bodies: a card is an
opaque string id with a title and an icon factory. The host owns what an id means, renders the
real cards elsewhere from the resulting layout, and stores the layout.

### Model

```
CardDockerLayout
 ├── Panels: List<CardDockerPanel>
 │     └── Id, Column (0-based), Sort (top to bottom), MinHeight, CardIds (left to right)
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
and redraws.

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
| `Layout` | property | `CardDockerLayout` | new empty layout | The arrangement. The setter stores a **clone** (`null` becomes an empty layout); the getter returns the control's live instance. |
| `ColumnCount` | dependency property | `int` | `3` | Number of columns. Changing it re-normalizes and redraws. |
| `ColumnCountProperty` | static readonly field | `DependencyProperty` | | Identifier for `ColumnCount`. |
| `ShowPanelAction` | dependency property | `bool` | `false` | Shows a small text button at the top of every panel. |
| `ShowPanelActionProperty` | static readonly field | `DependencyProperty` | | Identifier for `ShowPanelAction`. |
| `PanelActionLabel` | dependency property | `string` | `"Hide"` | Text on the per-panel button. |
| `PanelActionLabelProperty` | static readonly field | `DependencyProperty` | | Identifier for `PanelActionLabel`. |
| `LayoutChanged` | event | `EventHandler?` | | Raised after a user change (drop or panel resize). Read `Layout` for the result. |
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

1. Ids not present in `Cards` are removed from panels and the tray.
2. Panels left with no cards are removed.
3. Cards that appear nowhere are appended to the tray. The control never invents a panel for a
   new card; the host decides where new cards go by placing them in the layout it assigns.
4. `Column` is clamped to `0..ColumnCount-1` and `MinHeight` to `60..1200`.
5. `Sort` is rewritten densely per column.

Consequences worth knowing:

- Lowering `ColumnCount` moves panels from the removed columns into the last column, and this is
  written into `Layout`. Raising it again does not move them back.
- Normalization during an assignment does **not** raise `LayoutChanged`. If you need the
  normalized form (for example to save it), read `Layout` after assigning.
- If both `Cards` and `Layout.Panels` are empty, the control skips the redraw and keeps whatever it
  showed last.

### Interaction

- **Drag a chip.** Press a chip and move at least 6px (either axis) to start a drag; a shorter
  press does nothing (chips raise no click). Drag runs on raw pointer handlers with pointer
  capture, not XAML drag-and-drop or manipulation events, so it behaves the same on every head
  and mouse/touch/pen. Drop targets are snapshotted once at drag start; the smallest target under
  the pointer wins.
- **Drop targets.**

  | Target | Result |
  |---|---|
  | Left or right half of a chip in a panel | Insert before or after that chip (reorders within a panel, or moves between panels). An insertion bar marks the seam. |
  | "New panel" slot at the foot of each column | Creates a new panel at the bottom of that column holding the card. |
  | "Set aside" tray | Moves the card to the end of `TrayCardIds`. |
  | Anywhere else | Cancels; nothing changes. |

  A translucent blue wash (the same as the Docking package's landing preview) covers the target
  while hovering.
- **Panels are not dragged.** Only cards move. A panel's column and position change only as
  cards leave and arrive: a panel that loses its last card disappears, and new panels always land
  at the foot of a column. There is no gesture to reorder panels vertically.
- **Resize a panel.** Each panel has a grip along its bottom edge. Dragging it changes the panel's
  `MinHeight` live, clamped to `60..1200`; the layout is committed (and `LayoutChanged` raised)
  on release, and only if the height changed.
- **Capture loss.** If pointer capture is lost mid-drag, the drag is abandoned with no change.
- **Events.** `LayoutChanged` fires after every completed drop onto a target, even one that leaves
  the arrangement unchanged (for example dropping a chip on its own right half). Debounce in the
  host if saving is expensive.
- **Theme.** A theme change redraws the control (arrangement unchanged).
- **Keyboard.** There is no keyboard or automation path for rearranging.

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
go to the tray. Because the getter returns the live instance, take `Layout.Clone()` if you keep a
copy across further edits, and assign `Layout` again after changing it in code (mutating the
returned object does not redraw).

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
| `CardDockerControl` | drag wash and insertion bar | hard-coded translucent blue (ARGB `#802D5B8A` fill, `#E04A82C4` border/bar) |

The `SectionCard` brushes are standard WinUI theme resources, so redefining those keys in the
app's `ThemeDictionaries` restyles every card. `XcFontSizeTitle` is read from
`Application.Current.Resources` once, when the card builds; it does not follow later changes.
Corner radii, padding and spacing are fixed in the control.

`CardDockerControl` redraws on `ActualThemeChanged`. After merging or removing a dictionary at
runtime, call `ComponentHost.NotifyResourcesChanged()` as described in
[theming.md](theming.md#3-runtime-changes).

## Known limitations

- `SectionCard` is not reactive after load (see [behaviour](#sectioncard-behaviour)): no
  late tab changes, no live `Title`, no selected-tab property or selection-changed event.
- `SectionCardTab` has no icon, although the NavigationView strip could show one.
- `CardDockerControl` UI strings ("Set aside", "Drag a card here to set it aside.", "New panel")
  are hard-coded English with no override.
- No panel drag/reorder gesture and no keyboard alternative for rearranging.
- `Layout` getter exposes the live instance; mutating it skips normalization and redraw.

## Member reference

All types are in namespace `Xtreme.Components.Cards`. This list matches `PublicAPI.Shipped.txt`
and `PublicAPI.Unshipped.txt`.

**`SectionCard`** : `UserControl` (sealed)

- `SectionCard()`
- `IList<SectionCardTab> Tabs { get; }`
- `object? Body { get; set; }` / `static readonly DependencyProperty BodyProperty`
- `string? Title { get; set; }` / `static readonly DependencyProperty TitleProperty`

**`SectionCardTab`** : `DependencyObject` (sealed)

- `SectionCardTab()`
- `string? Header { get; set; }` / `static readonly DependencyProperty HeaderProperty`
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
- `List<string> CardIds { get; init; }`
- `int MinHeight { get; set; }`

**`CardDockerLayout`** (sealed)

- `CardDockerLayout()`
- `List<CardDockerPanel> Panels { get; init; }`
- `List<string> TrayCardIds { get; init; }`
- `CardDockerLayout Clone()`
