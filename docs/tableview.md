# Xtreme.Components.TableView

Helpers for [WinUI.TableView](https://github.com/w-ahmad/WinUI.TableView) on Uno Platform.
The package does **not** contain a table control of its own. It adds behavior to the
third-party `WinUI.TableView.TableView` control, which you create, configure and bind as usual.

Today the package holds one helper, `RowFlagDecorator`: it frames a flagged row in a 2px colored
frame and pins a small caps tag ("NEW", "CHANGED", "AGED OUT") to the row's bottom-left corner.
It is recycle-safe and follows each row's actual theme.

- Live demo: [gallery](https://xtremecrm.github.io/Xtreme.Components/), TableView page
- Sample source: [TableViewPage.cs](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/Pages/TableViewPage.cs)

## Dependencies

| Dependency | How it arrives | Notes |
|---|---|---|
| `WinUI.TableView` (w-ahmad, MIT) | Package dependency of `Xtreme.Components.TableView` | Built against `1.5.0-preview2` (pinned in the repo's `Directory.Packages.props`). The decorator's public API takes a `WinUI.TableView.TableView`, so the type is part of this package's surface. |
| `Xtreme.Components.Core` | Package dependency | Supplies `ComponentHost.TryGetBrush`, which the default brush resolver uses. See [core.md](core.md). |

The package does not depend on `Xtreme.Components.Primitives` (the dependency diagrams in the
root README and [architecture.md](architecture.md) show one; the project file references Core
only).

`WinUI.TableView` comes in transitively, so you do not have to add a separate
`PackageReference`. Add one explicitly if you want to pin a different version; a version other
than the one this package was built against is untested. Set-up of the table control itself
(its own resources, platform notes, features such as sorting, filtering and editing) is covered
by the [WinUI.TableView documentation](https://w-ahmad.dev/WinUI.TableView/), including its
[Uno getting-started page](https://w-ahmad.dev/WinUI.TableView/docs/getting-started-with-uno.html).
The gallery merges no WinUI.TableView dictionary and renders the table on Desktop (Skia) and
WebAssembly.

Like every package in this repository it targets `net10.0` only; the consuming Uno head supplies
the renderer.

## Setup

1. Add the package:

   ```bash
   dotnet add package Xtreme.Components.TableView --prerelease
   ```

2. Define the brushes your flags will name. Nothing in this package reads an `Xc*` token, so no
   `Themes/Generic.xaml` merge is needed for it. Put the brushes in `ThemeDictionaries` so each
   theme gets its own value, at the top level of `App.xaml` or in a dictionary merged into it (see
   [Brush resolution](#brush-resolution)). From the gallery's
   `App.xaml`:

   ```xml
   <Application.Resources>
     <ResourceDictionary>
       <ResourceDictionary.MergedDictionaries>
         <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
       </ResourceDictionary.MergedDictionaries>

       <ResourceDictionary.ThemeDictionaries>
         <ResourceDictionary x:Key="Light">
           <SolidColorBrush x:Key="GalleryFlagNewBrush" Color="#2E8B57" />
           <SolidColorBrush x:Key="GalleryFlagChangedBrush" Color="#C0392B" />
         </ResourceDictionary>
         <ResourceDictionary x:Key="Dark">
           <SolidColorBrush x:Key="GalleryFlagNewBrush" Color="#5FD38D" />
           <SolidColorBrush x:Key="GalleryFlagChangedBrush" Color="#F08A7E" />
         </ResourceDictionary>
       </ResourceDictionary.ThemeDictionaries>
     </ResourceDictionary>
   </Application.Resources>
   ```

3. Attach the decorator to a table once, after creating it (code-behind; there is no XAML
   attached property).

## Samples

### Attach and refresh

Trimmed from the gallery's `TableViewPage`:

```csharp
using System.Collections.ObjectModel;
using Xtreme.Components.TableView;

public sealed class Row
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Orders { get; set; }
    public bool IsNew { get; set; }
    public bool IsChanged { get; set; }
}

private readonly ObservableCollection<Row> _rows = [ /* ... */ ];
private readonly WinUI.TableView.TableView _table = new();

// constructor
_table.AutoGenerateColumns = true;
_table.ItemsSource = _rows;
_table.SelectionMode = ListViewSelectionMode.Single;

RowFlagDecorator.Attach(_table, item => item switch
{
    Row { IsNew: true }     => new RowFlag("NEW", "GalleryFlagNewBrush", "Added in this session"),
    Row { IsChanged: true } => new RowFlag("CHANGED", "GalleryFlagChangedBrush", "Has unsaved edits"),
    _ => null,
});

// A flag changed on a row the list did not re-bind: repaint the realized rows.
private void Toggle(Action<Row> change)
{
    if (_table.SelectedItem is not Row row) return;
    change(row);
    RowFlagDecorator.Refresh(_table);
}
```

The gallery writes `WinUI.TableView.TableView` in full. In code whose namespace sits under
`Xtreme.Components` (or under `WinUI`), the bare name `TableView` binds to the
`Xtreme.Components.TableView` (or `WinUI.TableView`) namespace before any `using` directive is
consulted; qualify the type or alias it (`using TableView = WinUI.TableView.TableView;`).

The table can equally be declared in XAML (`xmlns:tv="using:WinUI.TableView"`,
`<tv:TableView x:Name="EventTypesTable" ... />`) and decorated from the code-behind constructor
after `InitializeComponent()`.

### Custom brush resolver

Pass a `BrushResolver` when flag brushes do not live in application resources, or when a key
should map to something other than a resource:

```csharp
RowFlagDecorator.Attach(_table, FlagOf, (element, key) => key switch
{
    "danger" => new SolidColorBrush(element.ActualTheme == ElementTheme.Dark
        ? Color.FromArgb(0xFF, 0xF0, 0x8A, 0x7E)
        : Color.FromArgb(0xFF, 0xC0, 0x39, 0x2B)),
    _ => RowFlagDecorator.DefaultResolveBrush(element, key),
});
```

`element` is the row container being painted; read its `ActualTheme` to pick a theme-specific
value. Return null to fall back to the built-in defaults (transparent frame, gray tag).

### Custom look

`RowFlagOptions` carries the resolver and the frame and tag look. Every property defaults to the
built-in look, so set only what you change. From the gallery's "Custom look" switch:

```csharp
private static readonly RowFlagOptions CustomLook = new()
{
    FrameThickness = 3,
    FrameCornerRadius = new CornerRadius(4),
    TagFontSize = 10,
    TagPadding = new Thickness(8, 1, 8, 2),
    TagCornerRadius = new CornerRadius(0, 6, 0, 4),
    // ResolveBrush = ..., TagForeground = ...
};

RowFlagDecorator.Attach(_table, FlagOf, CustomLook);
```

The decorator keeps the options instance and reads it on every paint pass: change a property and
call `Refresh` to repaint the realized rows.

### Detach and re-attach

```csharp
RowFlagDecorator.Detach(_table);               // stop decorating; realized rows are cleared
RowFlagDecorator.Attach(_table, FlagOf);       // decorate again
RowFlagDecorator.Attach(_table, OtherFlagOf);  // replaces the previous registration
```

Calling `Attach` again on a decorated table replaces the registration: the earlier
`ContainerContentChanging` handler is unsubscribed and the realized rows are repainted from the
new one, so switching looks or flag rules needs no `Detach` first. `Detach` returns false when the
table was not decorated; `IsAttached` tells you without changing anything.

## API reference

Namespace `Xtreme.Components.TableView`. Sources:
[RowFlagDecorator.cs](https://github.com/XtremeCRM/Xtreme.Components/blob/main/src/Xtreme.Components.TableView/RowFlagDecorator.cs),
[RowFlagOptions.cs](https://github.com/XtremeCRM/Xtreme.Components/blob/main/src/Xtreme.Components.TableView/RowFlagOptions.cs).

### `RowFlag` (sealed record)

`public sealed record RowFlag(string Label, string BrushKey, string? Tip = null);`

| Member | Type | Default | Purpose |
|---|---|---|---|
| `RowFlag(string Label, string BrushKey, string? Tip = null)` | constructor | `Tip = null` | Creates a flag. |
| `Label` | `string` (get; init) | required | Text of the tag pinned to the row. Shown as given; write it in caps if you want caps. |
| `BrushKey` | `string` (get; init) | required | Key passed to the brush resolver. The resolved brush paints both the frame and the tag background. |
| `Tip` | `string?` (get; init) | `null` | Tooltip on the tag. A tag with a tip is hit-testable (so the tooltip can open); a tag without one lets pointer input through to the row. Re-evaluated on every paint, so a recycled container follows its current flag. |

Being a record, `RowFlag` also has value equality, `with` expressions and `Deconstruct`.

### `RowFlagDecorator` (static class)

| Member | Signature | Purpose |
|---|---|---|
| `Attach` | `static void Attach(WinUI.TableView.TableView table, Func<object?, RowFlag?> flagOf, BrushResolver? resolveBrush = null)` | Registers the decorator on `table`. `flagOf` receives a row's item and returns its flag, or null for an unflagged row. `resolveBrush` turns a `BrushKey` into a brush; null (the default) uses `DefaultResolveBrush`. Same as the `RowFlagOptions` overload with only `ResolveBrush` set. |
| `Attach` | `static void Attach(WinUI.TableView.TableView table, Func<object?, RowFlag?> flagOf, RowFlagOptions options)` | Registers the decorator with a custom resolver and look. Hooks the table's `ContainerContentChanging`, so every row realized or recycled afterwards is painted, and repaints the rows already realized. Attaching again replaces the previous registration (its handler is unsubscribed). |
| `Detach` | `static bool Detach(WinUI.TableView.TableView table)` | Unsubscribes the decorator and hides the frame and tag on every realized row. Returns false if the table was not decorated. |
| `IsAttached` | `static bool IsAttached(WinUI.TableView.TableView table)` | True while the table is decorated. |
| `Refresh` | `static void Refresh(WinUI.TableView.TableView table)` | Repaints the realized rows from `flagOf`. Call it after a flag changes without the list re-binding the row (for example a property set on the item). Visits only the containers the items panel holds, so the cost follows the visible row count, not `ItemsSource`. Does nothing if the table is not decorated. |
| `DefaultResolveBrush` | `static Brush? DefaultResolveBrush(FrameworkElement element, string key)` | The default resolver: `ComponentHost.TryGetBrush(key, element.ActualTheme)`. Public so a custom resolver can delegate to it. |
| `BrushResolver` | `delegate Brush? BrushResolver(FrameworkElement element, string key)` | Signature of a resolver. `element` is the row container that will paint the brush; `key` is `RowFlag.BrushKey`. Null means "not found". |

The `BrushResolver` overload has priority when the third argument is a bare `null`, so
`Attach(table, flagOf, null)` still compiles and means "default resolver".

### `RowFlagOptions` (sealed class)

| Property | Type | Default | Purpose |
|---|---|---|---|
| `ResolveBrush` | `BrushResolver?` | `null` (`DefaultResolveBrush`) | Turns a `BrushKey` into a brush. |
| `FrameThickness` | `double` | `2` | Width of the frame around a flagged row. |
| `FrameCornerRadius` | `CornerRadius` | `0` | Corner radius of the frame. |
| `TagFontSize` | `double` | `8` | Font size of the tag text (SemiBold). |
| `TagForeground` | `Brush?` | `null` (white) | Tag text brush. |
| `TagPadding` | `Thickness` | `6,0,6,1` | Padding inside the tag. |
| `TagCornerRadius` | `CornerRadius` | `0,4,0,0` | Tag corners (rounded top-right only by default). |

## Styling

There are no tokens, styles or templates in this package; the look is set per table through
`RowFlagOptions`.

| Part | Default | Override |
|---|---|---|
| Frame color | Resolved `BrushKey` brush | Define the key per theme, or supply a resolver. |
| Frame color, resolver returns null | `Transparent` | Resolver. |
| Frame thickness / corners | `2` / square | `FrameThickness`, `FrameCornerRadius`. |
| Tag background | Resolved `BrushKey` brush; `Gray` when the resolver returns null | As for the frame. |
| Tag text | `Label`; white, 8px, SemiBold | `TagForeground`, `TagFontSize`. With the default white text, choose flag brushes dark enough for it in both themes. |
| Tag shape | Padding `6,0,6,1`, corner radius `0,4,0,0`, anchored bottom-left | `TagPadding`, `TagCornerRadius`. |
| Row container `BorderBrush` / `BorderThickness` | Not touched | Style the rows as you like (see below). |

Everything else about the rows and cells (row and cell styles, alternate row colors, grid
lines, conditional styling) belongs to WinUI.TableView; see its
[conditional styling](https://w-ahmad.dev/WinUI.TableView/docs/conditional-styling.html) page.
The decorator does not write the row container's `BorderBrush` or `BorderThickness`, so a row
border set through those mechanisms is kept on flagged and unflagged rows alike; the flag frame
is drawn on top of it.

## Brush resolution

The default resolver calls `ComponentHost.TryGetBrush(key, element.ActualTheme)`, which looks
for the key, for the row's actual theme, in the application's `ThemeDictionaries` and own
entries, then in each merged dictionary's `ThemeDictionaries` and entries (last merged first,
recursively), and finally through the framework's application-scope lookup. A per-theme flag
brush therefore resolves correctly whether it is defined at the top level of `App.xaml` (as in the
setup sample) or in a dictionary merged into it, and a definition in `App.xaml` overrides one in a
merged dictionary. See [core.md](core.md#brush-lookup-rules) for the full lookup rules and
[theming.md](theming.md) for the theme setup.

## Behavior notes

- **Per container, every pass.** Row containers are recycled, so each `ContainerContentChanging`
  either paints or clears both the frame and the tag, including the tag's tooltip and
  hit-testing. A recycled container never keeps a stale flag.
- **Overlay frame.** The frame is a `Border` drawn over the row (not the container's own border),
  so a flag never changes a row's size. The list estimates its scroll extent from one realized
  row; earlier versions gave every row a transparent 2px border for that reason, and now no row
  needs one.
- **Theme switches** need no call. Each painted container listens to its own
  `ActualThemeChanged` and repaints whatever item it holds at that moment.
- **Placement.** The frame and the tag are added to the first `Panel` found within four levels of
  the row container's visual tree (the row template's layout panel), spanning all rows and columns
  of that panel: the frame stretched, with `Canvas.ZIndex` 99 and no hit-testing; the tag anchored
  bottom-left with `Canvas.ZIndex` 100. Not in a cell, because cells clip. If the template is not
  applied yet on the first pass, the decorator waits for the container's `Loaded` and re-reads the
  flag then.
- **Unflagged rows** hide the frame and tag (`Visibility.Collapsed`); both are created lazily the
  first time a container shows a flag and are reused afterwards.
- **Registration lifetime.** Tables, overlays and theme hooks are held in `ConditionalWeakTable`s,
  so the decorator does not keep a table or its containers alive. `Detach` is only needed to stop
  decorating a table that stays alive.

## Known limitations

- **Template assumption.** The frame and tag need a `Panel` within four levels of the row
  container. A re-templated row without one gets neither.
- **The frame covers the row's outer edge.** Being an overlay, it paints over the outermost
  `FrameThickness` pixels of the row instead of adding space around it.
- **Third-party versioning.** Built and tested against `WinUI.TableView` `1.5.0-preview2`, itself a
  pre-release.
