namespace Xtreme.Components.Core;

/// <summary>
/// Optional hooks the hosting app can set once at startup so library controls follow the app's
/// own conventions. Every hook is optional: a bare app sets none and the controls fall back to
/// WinUI defaults.
/// </summary>
/// <remarks>
/// This is the only global state in the library. It exists because a few things have no clean
/// per-instance seam: which theme a code-built popup should request when the app drives its
/// theme from a root element rather than <c>Application.RequestedTheme</c>, and how a glyph key
/// like <c>"price_tag"</c> turns into vector data the app owns.
/// </remarks>
public static class ComponentHost
{
    /// <summary>
    /// The theme the app is currently showing, when the app drives it from a root element's
    /// <c>RequestedTheme</c> (a live theme switch) rather than from the OS. Used by dialogs the
    /// library builds in code and by resource lookups that must match the visible theme.
    /// Null, or returning null, means "use the application theme".
    /// </summary>
    public static Func<ElementTheme?>? CurrentTheme { get; set; }

    /// <summary>
    /// Turns an icon key into vector geometry. Set it when the app has its own path data for
    /// keys a control exposes (for example <c>OverlayGlyphIcon.BackGeometryKey</c>). Null, or a
    /// resolver that returns null, means the key has no vector and the control shows nothing for it.
    /// </summary>
    public static Func<string, Geometry?>? ResolveGeometry { get; set; }

    /// <summary>
    /// Raised by <see cref="NotifyResourcesChanged"/>. Code palettes that cache token lookups
    /// (the Scheduler's, the calendar pickers') drop their caches on it.
    /// </summary>
    public static event EventHandler? ResourcesChanged;

    /// <summary>
    /// Call after merging or removing a resource dictionary at runtime (a high-contrast palette,
    /// a brand switch) so cached token lookups re-resolve. Theme switches need no call: palettes
    /// are keyed by theme and controls re-render on <c>ActualThemeChanged</c>.
    /// </summary>
    public static void NotifyResourcesChanged() => ResourcesChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>The theme to use for a code-built popup: the app's hook if set, else the app theme.</summary>
    public static ElementTheme EffectiveTheme()
    {
        var hooked = CurrentTheme?.Invoke();
        if (hooked is { } t && t != ElementTheme.Default)
            return t;
        return Application.Current?.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
    }

    /// <summary>
    /// Resolves a brush resource for the <see cref="EffectiveTheme"/>; see
    /// <see cref="TryGetBrush(string, ElementTheme)"/> for the search order.
    /// </summary>
    public static Brush? TryGetBrush(string key) => TryGetBrush(key, EffectiveTheme());

