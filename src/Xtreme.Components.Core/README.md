# Xtreme.Components.Core

The shared foundation of the [Xtreme.Components](https://github.com/XtremeCRM/Xtreme.Components)
control families. No controls live here; the family packages reference it.

**Full guide:** [docs/core.md](https://github.com/XtremeCRM/Xtreme.Components/blob/main/docs/core.md) covers every option with samples.

- `ComponentTheme.Surface(ElementTheme)` and `ComponentTheme.SurfaceOutline(ElementTheme)`:
  the pane-surface and hairline-outline brushes a control paints from code-behind when it
  cannot bind a `ThemeResource`. They follow the element's **actual** theme, which a plain
  `Application.Current.Resources` lookup does not.
- High-contrast overrides: when a consumer merges a high-contrast palette that defines the
  `XcHighContrast` sentinel key, the brushes come from `XcSurfaceBrush` and
  `XcSurfaceOutlineBrush` in that palette instead (resolved for the requested theme when the
  palette splits them into `ThemeDictionaries`). The brushes are shared per theme, not allocated
  per call; treat them as read-only.
- `ComponentHost`: the optional startup hooks (`CurrentTheme`, `ResolveGeometry`), a theme-aware
  `TryGetBrush(key, theme)` that also searches merged dictionaries' `ThemeDictionaries`, and
  `PrepareDialog(dialog, xamlRoot)`, which styles, themes and roots a code-built `ContentDialog`.

```xml
<!-- App.xaml, inside a high-contrast resource dictionary -->
<x:Boolean x:Key="XcHighContrast">True</x:Boolean>
<SolidColorBrush x:Key="XcSurfaceBrush" Color="{ThemeResource SystemColorWindowColor}" />
<SolidColorBrush x:Key="XcSurfaceOutlineBrush" Color="{ThemeResource SystemColorWindowTextColor}" />
```

Targets `net10.0` only; the consuming Uno head supplies the renderer. MIT license.
