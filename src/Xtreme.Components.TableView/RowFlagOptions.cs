namespace Xtreme.Components.TableView;

/// <summary>
/// How <see cref="RowFlagDecorator"/> resolves flag brushes and draws the frame and tag. Every
/// property defaults to the decorator's built-in look, so <c>new RowFlagOptions()</c> is the same as
/// attaching without options.
/// </summary>
/// <remarks>
/// The decorator keeps a reference to the instance and reads it on every paint pass, so a property
/// changed after <see cref="RowFlagDecorator.Attach(WinUI.TableView.TableView, Func{object?, RowFlag?}, RowFlagOptions)"/>
/// shows on rows painted afterwards; call <see cref="RowFlagDecorator.Refresh"/> to repaint the
/// realized rows at once.
/// </remarks>
public sealed class RowFlagOptions
{
    /// <summary>
    /// Turns a <see cref="RowFlag.BrushKey"/> into a brush. Null (the default) uses
    /// <see cref="RowFlagDecorator.DefaultResolveBrush"/>.
    /// </summary>
    public RowFlagDecorator.BrushResolver? ResolveBrush { get; set; }

    /// <summary>Width of the frame drawn around a flagged row. Default 2.</summary>
    public double FrameThickness { get; set; } = 2;

    /// <summary>Corner radius of the frame. Default 0 (square).</summary>
    public CornerRadius FrameCornerRadius { get; set; }

    /// <summary>Font size of the tag text. Default 8.</summary>
    public double TagFontSize { get; set; } = 8;

    /// <summary>
    /// Brush for the tag text. Null (the default) is white; choose flag brushes dark enough for it in
    /// both themes, or set a foreground that reads on them.
    /// </summary>
    public Brush? TagForeground { get; set; }

    /// <summary>Padding inside the tag. Default <c>6,0,6,1</c>.</summary>
    public Thickness TagPadding { get; set; } = new(6, 0, 6, 1);

    /// <summary>Corner radius of the tag. Default <c>0,4,0,0</c> (rounded top-right only).</summary>
    public CornerRadius TagCornerRadius { get; set; } = new(0, 4, 0, 0);
}
