using Microsoft.UI.Xaml.Controls;

namespace Xtreme.Components.Primitives;

/// <summary>
/// A <see cref="CalendarDatePicker"/> whose calendar popup uses compact fonts. The framework's
/// default <see cref="CalendarView"/> day-number font (DayItemFontSize 20) is oversized relative to
/// the app's body text; an app-level implicit style can't fix it (the default style from
/// XamlControlsResources wins and replacing it would drop the template), so we set the font sizes
/// directly on the CalendarView template part when the template is applied.
///
/// Day and month/year numbers use the <c>XcFontSizeBody</c> token (14 by default); the small
/// "first of month / year" labels stay at 11, below the type ramp's smallest token.
/// </summary>
public partial class CompactCalendarDatePicker : CalendarDatePicker
{
    private const double FirstOfLabelFontSize = 11;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("CalendarView") is CalendarView cv)
        {
            var body = FontSizeToken("XcFontSizeBody", 14);
            cv.DayItemFontSize = body;
            cv.FirstOfMonthLabelFontSize = FirstOfLabelFontSize;
            cv.MonthYearItemFontSize = body;
            cv.FirstOfYearDecadeLabelFontSize = FirstOfLabelFontSize;
        }
    }

    // Element resources first (a page-level override), then the app's; the Core default otherwise.
    private double FontSizeToken(string key, double fallback)
    {
        if (Resources.TryGetValue(key, out var local) && local is double l) return l;
        return Application.Current?.Resources.TryGetValue(key, out var app) == true && app is double a ? a : fallback;
    }
}
