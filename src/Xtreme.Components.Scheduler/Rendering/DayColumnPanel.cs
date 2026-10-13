using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// Arranges timed event blocks within a single day column. Each child carries attached
/// time/column metadata (set from <see cref="EventBlockLayout"/>); this panel turns that
/// into pixel rects in <see cref="ArrangeOverride"/>, so positioning re-flows automatically
/// with the column's width — no SizeChanged plumbing, and identical under Skia and Native.
/// </summary>
public sealed partial class DayColumnPanel : Panel
{
    private const double ColumnGap = 2;
    /// <summary>Smallest pixel height a timed block is drawn at — one line of text plus padding (XU-837: the
    /// old 14px floor drew short events as unreadable slivers). <see cref="EventBlockLayout"/> reserves the same
    /// space when packing overlaps, so a stretched block never sits on top of the next event.</summary>
    public const double MinBlockHeight = 22;

    /// <summary>Vertical scale; defaults to <see cref="TimeIncrementConfig.Default"/>.</summary>
    public TimeIncrementConfig Increment { get; set; } = TimeIncrementConfig.Default;

    #region Attached metadata

    public static readonly DependencyProperty StartMinutesProperty =
        DependencyProperty.RegisterAttached("StartMinutes", typeof(double), typeof(DayColumnPanel),
            new PropertyMetadata(0d, OnLayoutChanged));

    public static double GetStartMinutes(DependencyObject o) => (double)o.GetValue(StartMinutesProperty);
    public static void SetStartMinutes(DependencyObject o, double v) => o.SetValue(StartMinutesProperty, v);

    public static readonly DependencyProperty DurationMinutesProperty =
        DependencyProperty.RegisterAttached("DurationMinutes", typeof(double), typeof(DayColumnPanel),
            new PropertyMetadata(30d, OnLayoutChanged));

    public static double GetDurationMinutes(DependencyObject o) => (double)o.GetValue(DurationMinutesProperty);
    public static void SetDurationMinutes(DependencyObject o, double v) => o.SetValue(DurationMinutesProperty, v);

    public static readonly DependencyProperty ColumnIndexProperty =
        DependencyProperty.RegisterAttached("ColumnIndex", typeof(int), typeof(DayColumnPanel),
            new PropertyMetadata(0, OnLayoutChanged));

    public static int GetColumnIndex(DependencyObject o) => (int)o.GetValue(ColumnIndexProperty);
    public static void SetColumnIndex(DependencyObject o, int v) => o.SetValue(ColumnIndexProperty, v);

    public static readonly DependencyProperty ColumnCountProperty =
        DependencyProperty.RegisterAttached("ColumnCount", typeof(int), typeof(DayColumnPanel),
            new PropertyMetadata(1, OnLayoutChanged));

    public static int GetColumnCount(DependencyObject o) => (int)o.GetValue(ColumnCountProperty);
    public static void SetColumnCount(DependencyObject o, int v) => o.SetValue(ColumnCountProperty, v);

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (VisualTreeHelper.GetParent(d) is DayColumnPanel panel)
        {
            panel.InvalidateArrange();
        }
    }

    #endregion

    protected override Size MeasureOverride(Size availableSize)
    {
        var pxPerMin = Increment.PixelsPerMinute;
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;

        foreach (var child in Children)
        {
            var colCount = Math.Max(1, GetColumnCount(child));
            var colW = Math.Max(0, width / colCount - 2 * ColumnGap);
            var h = Math.Max(MinBlockHeight, GetDurationMinutes(child) * pxPerMin);
            child.Measure(new Size(colW, h));
        }

        return new Size(width, Increment.DayHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var pxPerMin = Increment.PixelsPerMinute;

        foreach (var child in Children)
        {
            var colCount = Math.Max(1, GetColumnCount(child));
            var colIndex = Math.Clamp(GetColumnIndex(child), 0, colCount - 1);
            var colW = finalSize.Width / colCount;

            var x = colIndex * colW + ColumnGap;
            var w = Math.Max(0, colW - 2 * ColumnGap);
            var y = GetStartMinutes(child) * pxPerMin;
            var h = Math.Max(MinBlockHeight, GetDurationMinutes(child) * pxPerMin);

            child.Arrange(new Rect(x, y, w, h));
        }

        return new Size(finalSize.Width, Increment.DayHeight);
    }
}
