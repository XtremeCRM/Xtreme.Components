namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// Arranges event bars horizontally within one resource lane of the continuous timeline:
/// X = minutes-from-range-start × <see cref="PixelsPerMinute"/>, width = duration × scale.
/// Overlapping events stack as sub-rows (Y = sub-row × <see cref="RowHeight"/>), so the lane's
/// height is <see cref="RowCount"/> × <see cref="RowHeight"/>. The horizontal scale is fixed
/// (the timeline is a fixed-width, horizontally-scrolling surface), so no width reactivity is
/// needed — positioning is identical under Skia and Native.
/// </summary>
internal sealed partial class TimelineLanePanel : Panel
{
    private const double Gap = 2;

    /// <summary>Horizontal pixels per minute (fixed across the timeline).</summary>
    public double PixelsPerMinute { get; set; } = 0.6;

    /// <summary>Height of one overlap sub-row.</summary>
    public double RowHeight { get; set; } = 30;

    /// <summary>Number of overlap sub-rows in this lane (its height divisor).</summary>
    public int RowCount { get; set; } = 1;

    public static readonly DependencyProperty StartMinutesProperty =
        DependencyProperty.RegisterAttached("StartMinutes", typeof(double), typeof(TimelineLanePanel),
            new PropertyMetadata(0d, OnLayoutChanged));

    public static double GetStartMinutes(DependencyObject o) => (double)o.GetValue(StartMinutesProperty);
    public static void SetStartMinutes(DependencyObject o, double v) => o.SetValue(StartMinutesProperty, v);

    public static readonly DependencyProperty DurationMinutesProperty =
        DependencyProperty.RegisterAttached("DurationMinutes", typeof(double), typeof(TimelineLanePanel),
            new PropertyMetadata(30d, OnLayoutChanged));

    public static double GetDurationMinutes(DependencyObject o) => (double)o.GetValue(DurationMinutesProperty);
    public static void SetDurationMinutes(DependencyObject o, double v) => o.SetValue(DurationMinutesProperty, v);

    public static readonly DependencyProperty SubRowProperty =
        DependencyProperty.RegisterAttached("SubRow", typeof(int), typeof(TimelineLanePanel),
            new PropertyMetadata(0, OnLayoutChanged));

    public static int GetSubRow(DependencyObject o) => (int)o.GetValue(SubRowProperty);
    public static void SetSubRow(DependencyObject o, int v) => o.SetValue(SubRowProperty, v);

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (VisualTreeHelper.GetParent(d) is TimelineLanePanel panel)
        {
            panel.InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = Math.Max(1, RowCount) * RowHeight;
        foreach (var child in Children)
        {
            var w = Math.Max(0, GetDurationMinutes(child) * PixelsPerMinute);
            child.Measure(new Size(w, RowHeight));
        }
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var x = GetStartMinutes(child) * PixelsPerMinute;
            var w = Math.Max(0, GetDurationMinutes(child) * PixelsPerMinute - Gap);
            var y = GetSubRow(child) * RowHeight;
            var h = Math.Max(0, RowHeight - Gap);
            child.Arrange(new Rect(x, y, w, h));
        }
        return new Size(finalSize.Width, Math.Max(1, RowCount) * RowHeight);
    }
}
