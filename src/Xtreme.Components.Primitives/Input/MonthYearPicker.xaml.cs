namespace Xtreme.Components.Primitives;

/// <summary>
/// A month chooser: previous/next arrows around a button that opens a month-and-year flyout.
/// <see cref="SelectedDate"/> is always the <b>1st of its month at midnight</b> (offset kept): any value
/// written to it, from code, a binding, the arrows or the flyout, is normalized to that, so hosts can
/// compare and query by it directly (XU-664).
/// </summary>
public sealed partial class MonthYearPicker : UserControl
{
    // The last normalized value SelectedDateChanged reported, so normalizing an incoming mid-month
    // value back to the same month does not raise a second, empty change.
    private DateTimeOffset _published;

    public MonthYearPicker()
    {
        this.InitializeComponent();

        SelectedDate = DateTimeOffset.Now; // normalized to the 1st by the change callback
        _published = SelectedDate;
        UpdateDisplay();
    }

    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate), typeof(DateTimeOffset), typeof(MonthYearPicker),
        new PropertyMetadata(FirstOfMonth(DateTimeOffset.Now), OnSelectedDateChanged));

    /// <summary>The selected month: always the 1st of the month at 00:00, in the offset it was given.</summary>
    public DateTimeOffset SelectedDate
    {
        get => (DateTimeOffset)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>
    /// Raised after <see cref="SelectedDate"/> moves to a different month, from the arrows, the flyout,
    /// code or a binding. The argument is the new (normalized) value.
    /// </summary>
    public event EventHandler<DateTimeOffset>? SelectedDateChanged;

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MonthYearPicker ctl) return;

        var value = (DateTimeOffset)e.NewValue;
        var normalized = FirstOfMonth(value);
        if (normalized != value)
        {
            ctl.SelectedDate = normalized; // re-enters with the normalized value
            return;
        }

        ctl.UpdateDisplay();
        if (normalized != ctl._published)
        {
            ctl._published = normalized;
            ctl.SelectedDateChanged?.Invoke(ctl, normalized);
        }
    }

    private static DateTimeOffset FirstOfMonth(DateTimeOffset value)
        => new(value.Year, value.Month, 1, 0, 0, 0, value.Offset);

    public static readonly DependencyProperty IncrementMonthsProperty = DependencyProperty.Register(
        nameof(IncrementMonths), typeof(int), typeof(MonthYearPicker), new PropertyMetadata(1));

    public int IncrementMonths
    {
        get => (int)GetValue(IncrementMonthsProperty);
        set => SetValue(IncrementMonthsProperty, value);
    }

    private void UpdateDisplay()
    {
        PART_MonthText.Text = SelectedDate.ToString("MMM");
        PART_YearText.Text = SelectedDate.ToString("yyyy");
    }

    private void OnDecrementClick(object sender, RoutedEventArgs e)
        => SelectedDate = SelectedDate.AddMonths(-Math.Max(1, IncrementMonths));

    private void OnIncrementClick(object sender, RoutedEventArgs e)
        => SelectedDate = SelectedDate.AddMonths(Math.Max(1, IncrementMonths));

    private void OnCenterClick(object sender, RoutedEventArgs e)
    {
        if (PART_Center.Flyout is Microsoft.UI.Xaml.Controls.DatePickerFlyout dp)
        {
            dp.Date = SelectedDate;
            dp.ShowAt(PART_Center);
        }
        else if (PART_Center.Flyout is Flyout f)
        {
            f.ShowAt(PART_Center);
        }
    }

    // Called from XAML closing handler on the DatePickerFlyout
    private void OnDatePickerFlyoutClosed(object sender, object e)
    {
        if (PART_Center.Flyout is Microsoft.UI.Xaml.Controls.DatePickerFlyout dp)
        {
            try
            {
                // XU-664: the flyout is a full day picker, but this is a month control — the change
                // callback normalizes to the 1st. A mid-month day made every battery-plan screen
                // (EndDate is always the 1st) silently return zero rows.
                SelectedDate = dp.Date;
            }
            catch { }
        }
    }
}
