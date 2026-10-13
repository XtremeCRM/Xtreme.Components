using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Xtreme.Components.Core;

/// <summary>
/// Per-theme surface colors shared by the Docking and Cards families (and any consumer that
/// paints chrome from code-behind).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately NOT a plain lookup of <c>ApplicationPageBackgroundThemeBrush</c> in
/// <c>Application.Current.Resources</c>: from code-behind that lookup does not resolve against the
/// element's actual theme — in dark mode it produced the LIGHT value (or missed entirely and hit a
/// hardcoded White fallback), painting the active tab, the splitter handle and the auto-hide flyout
/// white under white text. The host rebuilds on <c>ActualThemeChanged</c>, which re-runs every call
/// site.
/// </para>
/// <para>
/// XU-503: the hardcoded Light value is what the DockHost paints the whole document backdrop with
/// (DockHost.cs:447), so under a high-contrast theme that backdrop stayed pure white no matter what
/// the palette said — no theme token could reach it. High contrast is applied by merging a FLAT
/// override dictionary into application resources, and because that palette has no Light/Dark
/// split, the app-scope lookup that is wrong for Light/Dark is unambiguous here. Presence of the
/// dictionary is detected by a sentinel key that only the high-contrast palettes define.
/// </para>
/// <para>
/// Brushes are shared, not allocated per call: the built-in Light and Dark values are created once
/// per theme, and a high-contrast override returns the palette's own brush instance. Treat every
/// returned brush as read-only. The high-contrast check itself is NOT cached: palettes are merged and
/// removed at runtime (a consumer is not required to call <see cref="ComponentHost.NotifyResourcesChanged"/>),
/// so each call re-reads the sentinel, which is a single dictionary lookup.
/// </para>
/// </remarks>
public static class ComponentTheme
{
    /// <summary>
    /// Key that exists ONLY in a consumer's high-contrast override dictionary, so finding it means one
    /// is currently merged into application resources. A consumer that ships high-contrast palettes
    /// defines this key (any value) alongside <see cref="SurfaceKey"/> and <see cref="SurfaceOutlineKey"/>.
    /// </summary>
    public const string HighContrastSentinel = "XcHighContrast";

    /// <summary>Resource key a high-contrast palette defines to override <see cref="Surface"/>.</summary>
    public const string SurfaceKey = "XcSurfaceBrush";

    /// <summary>Resource key a high-contrast palette defines to override <see cref="SurfaceOutline"/>.</summary>
    public const string SurfaceOutlineKey = "XcSurfaceOutlineBrush";

    // The built-in (non-high-contrast) brushes, created on first use per theme. Thread-static
    // because a brush is a DependencyObject bound to the thread that created it; on Uno every
    // window shares one UI thread, so in practice this is one set of four brushes.
    [ThreadStatic] private static SolidColorBrush? s_surfaceLight, s_surfaceDark, s_outlineLight, s_outlineDark;

    /// <summary>
    /// The pane-surface color — what the document backdrop, the active document tab, the splitter
    /// grab handle and the auto-hide flyout body use to read as a solid piece of the page. Matches
    /// the Fluent dark page background rather than pure black so it still separates from the seams.
    /// The returned brush is shared; do not modify it.
    /// </summary>
    public static SolidColorBrush Surface(ElementTheme theme)
        => HighContrastBrush(SurfaceKey, theme)
           ?? (theme == ElementTheme.Dark
               ? s_surfaceDark ??= new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20))
               : s_surfaceLight ??= new SolidColorBrush(Microsoft.UI.Colors.White));

    /// <summary>
    /// Hairline outline for a <see cref="Surface"/> sitting on the translucent chrome washes — dark
    /// ink in light mode, light ink in dark mode (a black outline vanishes on a dark strip).
    /// The returned brush is shared; do not modify it.
    /// </summary>
    public static SolidColorBrush SurfaceOutline(ElementTheme theme)
        => HighContrastBrush(SurfaceOutlineKey, theme)
           ?? (theme == ElementTheme.Dark
               ? s_outlineDark ??= new SolidColorBrush(Windows.UI.Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF))
               : s_outlineLight ??= new SolidColorBrush(Windows.UI.Color.FromArgb(0x50, 0x00, 0x00, 0x00)));

    /// <summary>
    /// The brush for <paramref name="key"/> when a high-contrast palette is merged, else null so
    /// the caller keeps its Light/Dark behavior. The key is resolved through
    /// <see cref="ComponentHost.TryGetBrush(string, ElementTheme)"/> for <paramref name="theme"/>:
    /// a flat palette (the usual shape, one palette per contrast polarity) gives the same brush for
    /// either theme, while a palette that splits the key into <c>ThemeDictionaries</c> gets the entry
    /// for the theme asked for. A missing key, a non-solid brush or an expected lookup failure
    /// degrades to the normal palette rather than throwing.
    /// </summary>
    private static SolidColorBrush? HighContrastBrush(string key, ElementTheme theme)
    {
        try
        {
            var res = Application.Current?.Resources;
            if (res is null)
                return null;

            // TryGetValue, NOT ContainsKey: ContainsKey inspects only this dictionary's own
            // entries and does not walk MergedDictionaries, so it always missed the sentinel
            // (the high-contrast palette is merged, never a direct entry) and every lookup
            // fell back to the hardcoded Light value — the document backdrop stayed white
            // with high contrast plainly active.
            if (!res.TryGetValue(HighContrastSentinel, out _))
                return null;

            return ComponentHost.TryGetBrush(key, theme) as SolidColorBrush;
        }
        catch (Exception ex) when (ComponentHost.IsLookupFailure(ex))
        {
            return null;
        }
    }
}