    /// <summary>
    /// Resolves a brush resource for an explicit theme (typically an element's <c>ActualTheme</c>).
    /// Searches the application dictionary, then its merged dictionaries (last merged first,
    /// recursively); in each dictionary the entry in its <c>ThemeDictionaries</c> for
    /// <paramref name="theme"/> is tried before its own flat entry. So a per-theme value defined in
    /// a merged library or palette dictionary resolves for the requested theme, and a definition in
    /// the application dictionary still overrides one merged into it. Falls back to the framework's
    /// own application-scope lookup. Null when the key is not defined anywhere or is not a
    /// <see cref="Brush"/>.
    /// </summary>
    public static Brush? TryGetBrush(string key, ElementTheme theme)
    {
        try
        {
            var res = Application.Current?.Resources;
            if (res is null)
                return null;

            if (theme == ElementTheme.Default)
                theme = EffectiveTheme();

            if (FindThemed(res, key, theme, depth: 0) is Brush found)
                return found;

            return res.TryGetValue(key, out var value) && value is Brush b ? b : null;
        }
        catch (Exception ex) when (IsLookupFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// <see cref="PrepareDialog(ContentDialog, XamlRoot?)"/> without an explicit root: applies the
    /// app's <c>DefaultContentDialogStyle</c> (when defined) and the <see cref="EffectiveTheme"/>
    /// to a code-built <see cref="ContentDialog"/> so it renders on-theme, and derives
    /// <c>XamlRoot</c> only when the dialog has none (see the overload). Call before <c>ShowAsync</c>.
    /// </summary>
    public static void PrepareDialog(ContentDialog dialog) => PrepareDialog(dialog, null);

    /// <summary>
    /// Applies the app's <c>DefaultContentDialogStyle</c> (when defined as a <see cref="Style"/>)
    /// and the <see cref="EffectiveTheme"/> to a code-built <see cref="ContentDialog"/>, and sets the
    /// <c>XamlRoot</c> it shows in (<c>ShowAsync</c> throws without one on WinUI 3 and Uno).
    /// <paramref name="xamlRoot"/> wins when given (pass the invoking element's <c>XamlRoot</c>);
    /// otherwise a root the dialog already has is kept, and failing that one is taken from the
    /// dialog's <c>Content</c> when that element is already in a live tree. Call before <c>ShowAsync</c>.
    /// </summary>
    public static void PrepareDialog(ContentDialog dialog, XamlRoot? xamlRoot)
    {
        if (Application.Current?.Resources.TryGetValue("DefaultContentDialogStyle", out var style) == true && style is Style s)
            dialog.Style = s;
        dialog.RequestedTheme = EffectiveTheme();

        var root = xamlRoot ?? dialog.XamlRoot ?? (dialog.Content as UIElement)?.XamlRoot;
        if (root is not null && !ReferenceEquals(dialog.XamlRoot, root))
            dialog.XamlRoot = root;
    }

    // Deepest merge chain walked. Real apps nest two or three levels (App -> XamlControlsResources
    // -> its own merges); the cap only guards against a dictionary that ends up merged into itself.
    private const int MaxMergeDepth = 16;

    // One dictionary's view of the key: its ThemeDictionaries entry for the theme, then its own flat
    // entry, then its merged dictionaries (later merges win, as in the framework). Returns the raw
    // value; the first hit stops the search, as it does in the framework.
    private static object? FindThemed(ResourceDictionary dict, string key, ElementTheme theme, int depth)
    {
        if (depth > MaxMergeDepth)
            return null;

        if (ThemeDictionary(dict, theme) is { } themed && themed.TryGetValue(key, out var tv))
            return tv;

        // Keys (not ContainsKey or TryGetValue) because only this dictionary's OWN entry belongs at
        // this step: on Uno both of those also walk the merged dictionaries and the ThemeDictionaries
        // of the ACTIVE theme, which would hand back the other theme's value from a merged palette.
        if (dict.Keys.Contains(key))
            return dict[key];

        var merged = dict.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            if (FindThemed(merged[i], key, theme, depth + 1) is { } hit)
                return hit;
        }
        return null;
    }

    // A dictionary's ThemeDictionaries entry for the theme. "Dark" falls back to "Default", which
    // WinUI also uses for the dark theme. There is no "HighContrast" branch: high contrast in this
    // library is an app-merged override palette (see ComponentTheme).
    private static ResourceDictionary? ThemeDictionary(ResourceDictionary dict, ElementTheme theme)
    {
        var themes = dict.ThemeDictionaries;
        if (themes.Count == 0)
            return null;
        if (theme == ElementTheme.Dark)
        {
            if (themes.TryGetValue("Dark", out var dark) && dark is ResourceDictionary d)
                return d;
            return themes.TryGetValue("Default", out var dflt) && dflt is ResourceDictionary df ? df : null;
        }
        return themes.TryGetValue("Light", out var light) && light is ResourceDictionary l ? l : null;
    }

    /// <summary>
    /// The failures a resource lookup can legitimately hit, which should read as "not found": a key
    /// removed between the existence check and the read, a merged-dictionary list changed during
    /// the walk, a lazily built resource whose markup fails to materialize, and the platform's
    /// interop error for a failed lookup on WinAppSDK. Anything else is a bug and propagates.
    /// </summary>
    internal static bool IsLookupFailure(Exception ex)
        => ex is KeyNotFoundException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException
            or Microsoft.UI.Xaml.Markup.XamlParseException;
}
