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

    /// <summary>The theme to use for a code-built popup: the app's hook if set, else the app theme.</summary>
    public static ElementTheme EffectiveTheme()
    {
        var hooked = CurrentTheme?.Invoke();
        if (hooked is { } t && t != ElementTheme.Default)
            return t;
        return Application.Current?.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
    }

    /// <summary>
    /// Resolves a brush resource for the <see cref="EffectiveTheme"/>: the matching entry in the
    /// app's <c>ThemeDictionaries</c> ("Light" / "Dark") when one exists, else the plain
    /// application-scope lookup. Null when the key is not defined anywhere.
    /// </summary>
    public static Brush? TryGetBrush(string key)
    {
        try
        {
            var res = Application.Current?.Resources;
            if (res is null)
                return null;

            var themeKey = EffectiveTheme() == ElementTheme.Dark ? "Dark" : "Light";
            if (res.ThemeDictionaries.TryGetValue(themeKey, out var dictObj) && dictObj is ResourceDictionary dict
                && dict.TryGetValue(key, out var themed) && themed is Brush tb)
                return tb;

            return res.TryGetValue(key, out var value) && value is Brush b ? b : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Applies the app's <c>DefaultContentDialogStyle</c> (when defined) and the
    /// <see cref="EffectiveTheme"/> to a code-built <see cref="ContentDialog"/> so it renders
    /// on-theme. Call before <c>ShowAsync</c>.
    /// </summary>
    public static void PrepareDialog(ContentDialog dialog)
    {
        if (Application.Current?.Resources.TryGetValue("DefaultContentDialogStyle", out var style) == true && style is Style s)
            dialog.Style = s;
        dialog.RequestedTheme = EffectiveTheme();
    }
}
