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
  [MainPage.xaml.cs](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/MainPage.xaml.cs),
  [PrimitivesPage.xaml.cs](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/Pages/PrimitivesPage.xaml.cs),
  [App.xaml](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/App.xaml)

Token tables, high-contrast keys and runtime theme changes are documented in
[theming.md](theming.md); package layout and the reasons Core exists are in
[architecture.md](architecture.md). This page covers the code API and host initialisation.

## Host initialisation

Nothing in Core is required. A bare app that sets no hook gets WinUI defaults: the application
theme, no custom icon vectors, and the WinUI `ContentDialog` style. Initialise only what applies.

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

The gallery sets `CurrentTheme` in its dark-mode toggle handler, after changing the root
page's `RequestedTheme`:

```csharp
private void DarkToggle_Toggled(object sender, RoutedEventArgs e)
{
    RequestedTheme = DarkToggle.IsOn ? ElementTheme.Dark : ElementTheme.Light;
    ComponentHost.CurrentTheme = () => ActualTheme;
}
```

and resolves one icon key, `"star"`, in the Primitives page constructor:

```csharp
ComponentHost.ResolveGeometry = key => key == "star"
    ? (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
        "M12,2 L15,9 L22,9.5 L16.5,14.5 L18,22 L12,18 L6,22 L7.5,14.5 L2,9.5 L9,9 Z")
    : null;
```

In a real app set both once at startup (for example in `App.OnLaunched` after the window
content exists); the gallery sets them from pages only to keep each demo self-contained. The
hook is a delegate, so `CurrentTheme` set once at startup keeps returning the root's current
theme after later switches.

### 3. After a runtime resource change

When the app merges or removes a dictionary at runtime (a high-contrast palette, a brand
switch), call:

```csharp
ComponentHost.NotifyResourcesChanged();
```

