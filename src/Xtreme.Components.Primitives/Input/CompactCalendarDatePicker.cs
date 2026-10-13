using Microsoft.UI.Xaml.Controls;

namespace Xtreme.Components.Primitives;

/// <summary>
/// A <see cref="CalendarDatePicker"/> whose calendar popup uses compact fonts. The framework's
/// default <see cref="CalendarView"/> day-number font (DayItemFontSize 20) is oversized relative to
/// the app's body text; an app-level implicit style can't fix it (the default style from
/// XamlControlsResources wins and replacing it would drop the template), so we set the font sizes
/// directly on the CalendarView template part when the template is applied.
/// </summary>
public partial class CompactCalendarDatePicker : CalendarDatePicker
{
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("CalendarView") is CalendarView cv)
        {
            cv.DayItemFontSize = 14;
            cv.FirstOfMonthLabelFontSize = 11;
            cv.MonthYearItemFontSize = 14;
            cv.FirstOfYearDecadeLabelFontSize = 11;
        }
    }
}
