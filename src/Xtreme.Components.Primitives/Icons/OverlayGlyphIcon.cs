
namespace Xtreme.Components.Primitives;

public sealed class OverlayGlyphIcon : Control
{
    // Badge glyph size relative to the badge itself (the template's original 0.4 / 0.5).
    private const double FrontGlyphToBadge = 0.8;

    private FrameworkElement? _badge;
    private FontIcon? _frontGlyph;

    public OverlayGlyphIcon()
    {
        DefaultStyleKey = typeof(OverlayGlyphIcon);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _badge = GetTemplateChild("PART_Badge") as FrameworkElement;
        _frontGlyph = GetTemplateChild("PART_FrontGlyph") as FontIcon;
        ApplyBadgeSize();
    }

    private static void OnBadgeSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((OverlayGlyphIcon)d).ApplyBadgeSize();

    // The badge is FrontSizeRatio of Size; its glyph fills 80% of the badge. Sized in code rather than
    // by template bindings so the ratio is one property instead of a converter parameter per binding.
    private void ApplyBadgeSize()
    {
        var ratio = double.IsNaN(FrontSizeRatio) ? 0.5 : Math.Clamp(FrontSizeRatio, 0, 1);
        var badge = Math.Max(0, Size * ratio);
        if (_badge is not null)
        {
            _badge.Width = badge;
            _badge.Height = badge;
        }
        if (_frontGlyph is not null && badge > 0)
            _frontGlyph.FontSize = badge * FrontGlyphToBadge;
    }

    #region Dependency Properties

    public string BackGlyph
    {
        get => (string)GetValue(BackGlyphProperty);
        set => SetValue(BackGlyphProperty, value);
    }

    public static readonly DependencyProperty BackGlyphProperty =
        DependencyProperty.Register(
            nameof(BackGlyph),
            typeof(string),
            typeof(OverlayGlyphIcon),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Optional custom vector back icon (a key the app resolves through <c>ComponentHost.ResolveGeometry</c>, e.g. "price_tag"),
    /// used instead of <see cref="BackGlyph"/> when there's no good Fluent glyph. The template
    /// renders whichever of the two is set; the resolved geometry is exposed via
    /// <see cref="BackData"/>, themed by <see cref="BackForeground"/> so it follows Light/Dark.
    /// </summary>
    public string BackGeometryKey
    {
        get => (string)GetValue(BackGeometryKeyProperty);
        set => SetValue(BackGeometryKeyProperty, value);
    }

    public static readonly DependencyProperty BackGeometryKeyProperty =
        DependencyProperty.Register(
            nameof(BackGeometryKey),
            typeof(string),
            typeof(OverlayGlyphIcon),
            new PropertyMetadata(string.Empty, OnBackGeometryKeyChanged));

    /// <summary>Resolved back geometry (read-only, driven by <see cref="BackGeometryKey"/>).</summary>
    public Geometry? BackData
    {
        get => (Geometry?)GetValue(BackDataProperty);
        private set => SetValue(BackDataProperty, value);
    }

    public static readonly DependencyProperty BackDataProperty =
        DependencyProperty.Register(
            nameof(BackData),
            typeof(Geometry),
            typeof(OverlayGlyphIcon),
            new PropertyMetadata(null));

    private static void OnBackGeometryKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (OverlayGlyphIcon)d;
        var key = e.NewValue as string;
        icon.BackData = !string.IsNullOrEmpty(key) && Xtreme.Components.Core.ComponentHost.ResolveGeometry?.Invoke(key) is { } geo ? geo : null;
    }

    public string FrontGlyph
    {
        get => (string)GetValue(FrontGlyphProperty);
        set => SetValue(FrontGlyphProperty, value);
    }

    public static readonly DependencyProperty FrontGlyphProperty =
        DependencyProperty.Register(
            nameof(FrontGlyph),
            typeof(string),
            typeof(OverlayGlyphIcon),
            new PropertyMetadata("\uE710")); // Plus

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(
            nameof(Size),
            typeof(double),
            typeof(OverlayGlyphIcon),
            new PropertyMetadata(24d, OnBadgeSizeChanged));

    /// <summary>
    /// Size of the round badge as a fraction of <see cref="Size"/> (clamped to 0..1). Default 0.5.
    /// The badge glyph is 80% of the badge.
    /// </summary>
    public double FrontSizeRatio
    {
        get => (double)GetValue(FrontSizeRatioProperty);
        set => SetValue(FrontSizeRatioProperty, value);
    }

    public static readonly DependencyProperty FrontSizeRatioProperty =
        DependencyProperty.Register(
            nameof(FrontSizeRatio),
            typeof(double),
            typeof(OverlayGlyphIcon),
            new PropertyMetadata(0.5d, OnBadgeSizeChanged));

    public Brush BackForeground
    {
        get => (Brush)GetValue(BackForegroundProperty);
        set => SetValue(BackForegroundProperty, value);
    }

    public static readonly DependencyProperty BackForegroundProperty =
        DependencyProperty.Register(
            nameof(BackForeground),
            typeof(Brush),
            typeof(OverlayGlyphIcon),
            new PropertyMetadata(null));

    public Brush FrontForeground
    {
        get => (Brush)GetValue(FrontForegroundProperty);
        set => SetValue(FrontForegroundProperty, value);
    }

    public static readonly DependencyProperty FrontForegroundProperty =
        DependencyProperty.Register(
            nameof(FrontForeground),
            typeof(Brush),
            typeof(OverlayGlyphIcon),
            new PropertyMetadata(null));

    public Brush FrontBackground
    {
        get => (Brush)GetValue(FrontBackgroundProperty);
        set => SetValue(FrontBackgroundProperty, value);
    }

    public static readonly DependencyProperty FrontBackgroundProperty =
        DependencyProperty.Register(
            nameof(FrontBackground),
            typeof(Brush),
            typeof(OverlayGlyphIcon),
            new PropertyMetadata(null));

    #endregion
}
