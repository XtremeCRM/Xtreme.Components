namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// Centralized, theme-aware brush set for the scheduler's code-built visuals (gridlines,
/// event blocks, headers). Code-heavy rendering can't lean on XAML <c>{ThemeResource}</c>
/// markup cleanly, so the palette resolves light/dark itself. Views re-render on
/// <see cref="FrameworkElement.ActualThemeChanged"/> to pick up theme switches.
/// </summary>
internal sealed record SchedulerPalette
{
    /// <summary>Opaque surface behind the whole control (the grid body sits on this). Must be
    /// opaque so a host page's background/watermark doesn't bleed through the transparent cells.</summary>
    public required Brush SurfaceBackground { get; init; }
    public required Brush HeaderBackground { get; init; }
    public required Brush HeaderForeground { get; init; }
    public required Brush SecondaryForeground { get; init; }
    public required Brush MinorLine { get; init; }
    public required Brush MajorLine { get; init; }
    public required Brush TodayHighlight { get; init; }

    /// <summary>Tint for OFF-hours in the time grid. Work hours keep the lighter base surface, so the
    /// hours before / after the work day read darker. Translucent so it dims rather than covers.</summary>
    public required Brush OffHoursBackground { get; init; }
    public required Brush EventDefaultBack { get; init; }
    public required Brush EventDefaultBar { get; init; }
    public required Brush EventForeground { get; init; }

    /// <summary>Outline drawn around every event block so it reads distinctly against the grid.</summary>
    public required Brush EventBorder { get; init; }
    public required Brush OutsideMonthForeground { get; init; }
    public required Brush AgendaRule { get; init; }
    public required Brush AccentForeground { get; init; }

    /// <summary>Muted red for Saturday/Sunday day numbers in the date navigator (Legacy DateNavigator parity).</summary>
    public required Brush WeekendForeground { get; init; }

    /// <summary>Fill behind a SELECTED day in the date navigator, with the text colour that reads on
    /// it. XU-413: these replace AccentFillColorDefaultBrush / TextOnAccentFillColorPrimaryBrush read
    /// from Application.Current.Resources, which resolve against the APPLICATION theme and so stayed
    /// Light while the navigator rendered Dark.</summary>
    public required Brush SelectionFill { get; init; }
    public required Brush SelectionForeground { get; init; }

    /// <summary>Faint fill behind adjacent-month days, so the current month still reads as a block
    /// while the neighbours stay obviously live rather than greyed-out-disabled.</summary>
    public required Brush OutsideMonthFill { get; init; }

    /// <summary>
    /// Resolve the palette for the element's current theme. Token-first: every role checks the
    /// app's resources for <c>XcScheduler&lt;Role&gt;Brush</c> (through
    /// <see cref="Xtreme.Components.Core.ComponentHost.TryGetBrush(string, ElementTheme)"/>, so a
    /// Light/Dark <c>ThemeDictionaries</c> entry wins over a flat one) and falls back to the built-in
    /// value. Resolved once per theme and dropped on <c>ComponentHost.NotifyResourcesChanged</c>.
    /// </summary>
    public static SchedulerPalette For(ElementTheme theme)
    {
        if (theme == ElementTheme.Dark)
            return _dark ??= Resolve(Dark, ElementTheme.Dark);
        return _light ??= Resolve(Light, ElementTheme.Light);
    }

    private static SchedulerPalette? _light;
    private static SchedulerPalette? _dark;

    static SchedulerPalette()
    {
        Xtreme.Components.Core.ComponentHost.ResourcesChanged += (_, _) => { _light = null; _dark = null; };
    }

    private static SchedulerPalette Resolve(SchedulerPalette fallback, ElementTheme theme)
    {
        Brush Tok(string role, Brush dflt) =>
            Xtreme.Components.Core.ComponentHost.TryGetBrush("XcScheduler" + role + "Brush", theme) ?? dflt;

        return fallback with
        {
            SurfaceBackground = Tok(nameof(SurfaceBackground), fallback.SurfaceBackground),
            HeaderBackground = Tok(nameof(HeaderBackground), fallback.HeaderBackground),
            HeaderForeground = Tok(nameof(HeaderForeground), fallback.HeaderForeground),
            SecondaryForeground = Tok(nameof(SecondaryForeground), fallback.SecondaryForeground),
            MinorLine = Tok(nameof(MinorLine), fallback.MinorLine),
            MajorLine = Tok(nameof(MajorLine), fallback.MajorLine),
            TodayHighlight = Tok(nameof(TodayHighlight), fallback.TodayHighlight),
            OffHoursBackground = Tok(nameof(OffHoursBackground), fallback.OffHoursBackground),
            EventDefaultBack = Tok(nameof(EventDefaultBack), fallback.EventDefaultBack),
            EventDefaultBar = Tok(nameof(EventDefaultBar), fallback.EventDefaultBar),
            EventForeground = Tok(nameof(EventForeground), fallback.EventForeground),
            EventBorder = Tok(nameof(EventBorder), fallback.EventBorder),
            OutsideMonthForeground = Tok(nameof(OutsideMonthForeground), fallback.OutsideMonthForeground),
            AgendaRule = Tok(nameof(AgendaRule), fallback.AgendaRule),
            AccentForeground = Tok(nameof(AccentForeground), fallback.AccentForeground),
            WeekendForeground = Tok(nameof(WeekendForeground), fallback.WeekendForeground),
            SelectionFill = Tok(nameof(SelectionFill), fallback.SelectionFill),
            SelectionForeground = Tok(nameof(SelectionForeground), fallback.SelectionForeground),
            OutsideMonthFill = Tok(nameof(OutsideMonthFill), fallback.OutsideMonthFill),
        };
    }

