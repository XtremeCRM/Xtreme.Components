namespace Xtreme.Components.Primitives;

public sealed partial class MonthYearPicker : UserControl
{
    public MonthYearPicker()
    {
        this.InitializeComponent();

        SelectedDate = DateTimeOffset.Now;
        UpdateDisplay();
    }

    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate), typeof(DateTimeOffset), typeof(MonthYearPicker),
        new PropertyMetadata(DateTimeOffset.Now, OnSelectedDateChanged));

    public DateTimeOffset SelectedDate
    {
        get => (DateTimeOffset)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MonthYearPicker ctl)
        {
            ctl.UpdateDisplay();
        }
    }

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
    {
        var dt = SelectedDate.DateTime.AddMonths(-Math.Max(1, IncrementMonths));
        SelectedDate = new DateTimeOffset(dt, SelectedDate.Offset);
    }

    private void OnIncrementClick(object sender, RoutedEventArgs e)
    {
        var dt = SelectedDate.DateTime.AddMonths(Math.Max(1, IncrementMonths));
        SelectedDate = new DateTimeOffset(dt, SelectedDate.Offset);
    }

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
                // XU-664: the flyout is a full day picker, but this is a month control — keep the
                // 1st of the month. A mid-month day made every battery-plan screen (EndDate is
                // always the 1st) silently return zero rows.
                var picked = dp.Date;
                SelectedDate = new DateTimeOffset(picked.Year, picked.Month, 1, 0, 0, 0, picked.Offset);
            }
            catch { }
        }
    }
}
