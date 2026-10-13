using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Xtreme.Components.Primitives;

/// <summary>
/// Lays children out in equal-width columns, each child going into whichever column is currently
/// shortest — a masonry flow.
/// </summary>
/// <remarks>
/// <para>
/// Written for the Advanced Search criteria panels. A <see cref="WrapPanel"/> wraps by ROWS, so every
/// item on a row reserves the height of the tallest item on it: a seven-field Order panel beside a
/// two-field Amounts panel left a block of dead space under the short one, and the panels that could
/// have filled it were pushed onto the next row instead. Packing by column removes that gap, because
/// a short panel is followed by the next panel rather than by whitespace.
/// </para>
/// <para>
/// Column COUNT comes from <see cref="MinColumnWidth"/> and the available width, so the layout still
/// adapts to a resize with no breakpoint to maintain; column WIDTH is then uniform, which keeps the
/// panels aligned in a grid rather than ragged.
/// </para>
/// <para>
/// One consequence worth knowing: children no longer read strictly left-to-right, since a tall child
/// makes its column unattractive for a while. That is the point of the layout, but it means the
/// markup order is a rough guide to position, not a guarantee.
/// </para>
/// </remarks>
public partial class ColumnFlowPanel : Panel
{
    /// <summary>Narrowest a column may be before the panel drops to fewer columns.</summary>
    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public static readonly DependencyProperty MinColumnWidthProperty =
        DependencyProperty.Register(nameof(MinColumnWidth), typeof(double), typeof(ColumnFlowPanel),
            new PropertyMetadata(300d, OnLayoutPropertyChanged));

    /// <summary>Most columns to use, however wide the panel gets.</summary>
    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public static readonly DependencyProperty MaxColumnsProperty =
        DependencyProperty.Register(nameof(MaxColumns), typeof(int), typeof(ColumnFlowPanel),
            new PropertyMetadata(5, OnLayoutPropertyChanged));

    /// <summary>Horizontal gap between columns.</summary>
    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(nameof(ColumnSpacing), typeof(double), typeof(ColumnFlowPanel),
            new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>Vertical gap between stacked children in a column.</summary>
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(nameof(RowSpacing), typeof(double), typeof(ColumnFlowPanel),
            new PropertyMetadata(0d, OnLayoutPropertyChanged));

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ColumnFlowPanel)d).InvalidateMeasure();

    /// <summary>
    /// Column index per child, decided during measure and reused during arrange. Arrange must not
    /// recompute the assignment: it runs against final sizes and could pick a different column,
    /// which would place children where they were never measured.
    /// </summary>
    private int[] _columnOf = [];
    private double _columnWidth;
    private int _columns = 1;

    private int ResolveColumns(double availableWidth)
    {
        if (double.IsInfinity(availableWidth) || availableWidth <= 0)
            return 1;

        var fit = (int)((availableWidth + ColumnSpacing) / (MinColumnWidth + ColumnSpacing));
        return Math.Clamp(fit, 1, Math.Max(1, MaxColumns));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _columns = ResolveColumns(availableSize.Width);
        _columnWidth = double.IsInfinity(availableSize.Width)
            ? MinColumnWidth
            : Math.Max(0, (availableSize.Width - ColumnSpacing * (_columns - 1)) / _columns);

        var heights = new double[_columns];
        _columnOf = new int[Children.Count];

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(_columnWidth, double.PositiveInfinity));

            // Shortest column wins; ties go left, so a fresh row fills in reading order.
            int target = 0;
            for (int c = 1; c < _columns; c++)
            {
                if (heights[c] < heights[target] - 0.5)
                    target = c;
            }

            _columnOf[i] = target;
            heights[target] += child.DesiredSize.Height + (heights[target] > 0 ? RowSpacing : 0);
        }

        double tallest = 0;
        foreach (var h in heights)
            tallest = Math.Max(tallest, h);

        return new Size(
            double.IsInfinity(availableSize.Width) ? _columnWidth * _columns + ColumnSpacing * (_columns - 1) : availableSize.Width,
            tallest);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var offsets = new double[_columns];

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            int c = i < _columnOf.Length ? _columnOf[i] : 0;
            if (c >= _columns) c = 0;

            if (offsets[c] > 0) offsets[c] += RowSpacing;

            var x = c * (_columnWidth + ColumnSpacing);
            child.Arrange(new Rect(x, offsets[c], _columnWidth, child.DesiredSize.Height));
            offsets[c] += child.DesiredSize.Height;
        }

        double tallest = 0;
        foreach (var o in offsets)
            tallest = Math.Max(tallest, o);

        return new Size(finalSize.Width, tallest);
    }
}
