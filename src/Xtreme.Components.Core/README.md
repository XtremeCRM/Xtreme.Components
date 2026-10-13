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
  `XcSurfaceOutlineBrush` in that palette instead.

```xml
<!-- App.xaml, inside a high-contrast resource dictionary -->
<x:Boolean x:Key="XcHighContrast">True</x:Boolean>
<SolidColorBrush x:Key="XcSurfaceBrush" Color="{ThemeResource SystemColorWindowColor}" />
<SolidColorBrush x:Key="XcSurfaceOutlineBrush" Color="{ThemeResource SystemColorWindowTextColor}" />
```

Targets `net10.0` only; the consuming Uno head supplies the renderer. MIT licence.
