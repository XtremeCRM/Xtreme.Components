# Xtreme.Components.TableView

Helpers for [WinUI.TableView](https://github.com/w-ahmad/WinUI.TableView) on Uno Platform.
The package does **not** contain a table control of its own. It adds behaviour to the
third-party `WinUI.TableView.TableView` control, which you create, configure and bind as usual.

Today the package holds one helper, `RowFlagDecorator`: it frames a flagged row in a 2px coloured
border and pins a small caps tag ("NEW", "CHANGED", "AGED OUT") to the row's bottom-left corner.
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
   `Themes/Generic.xaml` merge is needed for it. Put the brushes in the application's own
   `ThemeDictionaries` so each theme gets its own value (see
   [Brush resolution](#brush-resolution) for why the top level matters). From the gallery's
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
value. Return null to fall back to the built-in defaults (transparent frame, grey tag).

## API reference

Namespace `Xtreme.Components.TableView`. Source:
[RowFlagDecorator.cs](https://github.com/XtremeCRM/Xtreme.Components/blob/main/src/Xtreme.Components.TableView/RowFlagDecorator.cs).

### `RowFlag` (sealed record)

`public sealed record RowFlag(string Label, string BrushKey, string? Tip = null);`

| Member | Type | Default | Purpose |
|---|---|---|---|
| `RowFlag(string Label, string BrushKey, string? Tip = null)` | constructor | `Tip = null` | Creates a flag. |
| `Label` | `string` (get; init) | required | Text of the tag pinned to the row. Shown as given; write it in caps if you want caps. |
| `BrushKey` | `string` (get; init) | required | Key passed to the brush resolver. The resolved brush paints both the frame and the tag background. |
| `Tip` | `string?` (get; init) | `null` | Tooltip on the tag. See the hit-testing note under [Known limitations](#known-limitations). |

Being a record, `RowFlag` also has value equality, `with` expressions and `Deconstruct`.

### `RowFlagDecorator` (static class)

| Member | Signature | Purpose |
|---|---|---|
| `Attach` | `static void Attach(WinUI.TableView.TableView table, Func<object?, RowFlag?> flagOf, BrushResolver? resolveBrush = null)` | Registers the decorator on `table`. `flagOf` receives a row's item and returns its flag, or null for an unflagged row. `resolveBrush` turns a `BrushKey` into a brush; null (the default) uses `DefaultResolveBrush`. Hooks the table's `ContainerContentChanging`, so every row realized or recycled afterwards is painted. |
| `Refresh` | `static void Refresh(WinUI.TableView.TableView table)` | Repaints every realized row from `flagOf`. Call it after a flag changes without the list re-binding the row (for example a property set on the item). Does nothing if `Attach` was never called for the table or `ItemsSource` is not an `IEnumerable`. |
| `DefaultResolveBrush` | `static Brush? DefaultResolveBrush(FrameworkElement element, string key)` | The default resolver: `ComponentHost.TryGetBrush(key, element.ActualTheme)`. Public so a custom resolver can delegate to it. |
| `BrushResolver` | `delegate Brush? BrushResolver(FrameworkElement element, string key)` | Signature of a resolver. `element` is the row container that will paint the brush; `key` is `RowFlag.BrushKey`. Null means "not found". |

## Styling

There are no tokens, styles or templates in this package. What is styleable:

| Part | What sets it | Override |
|---|---|---|
| Frame colour | Resolved `BrushKey` brush | Define the key per theme, or supply a `BrushResolver`. |
| Frame colour, unflagged row | `Transparent` (hard-coded) | Not configurable. |
| Frame thickness | `2` on every realized row (hard-coded) | Not configurable. |
| Tag background | Resolved `BrushKey` brush; `Gray` when the resolver returns null | As for the frame. |
| Tag text | `Label`; white, 8px, SemiBold (hard-coded) | Text only; colour and size are not configurable, so choose flag brushes dark enough for white text in both themes. |
| Tag shape | Padding `6,0,6,1`, corner radius `0,4,0,0` (rounded top-right only), anchored bottom-left | Not configurable. |

Everything else about the rows and cells (row and cell styles, alternate row colours, grid
lines, conditional styling) belongs to WinUI.TableView; see its
[conditional styling](https://w-ahmad.dev/WinUI.TableView/docs/conditional-styling.html) page.
Do not set `BorderBrush` or `BorderThickness` on row containers through those mechanisms: the
decorator overwrites both on every pass.

## Brush resolution

The default resolver calls `ComponentHost.TryGetBrush(key, element.ActualTheme)`, which:

1. looks in `Application.Current.Resources.ThemeDictionaries["Light"]` or `["Dark"]` (matching
   the row's actual theme) for the key, then
2. falls back to a plain `Application.Current.Resources` lookup.

Step 1 reads only the application's **own** `ThemeDictionaries`, not the theme dictionaries of a
merged dictionary. A key defined only inside a merged dictionary's `ThemeDictionaries` is found
by step 2, which does not take the row's theme into account, so a row can show the other
theme's colour when the app drives its theme from a root element. Define flag brushes at the top
level of `Application.Resources` as in the setup sample, or supply a resolver. See
[core.md](core.md) for the full lookup rules and [theming.md](theming.md) for the theme setup.

## Behaviour notes

- **Per container, every pass.** Row containers are recycled, so each `ContainerContentChanging`
  either paints or clears both the frame and the tag. A recycled container never keeps a stale
  flag.
- **Uniform border.** Every realized row gets a 2px border, transparent when unflagged. The list
  estimates its scroll extent from one realized row; when only flagged rows were taller, the last
  rows became unreachable.
- **Theme switches** need no call. Each painted container listens to its own
  `ActualThemeChanged` and repaints whatever item it holds at that moment (its `Content`, else
  its `DataContext`).
- **Tag placement.** The tag is a `Border` added to the first `Panel` found within four levels of
  the row container's visual tree (the row template's layout panel), spanning all rows and columns
  of that panel with `Canvas.ZIndex` 100. It is not placed in a cell because cells clip. If the
  template is not applied yet on the first pass, the decorator waits for the container's `Loaded`
  and re-reads the flag then.
- **Unflagged rows** hide the tag (`Visibility.Collapsed`); the tag element is created lazily the
  first time a container shows a flag and is reused afterwards.
- **Registration lifetime.** Tables, tags and theme hooks are held in `ConditionalWeakTable`s, so
  the decorator does not keep a table or its containers alive.

## Known limitations

- **No detach.** There is no way to remove the decorator from a table.
- **Calling `Attach` twice** on the same table replaces the stored `flagOf`/resolver used by
  `Refresh` and theme repaints, but the first call's `ContainerContentChanging` handler stays
  subscribed and keeps evaluating the first `flagOf`. Attach once per table.
- **`Refresh` cost.** It enumerates the whole `ItemsSource` and calls `ContainerFromItem` for
  each item, so it is linear in the item count, not the realized-row count. For very large
  sources, prefer letting the list re-bind the changed item.
- **Tooltip hit-testing is fixed at tag creation.** `IsHitTestVisible` is set from the first flag
  the container shows. If that flag had no `Tip`, a later flag with a `Tip` on the same recycled
  container shows no tooltip; if it had one, later tip-less tags still intercept pointer input
  over their area.
- **Hard-coded tag look.** White 8px text, fixed padding and shape (see [Styling](#styling)).
- **Template assumption.** The tag needs a `Panel` within four levels of the row container. A
  re-templated row without one gets the frame but no tag.
- **Row border is taken over.** `BorderBrush` and `BorderThickness` on every realized row
  container are owned by the decorator.
- **Third-party versioning.** Built and tested against `WinUI.TableView` `1.5.0-preview2`, itself a
  pre-release.
