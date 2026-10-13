namespace Xtreme.Components.Primitives;

/// <summary>
/// Theme-keyed colors for the calendar-style pickers (<see cref="MultiMonthDateNavigator"/>).
/// </summary>
/// <remarks>
/// Keyed off the element's <c>ActualTheme</c> rather than read from <c>Application.Current.Resources</c>:
/// an app-scope brush lookup resolves against the app theme, not the element's, so a dark element in
/// a light-themed app painted light. Values mirror the Scheduler palette so a navigator embedded in
/// the scheduler toolbar matches its host. Migration to <c>Xc*</c> theme tokens is tracked separately.
/// </remarks>
internal sealed record CalendarPalette
{
    public required Brush SecondaryForeground { get; init; }
    public required Brush MinorLine { get; init; }
    public required Brush SelectionFill { get; init; }
    public required Brush SelectionForeground { get; init; }
    public required Brush OutsideMonthFill { get; init; }
    public required Brush OutsideMonthForeground { get; init; }
    public required Brush HeaderForeground { get; init; }
    public required Brush WeekendForeground { get; init; }

    /// <summary>
    /// Token-first: each role checks the app's resources for <c>XcCalendar&lt;Role&gt;Brush</c>
    /// (theme-aware through <see cref="Xtreme.Components.Core.ComponentHost.TryGetBrush(string, ElementTheme)"/>)
    /// and falls back to the built-in value. Resolved once per theme; dropped on
    /// <c>ComponentHost.NotifyResourcesChanged</c>.
    /// </summary>
    public static CalendarPalette For(ElementTheme theme)
    {
        if (theme == ElementTheme.Dark)
            return _dark ??= Resolve(Dark, ElementTheme.Dark);
        return _light ??= Resolve(Light, ElementTheme.Light);
    }

    private static CalendarPalette? _light;
    private static CalendarPalette? _dark;

    static CalendarPalette()
    {
        Xtreme.Components.Core.ComponentHost.ResourcesChanged += (_, _) => { _light = null; _dark = null; };
    }

    private static CalendarPalette Resolve(CalendarPalette fallback, ElementTheme theme)
    {
        Brush Tok(string role, Brush dflt) =>
            Xtreme.Components.Core.ComponentHost.TryGetBrush("XcCalendar" + role + "Brush", theme) ?? dflt;

        return fallback with
        {
            SecondaryForeground = Tok(nameof(SecondaryForeground), fallback.SecondaryForeground),
            MinorLine = Tok(nameof(MinorLine), fallback.MinorLine),
            SelectionFill = Tok(nameof(SelectionFill), fallback.SelectionFill),
            SelectionForeground = Tok(nameof(SelectionForeground), fallback.SelectionForeground),
            OutsideMonthFill = Tok(nameof(OutsideMonthFill), fallback.OutsideMonthFill),
            OutsideMonthForeground = Tok(nameof(OutsideMonthForeground), fallback.OutsideMonthForeground),
            HeaderForeground = Tok(nameof(HeaderForeground), fallback.HeaderForeground),
            WeekendForeground = Tok(nameof(WeekendForeground), fallback.WeekendForeground),
        };
    }

    private static readonly CalendarPalette Light = new()
    {
        HeaderForeground = Solid(0xFF, 0x20, 0x20, 0x20),
        WeekendForeground = Solid(0xFF, 0xC0, 0x3A, 0x2B),
        SecondaryForeground = Solid(0xFF, 0x6E, 0x6E, 0x6E),
        MinorLine = Solid(0xFF, 0xD8, 0xD8, 0xD8),
        SelectionFill = Solid(0xFF, 0x00, 0x6C, 0xBE),
        SelectionForeground = Solid(0xFF, 0xFF, 0xFF, 0xFF),
        OutsideMonthFill = Solid(0x14, 0x00, 0x00, 0x00),
        OutsideMonthForeground = Solid(0xFF, 0xAD, 0xAD, 0xAD),
    };

    private static readonly CalendarPalette Dark = new()
    {
        HeaderForeground = Solid(0xFF, 0xF0, 0xF0, 0xF0),
        WeekendForeground = Solid(0xFF, 0xF0, 0x8A, 0x7E),
        SecondaryForeground = Solid(0xFF, 0xB0, 0xB0, 0xB0),
        MinorLine = Solid(0xFF, 0x3A, 0x3A, 0x3A),
        SelectionFill = Solid(0xFF, 0x2F, 0x6F, 0xB5),
        SelectionForeground = Solid(0xFF, 0xFF, 0xFF, 0xFF),
        OutsideMonthFill = Solid(0x1F, 0xFF, 0xFF, 0xFF),
        OutsideMonthForeground = Solid(0xFF, 0x70, 0x70, 0x70),
    };

    private static SolidColorBrush Solid(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));
}