Theme switches do not need it. See [theming.md](theming.md#3-runtime-changes).

### 4. Code-built dialogs (library authors and hosts)

```csharp
var dialog = new ContentDialog { Title = "Rename", Content = box, PrimaryButtonText = "OK",
                                 XamlRoot = this.XamlRoot };
ComponentHost.PrepareDialog(dialog);
await dialog.ShowAsync();
```

`PrepareDialog` applies an app resource named `DefaultContentDialogStyle` when one exists and
sets `RequestedTheme` to `ComponentHost.EffectiveTheme()`. It does not set `XamlRoot`.

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
| `TryGetBrush(string key, ElementTheme theme)` | `static Brush?` | | Brush lookup for an explicit theme (typically an element's `ActualTheme`). See [Brush lookup rules](#brush-lookup-rules). Null when not found, when the value is not a `Brush`, or on any exception. |
| `PrepareDialog(ContentDialog dialog)` | `static void` | | Applies the app's `DefaultContentDialogStyle` (when defined as a `Style` in application resources) and sets `dialog.RequestedTheme = EffectiveTheme()`. Call before `ShowAsync`. |

### `ComponentTheme` (static class)

Per-theme surface colours for chrome painted from code-behind, where a `{ThemeResource}` binding
is not available.

| Member | Type / signature | Value | Purpose |
|---|---|---|---|
| `HighContrastSentinel` | `const string` | `"XcHighContrast"` | Key that only a consumer's high-contrast override dictionary defines (any value). Its presence in application resources (merged dictionaries included) switches `Surface` and `SurfaceOutline` to the override keys. |
| `SurfaceKey` | `const string` | `"XcSurfaceBrush"` | Override key for `Surface` while high contrast is active. |
| `SurfaceOutlineKey` | `const string` | `"XcSurfaceOutlineBrush"` | Override key for `SurfaceOutline` while high contrast is active. |
| `Surface(ElementTheme theme)` | `static SolidColorBrush` | Light: `White`; Dark: `#FF202020` | Pane surface: document backdrop, active document tab, splitter grab handle, auto-hide flyout body. |
| `SurfaceOutline(ElementTheme theme)` | `static SolidColorBrush` | Light: `#50000000`; Dark: `#50FFFFFF` | Hairline outline for a `Surface` on translucent chrome. |

With high contrast active, `Surface` / `SurfaceOutline` return the palette's `XcSurfaceBrush` /
`XcSurfaceOutlineBrush` when it is a `SolidColorBrush`; otherwise (key missing, wrong type, or
any exception) they return the Light/Dark value above. The `theme` argument is ignored while the
override applies, because a high-contrast palette has no Light/Dark split. Outside high contrast
each call allocates a new brush. `ElementTheme.Default` is treated as Light; pass an element's
`ActualTheme`, not its `RequestedTheme`.

### Resources (`Themes/Generic.xaml`)

Source: `ms-appx:///Xtreme.Components.Core/Themes/Generic.xaml`.

| Key | Type | Default |
|---|---|---|
| `XcFontSizeCaption` | `x:Double` | `12` |
| `XcFontSizeBody` | `x:Double` | `14` |
| `XcFontSizeTitle` | `x:Double` | `16` |
| `XcFieldLabelStyle` | `Style` (TextBlock) | `XcFontSizeCaption`, `TextFillColorSecondaryBrush`, margin `0,0,0,4` |
| `XcListSectionHeaderTextStyle` | `Style` (TextBlock) | `XcFontSizeCaption`, SemiBold, `TextFillColorSecondaryBrush`, margin `0,8,0,4` |
| `XcListViewItemStyle` | `Style` (ListViewItem) | `MinHeight` 32, `Padding` `8,4` |

`XcHighContrast`, `XcSurfaceBrush` and `XcSurfaceOutlineBrush` are deliberately not defined here;
their presence is the high-contrast switch. Which controls read which token is listed in
[theming.md](theming.md#2-tokens).

## Brush lookup rules

`ComponentHost.TryGetBrush(key, theme)`:

1. Returns null if there is no `Application.Current`.
2. Maps `ElementTheme.Default` to `EffectiveTheme()`.
3. Looks for `key` in `Application.Current.Resources.ThemeDictionaries["Dark"]` (for Dark) or
   `["Light"]` (for Light), returning it if it is a `Brush`.
4. Otherwise returns `Application.Current.Resources.TryGetValue(key)` if that is a `Brush`.

Step 3 reads only the application dictionary's **own** `ThemeDictionaries`. A key defined in a
merged dictionary's `ThemeDictionaries` is only reached by step 4, the framework's normal lookup,
which does not use the `theme` argument. Keys that must differ per theme should therefore be
defined in the top-level `ThemeDictionaries` of `App.xaml` (as the gallery does for its row-flag
brushes). Flat keys (same value in both themes) can live anywhere.

There is no `HighContrast` theme-dictionary branch: high contrast in this library is an
app-merged override palette, detected through `XcHighContrast` (see
[theming.md](theming.md#surface-code-painted-chrome-componenttheme)).

## Notes and limitations

- **Global, process-wide state.** `CurrentTheme` and `ResolveGeometry` are single static slots;
  two parts of an app (or two windows with different themes) cannot set different values.
  `EffectiveTheme` is one value for the whole app.
- **Errors are swallowed.** `TryGetBrush` and the high-contrast lookup in `ComponentTheme`
  catch every exception and return null / the default colour. A typo in a key fails silently
  (Primitives' brush converter paints hot pink in that case so the gap is visible).
- **`PrepareDialog`** looks up `DefaultContentDialogStyle` with `Application.Current.Resources`
  only and does not set `XamlRoot`.
- **Static event.** `ResourcesChanged` holds strong references to its handlers; subscribe from
  long-lived (static or app-lifetime) objects, or unsubscribe.
- **`XcListViewItemStyle`** sets size and padding only; the selection look is the WinUI default.
