# Xtreme.Components.Core

The shared foundation of the Xtreme.Components families. It contains no controls. It provides:

- **`ComponentHost`**: the optional, app-wide hooks a host sets at startup (current theme, icon
  geometry), plus the theme-aware brush lookup and dialog preparation the other packages call.
- **`ComponentTheme`**: the `ActualTheme`-driven surface and outline brushes that code-painted
  chrome uses, with high-contrast overrides.
- **`Themes/Generic.xaml`**: the `Xc*` font-size and style tokens.

Every other package depends on it, directly (Primitives, Docking, Cards, TableView) or through
Primitives (Scheduler). You rarely add it by hand; it arrives with any family package.

- Live demo: [gallery](https://xtremecrm.github.io/Xtreme.Components/) (the dark-mode switch
  and the Primitives page's star icon exercise `ComponentHost`)
- Host wiring in the gallery:
  [App.xaml.cs](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/App.xaml.cs) (the hooks, set once in `OnLaunched`),
  [App.xaml](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/App.xaml)

Token tables, high-contrast keys and runtime theme changes are documented in
[theming.md](theming.md); package layout and the reasons Core exists are in
[architecture.md](architecture.md). This page covers the code API and host initialization.

## Host initialization

Nothing in Core is required. A bare app that sets no hook gets WinUI defaults: the application
theme, no custom icon vectors, and the WinUI `ContentDialog` style. Initialize only what applies.

### 1. Merge the resources (App.xaml)

A family's `Themes/Generic.xaml` merges Core's tokens itself. Today only Primitives ships one
(Scheduler gets it through its Primitives dependency, but the dictionary still has to be merged
in `App.xaml`). Merge Core's own dictionary instead if you use none of Primitives' controls.
Overrides go after the merge. From the gallery:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
      <!-- The one merge a consumer needs: a family dictionary brings the Core tokens with it. -->
      <ResourceDictionary Source="ms-appx:///Xtreme.Components.Primitives/Themes/Generic.xaml" />
      <!-- Core alone: -->
      <!-- <ResourceDictionary Source="ms-appx:///Xtreme.Components.Core/Themes/Generic.xaml" /> -->
    </ResourceDictionary.MergedDictionaries>
    <!-- token overrides and app ThemeDictionaries here -->
  </ResourceDictionary>
</Application.Resources>
```

Uno does not discover a library's `Generic.xaml` on every platform, which is why the merge is
explicit.

### 2. Set the hooks (code)

```csharp
using Xtreme.Components.Core;

// Live theme switching driven from a root element (not the OS / Application.RequestedTheme):
ComponentHost.CurrentTheme = () => rootElement.ActualTheme;

// Vector data for icon keys a control exposes (e.g. OverlayGlyphIcon.BackGeometryKey):
ComponentHost.ResolveGeometry = key => MyIcons.TryGet(key);
```

Set both once at startup, after the window content exists. The gallery does it in
`App.OnLaunched`, right after creating the root frame:

```csharp
private static void InitializeComponentHost(Frame rootFrame)
{
    // The dark-mode switch sets the root page's RequestedTheme, so read the visible theme there.
    ComponentHost.CurrentTheme = () => (rootFrame.Content as FrameworkElement)?.ActualTheme;

    // One icon key, "star". A fresh Geometry per call: a geometry must not be shared between shapes.
    ComponentHost.ResolveGeometry = key => key == "star"
        ? (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
            "M12,2 L15,9 L22,9.5 L16.5,14.5 L18,22 L12,18 L6,22 L7.5,14.5 L2,9.5 L9,9 Z")
        : null;
}
```

Both hooks are delegates, so `CurrentTheme` set once keeps returning the root's current theme
after later switches; the gallery's dark-mode toggle only changes the root page's
`RequestedTheme`. Avoid assigning hooks from a page constructor or an event handler: the last
assignment wins for the whole app, and anything that reads the hook before that page opens sees
nothing.

### 3. After a runtime resource change

When the app merges or removes a dictionary at runtime (a high-contrast palette, a brand
switch), call:

```csharp
ComponentHost.NotifyResourcesChanged();
```

Theme switches do not need it. See [theming.md](theming.md#3-runtime-changes).

### 4. Code-built dialogs (library authors and hosts)

```csharp
var dialog = new ContentDialog { Title = "Rename", Content = box, PrimaryButtonText = "OK" };
ComponentHost.PrepareDialog(dialog, this.XamlRoot);
await dialog.ShowAsync();
```

`PrepareDialog` applies an app resource named `DefaultContentDialogStyle` when one exists, sets
`RequestedTheme` to `ComponentHost.EffectiveTheme()`, and sets the `XamlRoot` the dialog shows in
(`ShowAsync` throws without one). Pass the invoking element's `XamlRoot`. Without one (or with
the one-argument overload) a `XamlRoot` the dialog already has is kept, and failing that it is
taken from the dialog's `Content` when that element is already in a live tree; otherwise it stays
unset and the caller must set it.

## What the other packages use

| Consumer | Core member |
|---|---|
| Docking (`DockHost`, `DockSplitter`), Cards (`CardDockerControl`) | `ComponentTheme.Surface`, `ComponentTheme.SurfaceOutline` |
| Primitives `OverlayGlyphIcon` | `ComponentHost.ResolveGeometry` |
| Primitives `ChipPickerDialog` | `ComponentHost.PrepareDialog` |
| Primitives converters, `CalendarPalette`; Scheduler `SchedulerPalette` | `ComponentHost.TryGetBrush`, `ComponentHost.ResourcesChanged` |
| TableView `RowFlagDecorator.DefaultResolveBrush` | `ComponentHost.TryGetBrush(key, theme)` |
| Primitives controls and styles | `Xc*` tokens in `Themes/Generic.xaml` |

## API reference

Namespace `Xtreme.Components.Core`. Sources:
[ComponentHost.cs](https://github.com/XtremeCRM/Xtreme.Components/blob/main/src/Xtreme.Components.Core/ComponentHost.cs),
[ComponentTheme.cs](https://github.com/XtremeCRM/Xtreme.Components/blob/main/src/Xtreme.Components.Core/ComponentTheme.cs).

### `ComponentHost` (static class)

The only global state in the library. Hooks are plain static properties: the last assignment
wins, and nothing is thread-guarded (set them on the UI thread).

| Member | Type / signature | Default | Purpose |
|---|---|---|---|
| `CurrentTheme` | `static Func<ElementTheme?>? { get; set; }` | `null` | The theme the app is showing when it drives the theme from a root element's `RequestedTheme`. Read by `EffectiveTheme`. Null, a null return or `ElementTheme.Default` mean "use the application theme". |
| `ResolveGeometry` | `static Func<string, Geometry?>? { get; set; }` | `null` | Turns an icon key into vector geometry the app owns. Null, or a null return, means the key has no vector and the control shows nothing for it. |
| `ResourcesChanged` | `static event EventHandler?` | no subscribers | Raised by `NotifyResourcesChanged` (sender `null`, `EventArgs.Empty`). Code palettes that cache token lookups (Scheduler, calendar pickers) drop their caches on it. |
| `NotifyResourcesChanged()` | `static void` | | Raises `ResourcesChanged`. Call after merging or removing a resource dictionary at runtime. |
| `EffectiveTheme()` | `static ElementTheme` | | `CurrentTheme()` when it returns `Light` or `Dark`; otherwise `Dark` if `Application.Current.RequestedTheme` is `Dark`, else `Light` (including when there is no `Application.Current`). Never returns `Default`. |
| `TryGetBrush(string key)` | `static Brush?` | | `TryGetBrush(key, EffectiveTheme())`. |
| `TryGetBrush(string key, ElementTheme theme)` | `static Brush?` | | Brush lookup for an explicit theme (typically an element's `ActualTheme`). See [Brush lookup rules](#brush-lookup-rules). Null when not found, when the value is not a `Brush`, or on an expected lookup failure; other exceptions propagate. |
| `PrepareDialog(ContentDialog dialog)` | `static void` | | `PrepareDialog(dialog, null)`. |
| `PrepareDialog(ContentDialog dialog, XamlRoot? xamlRoot)` | `static void` | | Applies the app's `DefaultContentDialogStyle` (when defined as a `Style` in application resources), sets `dialog.RequestedTheme = EffectiveTheme()`, and sets `dialog.XamlRoot`: `xamlRoot` when given, else the dialog's existing root, else its `Content`'s root when that is in a live tree. Call before `ShowAsync`. |

### `ComponentTheme` (static class)

Per-theme surface colors for chrome painted from code-behind, where a `{ThemeResource}` binding
is not available.

| Member | Type / signature | Value | Purpose |
|---|---|---|---|
| `HighContrastSentinel` | `const string` | `"XcHighContrast"` | Key that only a consumer's high-contrast override dictionary defines (any value). Its presence in application resources (merged dictionaries included) switches `Surface` and `SurfaceOutline` to the override keys. |
| `SurfaceKey` | `const string` | `"XcSurfaceBrush"` | Override key for `Surface` while high contrast is active. |
| `SurfaceOutlineKey` | `const string` | `"XcSurfaceOutlineBrush"` | Override key for `SurfaceOutline` while high contrast is active. |
| `Surface(ElementTheme theme)` | `static SolidColorBrush` | Light: `White`; Dark: `#FF202020` | Pane surface: document backdrop, active document tab, splitter grab handle, auto-hide flyout body. |
| `SurfaceOutline(ElementTheme theme)` | `static SolidColorBrush` | Light: `#50000000`; Dark: `#50FFFFFF` | Hairline outline for a `Surface` on translucent chrome. |

With high contrast active, `Surface` / `SurfaceOutline` return the palette's `XcSurfaceBrush` /
`XcSurfaceOutlineBrush` when it is a `SolidColorBrush`; otherwise (key missing, wrong type, or an
expected lookup failure) they return the Light/Dark value above. The override key is resolved with
`ComponentHost.TryGetBrush(key, theme)`, so the `theme` argument still counts under high contrast
where it can: a flat palette (the usual shape, one palette per contrast polarity) gives the same
brush for either theme, and a palette that puts the key in `ThemeDictionaries` gets the entry for
the theme asked for.

The brushes are shared, not allocated per call: the built-in Light and Dark brushes are created
once per theme, and a high-contrast override returns the palette's own instance. Treat them as
read-only (do not change `Color` or `Opacity`; create your own brush for that). The high-contrast
check itself is not cached, so a palette merged or removed at runtime takes effect on the next
call without `NotifyResourcesChanged`. `ElementTheme.Default` is treated as Light outside high
contrast; pass an element's `ActualTheme`, not its `RequestedTheme`.

### Resources (`Themes/Generic.xaml`)

Source: `ms-appx:///Xtreme.Components.Core/Themes/Generic.xaml`.

| Key | Type | Default |
|---|---|---|
| `XcFontSizeCaption` | `x:Double` | `12` |
| `XcFontSizeBody` | `x:Double` | `14` |
| `XcFontSizeTitle` | `x:Double` | `16` |
| `XcFieldLabelStyle` | `Style` (TextBlock) | `XcFontSizeCaption`, `TextFillColorSecondaryBrush`, margin `0,0,0,4` |
| `XcListSectionHeaderTextStyle` | `Style` (TextBlock) | `XcFontSizeCaption`, SemiBold, `TextFillColorSecondaryBrush`, margin `0,8,0,4` |
| `XcListViewItemStyle` | `Style` (ListViewItem) | `MinHeight` 32, `Padding` `8,4`; selection colors are the platform default |

`XcHighContrast`, `XcSurfaceBrush` and `XcSurfaceOutlineBrush` are deliberately not defined here;
their presence is the high-contrast switch. Which controls read which token is listed in
[theming.md](theming.md#2-tokens).

## Brush lookup rules

`ComponentHost.TryGetBrush(key, theme)`:

1. Returns null if there is no `Application.Current`.
2. Maps `ElementTheme.Default` to `EffectiveTheme()`.
3. Searches `Application.Current.Resources`, then its merged dictionaries from the last merged to
   the first, recursing into their own merged dictionaries. In each dictionary it tries, in order:
   - the dictionary's `ThemeDictionaries` entry for the theme (`"Light"`, or `"Dark"` falling back
     to `"Default"`),
   - the dictionary's own flat entry.

   The first hit wins; if it is a `Brush` it is returned.
4. Otherwise returns the framework's own `Application.Current.Resources.TryGetValue(key)` if that
   is a `Brush`.

So a per-theme value resolves for the theme asked for wherever it is defined: in the top-level
`ThemeDictionaries` of `App.xaml` (as the gallery does for its row-flag brushes), or in a
library, brand or palette dictionary merged into it. Definitions closer to the application win: a
flat override written in `App.xaml` after the merges beats a per-theme default inside a merged
dictionary, matching the "overrides go after the merge" rule. Flat keys (same value in both
themes) can live anywhere. The walk stops 16 merge levels deep.

Only expected lookup failures read as "not found" (null): `KeyNotFoundException`,
`InvalidOperationException` (a merged-dictionary list changed during the walk), `XamlParseException`
(a lazily built resource that fails to materialize) and `COMException`. Any other exception is a
bug and propagates.

There is no `HighContrast` theme-dictionary branch: high contrast in this library is an
app-merged override palette, detected through `XcHighContrast` (see
[theming.md](theming.md#surface-code-painted-chrome-componenttheme)).

## Notes and limitations

- **Global, process-wide state.** `CurrentTheme` and `ResolveGeometry` are single static slots;
  two parts of an app (or two windows with different themes) cannot set different values.
  `EffectiveTheme` is one value for the whole app.
- **Missing keys are silent.** `TryGetBrush` and the high-contrast lookup in `ComponentTheme`
  return null / the default color for a key that is not defined or an expected lookup failure
  (see [Brush lookup rules](#brush-lookup-rules)), so a typo in a key fails silently (Primitives'
  brush converter paints hot pink in that case so the gap is visible). Unexpected exceptions are
  no longer swallowed.
- **`PrepareDialog`** looks up `DefaultContentDialogStyle` with `Application.Current.Resources`
  only. It can only derive a `XamlRoot` from the dialog itself; pass one from the invoking
  element when the dialog is built in code.
- **Static event.** `ResourcesChanged` holds strong references to its handlers; subscribe from
  long-lived (static or app-lifetime) objects, or unsubscribe.
- **`XcListViewItemStyle`** sets size and padding only; the selection look is the WinUI default.
  The selected / hover colors are read inside the `ListViewItem` template, so a style setter
  cannot change them without re-templating. Override the platform's `ListViewItemBackground*`
  theme resources in `App.xaml`, or redefine `XcListViewItemStyle` there (based on your own item
  style), to change them.
