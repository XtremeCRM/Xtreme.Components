using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Xtreme.Components.Primitives;

/// <summary>
/// Lays children out left-to-right, wrapping to a new line when the next child would overflow.
/// </summary>
/// <remarks>
/// XU-183: written rather than referenced. CommunityToolkit ships a WrapPanel, but pulling in
/// CommunityToolkit.WinUI.Controls.Primitives for one panel widens the package graph, and a
/// Toolkit graph shift is exactly what silently broke remote image loading once before (see
/// AvatarImageHelper). Forty lines beats that risk.
///
/// Preferred over an AdaptiveTrigger for responsive layout because there is no breakpoint to
/// choose or maintain: children carry their own MinWidth and the panel wraps whenever they no
/// longer fit, so the same markup adapts to a resized window, a split view, and a phone.
/// </remarks>
public partial class WrapPanel : Panel
{
    /// <summary>Horizontal gap between children on the same line.</summary>
    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }
    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(nameof(ColumnSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>Vertical gap between lines.</summary>
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }
    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(nameof(RowSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0d, OnLayoutPropertyChanged));

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WrapPanel)d).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        // Children measure against the full line width, never the remaining space — a child must
        // report the size it *wants* so we can decide whether it fits, not shrink to what is left.
        var childConstraint = new Size(availableSize.Width, double.PositiveInfinity);

        double lineWidth = 0, lineHeight = 0, totalWidth = 0, totalHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(childConstraint);
            var desired = child.DesiredSize;

            var widthWithSpacing = lineWidth == 0 ? desired.Width : lineWidth + ColumnSpacing + desired.Width;
            if (lineWidth > 0 && widthWithSpacing > availableSize.Width)
            {
                // Wrap: bank the finished line and start a new one with this child.
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + RowSpacing;
                lineWidth = desired.Width;
                lineHeight = desired.Height;
            }
            else
            {
                lineWidth = widthWithSpacing;
                lineHeight = Math.Max(lineHeight, desired.Height);
            }
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;

        // Infinite available width (inside a horizontal StackPanel/ScrollViewer) makes the
        // measured width meaningless as a constraint; fall back to the natural single-line width.
        var width = double.IsInfinity(availableSize.Width) ? totalWidth : Math.Min(totalWidth, availableSize.Width);
        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;

        foreach (var child in Children)
        {
            var desired = child.DesiredSize;
            var xWithSpacing = x == 0 ? 0 : x + ColumnSpacing;

            if (x > 0 && xWithSpacing + desired.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight + RowSpacing;
                lineHeight = 0;
                xWithSpacing = 0;
            }

            child.Arrange(new Rect(xWithSpacing, y, desired.Width, desired.Height));
            x = xWithSpacing + desired.Width;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        return finalSize;
    }
}