    private static readonly SchedulerPalette Light = new()
    {
        SurfaceBackground = Solid(0xFF, 0xFF, 0xFF, 0xFF),
        HeaderBackground = Solid(0xFF, 0xF7, 0xF7, 0xF7),
        HeaderForeground = Solid(0xFF, 0x20, 0x20, 0x20),
        SecondaryForeground = Solid(0xFF, 0x6E, 0x6E, 0x6E),
        MinorLine = Solid(0xFF, 0xD8, 0xD8, 0xD8),
        MajorLine = Solid(0xFF, 0xBF, 0xBF, 0xBF),
        TodayHighlight = Solid(0x18, 0x00, 0x78, 0xD4),
        // Off-hours: a subtle translucent darkening so the work-hours window stays the lighter base
        // surface and the hours outside it read a touch darker. Translucent so it doesn't block the
        // today tint / host watermark the way an opaque fill would.
        OffHoursBackground = Solid(0x14, 0x00, 0x00, 0x00),
        EventDefaultBack = Solid(0xFF, 0xE6, 0xEC, 0xF6),
        EventDefaultBar = Solid(0xFF, 0x4F, 0x6B, 0xED),
        EventForeground = Solid(0xFF, 0x20, 0x20, 0x20),
        EventBorder = Solid(0xFF, 0x9A, 0x9A, 0x9A),
        OutsideMonthForeground = Solid(0xFF, 0xAD, 0xAD, 0xAD),
        AgendaRule = Solid(0xFF, 0xE0, 0xE0, 0xE0),
        AccentForeground = Solid(0xFF, 0x00, 0x6C, 0xBE),
        WeekendForeground = Solid(0xFF, 0xC0, 0x3A, 0x2B),
        // White on this fill measures 5.40:1.
        SelectionFill = Solid(0xFF, 0x00, 0x6C, 0xBE),
        SelectionForeground = Solid(0xFF, 0xFF, 0xFF, 0xFF),
        OutsideMonthFill = Solid(0x14, 0x00, 0x00, 0x00),
    };

    private static readonly SchedulerPalette Dark = new()
    {
        SurfaceBackground = Solid(0xFF, 0x1F, 0x1F, 0x1F),
        HeaderBackground = Solid(0xFF, 0x26, 0x26, 0x26),
        HeaderForeground = Solid(0xFF, 0xF0, 0xF0, 0xF0),
        SecondaryForeground = Solid(0xFF, 0xB0, 0xB0, 0xB0),
        MinorLine = Solid(0xFF, 0x3A, 0x3A, 0x3A),
        MajorLine = Solid(0xFF, 0x55, 0x55, 0x55),
        TodayHighlight = Solid(0x24, 0x60, 0xCD, 0xFF),
        // Off-hours darkening (see Light theme note); work hours keep the lighter #1F1F1F surface.
        OffHoursBackground = Solid(0x33, 0x00, 0x00, 0x00),
        EventDefaultBack = Solid(0xFF, 0x33, 0x38, 0x40),
        EventDefaultBar = Solid(0xFF, 0x60, 0xA0, 0xFF),
        EventForeground = Solid(0xFF, 0xF0, 0xF0, 0xF0),
        EventBorder = Solid(0xFF, 0x6E, 0x6E, 0x6E),
        OutsideMonthForeground = Solid(0xFF, 0x70, 0x70, 0x70),
        AgendaRule = Solid(0xFF, 0x3A, 0x3A, 0x3A),
        AccentForeground = Solid(0xFF, 0x60, 0xCD, 0xFF),
        WeekendForeground = Solid(0xFF, 0xF0, 0x8A, 0x7E),
        // Deeper than the light fill so it still separates from the #1F1F1F surface (3.19:1)
        // while keeping white text at 5.17:1.
        SelectionFill = Solid(0xFF, 0x2F, 0x6F, 0xB5),
        SelectionForeground = Solid(0xFF, 0xFF, 0xFF, 0xFF),
        OutsideMonthFill = Solid(0x1F, 0xFF, 0xFF, 0xFF),
    };

    private static SolidColorBrush Solid(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    /// <summary>Choose readable text (near-black or near-white) for a given fill color.</summary>
    public static Brush ContrastForeground(Color back)
    {
        // W3C "colour brightness" (perceived-brightness luma) with the standard 125/255 crossover — NOT WCAG
        // relative luminance. For the black/white *decision* on saturated fills, luma matches convention:
        // white-on-red, black-on-green/yellow. (Maximising WCAG contrast ratio instead picks black-on-red,
        // because pure red's luminance edges just over its crossover — technically higher contrast but reads
        // wrong.) The previous luma cutoff was 0.6, which was too high: a bright green scored ~0.59 and fell
        // to unreadable white text; 0.5 fixes green while keeping red/navy on white.
        var brightness = (0.299 * back.R + 0.587 * back.G + 0.114 * back.B) / 255.0;
        return brightness > 0.5
            ? new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A))
            : new SolidColorBrush(Color.FromArgb(0xFF, 0xFA, 0xFA, 0xFA));
    }
}
