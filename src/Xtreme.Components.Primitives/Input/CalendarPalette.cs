namespace Xtreme.Components.Primitives;

/// <summary>
/// Theme-keyed colours for the calendar-style pickers (<see cref="MultiMonthDateNavigator"/>).
/// </summary>
/// <remarks>
/// Keyed off the element's <c>ActualTheme</c> rather than read from <c>Application.Current.Resources</c>:
/// an app-scope brush lookup resolves against the app theme, not the element's, so a dark element in
/// a light-themed app painted light. Values mirror the Scheduler palette so a navigator embedded in
/// the scheduler toolbar matches its host. Migration to <c>Xc*</c> theme tokens is tracked separately.
/// </remarks>
internal sealed class CalendarPalette
{
    public required Brush SecondaryForeground { get; init; }
    public required Brush MinorLine { get; init; }
    public required Brush SelectionFill { get; init; }
    public required Brush SelectionForeground { get; init; }
    public required Brush OutsideMonthFill { get; init; }
    public required Brush OutsideMonthForeground { get; init; }
    public required Brush HeaderForeground { get; init; }
    public required Brush WeekendForeground { get; init; }

    public static CalendarPalette For(ElementTheme theme) => theme == ElementTheme.Dark ? Dark : Light;

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
