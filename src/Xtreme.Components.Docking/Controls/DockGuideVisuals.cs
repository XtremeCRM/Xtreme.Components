using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Xtreme.Components.Docking.Controls;

/// <summary>What a docking-guide button does, which decides the picture on it.</summary>
internal enum GuideGlyph
{
    /// <summary>Dock against the host's outer border, outside every other pane.</summary>
    EdgeOuter,

    /// <summary>Dock against the document, inside the panes already on that edge.</summary>
    EdgeInner,

    /// <summary>Split the hovered pane: land beside it, taking that half.</summary>
    Split,

    /// <summary>Join the hovered pane as a tab.</summary>
    Tab,

    /// <summary>The document itself — the compass's non-interactive center.</summary>
    Document,
}

/// <summary>
/// Colors for the docking guides. Token-first, like the scheduler: every role checks the app's
/// resources for <c>XcDockGuide&lt;Role&gt;Brush</c> and falls back to the built-in Light or Dark value.
/// </summary>
internal sealed record DockGuidePalette
{
    /// <summary>The raised plate the guide buttons sit on.</summary>
    public required Brush Plate { get; init; }

    public required Brush PlateOutline { get; init; }

    /// <summary>The soft drop shadow under the plate.</summary>
    public required Brush PlateShadow { get; init; }

    /// <summary>The window outlines drawn on each button.</summary>
    public required Brush Glyph { get; init; }

    /// <summary>The part of each picture showing where the pane will land; also the hover outline.</summary>
    public required Brush Landing { get; init; }

    /// <summary>A hovered button's fill.</summary>
    public required Brush Hover { get; init; }

    /// <summary>
    /// The wash over the region a release would fill. A LIGHT tint rather than the accent itself, so it
    /// still reads over a pane that is already blue; the solid outline carries the exact boundary.
    /// </summary>
    public required Brush Preview { get; init; }

    public required Brush PreviewOutline { get; init; }

    public static readonly Brush Transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public static DockGuidePalette For(ElementTheme theme)
    {
        if (theme == ElementTheme.Dark)
            return _dark ??= Resolve(Dark, ElementTheme.Dark);
        return _light ??= Resolve(Light, ElementTheme.Light);
    }

    private static DockGuidePalette? _light;
    private static DockGuidePalette? _dark;

    static DockGuidePalette()
    {
        Xtreme.Components.Core.ComponentHost.ResourcesChanged += (_, _) => { _light = null; _dark = null; };
    }

    private static DockGuidePalette Resolve(DockGuidePalette fallback, ElementTheme theme)
    {
        Brush Tok(string role, Brush dflt) =>
            Xtreme.Components.Core.ComponentHost.TryGetBrush("XcDockGuide" + role + "Brush", theme) ?? dflt;

        return fallback with
        {
            Plate = Tok(nameof(Plate), fallback.Plate),
            PlateOutline = Tok(nameof(PlateOutline), fallback.PlateOutline),
            PlateShadow = Tok(nameof(PlateShadow), fallback.PlateShadow),
            Glyph = Tok(nameof(Glyph), fallback.Glyph),
            Landing = Tok(nameof(Landing), fallback.Landing),
            Hover = Tok(nameof(Hover), fallback.Hover),
            Preview = Tok(nameof(Preview), fallback.Preview),
            PreviewOutline = Tok(nameof(PreviewOutline), fallback.PreviewOutline),
        };
    }

    private static SolidColorBrush B(byte a, byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(a, r, g, b));

    private static readonly DockGuidePalette Light = new()
    {
        Plate = B(0xF2, 0xF7, 0xF7, 0xF7),
        PlateOutline = B(0x66, 0x00, 0x00, 0x00),
        PlateShadow = B(0x30, 0x00, 0x00, 0x00),
        Glyph = B(0xFF, 0x5C, 0x5C, 0x5C),
        Landing = B(0xFF, 0x2F, 0x6F, 0xB5),
        Hover = B(0x38, 0x2F, 0x6F, 0xB5),
        Preview = B(0x8C, 0xA8, 0xCE, 0xF5),
        PreviewOutline = B(0xFF, 0x2F, 0x6F, 0xB5),
    };

    private static readonly DockGuidePalette Dark = new()
    {
        Plate = B(0xF2, 0x2D, 0x2D, 0x2D),
        PlateOutline = B(0x70, 0xFF, 0xFF, 0xFF),
        PlateShadow = B(0x60, 0x00, 0x00, 0x00),
        Glyph = B(0xFF, 0xC8, 0xC8, 0xC8),
        Landing = B(0xFF, 0x4A, 0x90, 0xE2),
        Hover = B(0x48, 0x4A, 0x90, 0xE2),
        Preview = B(0x80, 0x7F, 0xB2, 0xEE),
        PreviewOutline = B(0xFF, 0xA8, 0xCE, 0xF5),
    };
}

/// <summary>
/// Draws the docking guides: small window pictures on a raised plate. Every button shows a miniature
/// window — a title strip and a frame — with the part the pane would occupy filled in, so a button
/// says what it does instead of pointing an arrow at it. Buttons of one compass sit edge to edge on a
/// single cross-shaped plate.
/// </summary>
/// <remarks>
/// Drawn from plain Borders and one Path rather than an icon font or image assets: nothing to ship or
/// load, every color is a theme token, and it renders the same on every Uno target.
/// </remarks>
internal static class DockGuideVisuals
{
    /// <summary>One button's size; buttons in a compass share edges, so this is also their spacing.</summary>
    public const double Cell = 30;

    /// <summary>Plate margin around the buttons.</summary>
    public const double PlatePadding = 4;

    private const double GlyphSize = 22;

    /// <summary>
    /// A guide button: transparent until hovered, holding the picture for <paramref name="glyph"/>.
    /// Positioned in overlay coordinates through its RenderTransform, top-left at (<paramref name="x"/>,
    /// <paramref name="y"/>).
    /// </summary>
    public static Border Button(GuideGlyph glyph, DockEdge direction, DockGuidePalette palette, double x, double y)
        => new()
        {
            Width = Cell,
            Height = Cell,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = DockGuidePalette.Transparent,
            Background = DockGuidePalette.Transparent,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TranslateTransform { X = x, Y = y },
            Child = Picture(glyph, direction, palette),
        };

    /// <summary>Shows or clears a button's hover state.</summary>
    public static void SetHover(Border button, bool hover, DockGuidePalette palette)
    {
        button.Background = hover ? palette.Hover : DockGuidePalette.Transparent;
        button.BorderBrush = hover ? palette.Landing : DockGuidePalette.Transparent;
    }

    /// <summary>
    /// The plate for a compass whose center cell is centered on (<paramref name="cx"/>,
    /// <paramref name="cy"/>), with arms of the given lengths in cells (0 = no arm that way). Returns the
    /// shadow and the plate, back to front, plus the plate's bounds in overlay coordinates.
    /// </summary>
    public static (UIElement[] Layers, Windows.Foundation.Rect Bounds) Plate(
        double cx, double cy, int up, int right, int down, int left, DockGuidePalette palette)
    {
        var a = Cell / 2 + PlatePadding; // half the arm width
        var minX = -a - left * Cell;
        var minY = -a - up * Cell;
        var maxX = a + right * Cell;
        var maxY = a + down * Cell;

        // The cross outline, clockwise from the top arm's left corner, in coordinates relative to the
        // shape's top-left so nothing is drawn at negative offsets. An arm of length 0 just repeats
        // corner points, which draws the plain square.
        Windows.Foundation.Point P(double x, double y) => new(x - minX, y - minY);
        var points = new[]
        {
            P(-a, minY), P(a, minY), P(a, -a), P(maxX, -a), P(maxX, a), P(a, a),
            P(a, maxY), P(-a, maxY), P(-a, a), P(minX, a), P(minX, -a), P(-a, -a),
        };

        Path Shape(Brush fill, Brush? stroke, double dx, double dy)
        {
            var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            foreach (var point in points.Skip(1))
                figure.Segments.Add(new LineSegment { Point = point });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return new Path
            {
                Data = geometry,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = stroke is null ? 0 : 1,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = new TranslateTransform { X = cx + minX + dx, Y = cy + minY + dy },
            };
        }

        var bounds = new Windows.Foundation.Rect(cx + minX, cy + minY, maxX - minX, maxY - minY);
        return (new UIElement[] { Shape(palette.PlateShadow, null, 1.5, 2), Shape(palette.Plate, palette.PlateOutline, 0, 0) }, bounds);
    }

    /// <summary>
    /// The window picture. The title strip always stays on top; what fills the body depends on the
    /// glyph and points toward <paramref name="direction"/>.
    /// </summary>
    private static Canvas Picture(GuideGlyph glyph, DockEdge direction, DockGuidePalette palette)
    {
        var canvas = new Canvas
        {
            Width = GlyphSize,
            Height = GlyphSize,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        void Add(UIElement element, double x, double y)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            canvas.Children.Add(element);
        }

        Border Fill(double w, double h, Brush brush) => new() { Width = w, Height = h, Background = brush };
        Border Outline(double w, double h) => new()
        {
            Width = w,
            Height = h,
            BorderBrush = palette.Glyph,
            BorderThickness = new Thickness(1),
            Opacity = 0.75,
        };

        // The window: frame and title strip.
        Add(new Border
        {
            Width = 20,
            Height = 20,
            BorderBrush = palette.Glyph,
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(1.5),
        }, 1, 1);
        Add(Fill(18, 3, palette.Glyph), 2, 2);

        // The body under the title strip.
        const double bodyX = 3, bodyY = 6.5, bodyW = 16, bodyH = 12.5;

        // A slice of the body measured from the side the button points at: [from, to) as fractions of
        // the body's length along that axis. Left/Top count from their own side, Right/Bottom from theirs.
        void Slice(double from, double to, bool landing)
        {
            var horizontal = direction is DockEdge.Left or DockEdge.Right;
            var length = horizontal ? bodyW : bodyH;
            var start = from * length;
            var size = Math.Max((to - from) * length, 1);

            double x = bodyX, y = bodyY, w = bodyW, h = bodyH;
            switch (direction)
            {
                case DockEdge.Left: x = bodyX + start; w = size; break;
                case DockEdge.Right: x = bodyX + bodyW - start - size; w = size; break;
                case DockEdge.Top: y = bodyY + start; h = size; break;
                default: y = bodyY + bodyH - start - size; h = size; break;
            }

            Add(landing ? Fill(w, h, palette.Landing) : Outline(w, h), x, y);
        }

        switch (glyph)
        {
            case GuideGlyph.EdgeOuter:
                // Against the window frame, with the document beside it.
                Slice(0, 0.34, landing: true);
                Slice(0.46, 1, landing: false);
                break;

            case GuideGlyph.EdgeInner:
                // An existing pane against the frame, the landing inside it, then the document.
                Slice(0, 0.2, landing: false);
                Slice(0.28, 0.58, landing: true);
                Slice(0.68, 1, landing: false);
                break;

            case GuideGlyph.Split:
                Slice(0, 0.5, landing: true);
                break;

            case GuideGlyph.Tab:
                // The whole body becomes the pane, and a tab rides the title strip.
                Add(Fill(bodyW, bodyH, palette.Landing), bodyX, bodyY);
                Add(Fill(7, 3, palette.Landing), 2, 2);
                break;

            case GuideGlyph.Document:
                Add(Outline(bodyW - 4, bodyH - 3), bodyX + 2, bodyY + 1.5);
                canvas.Opacity = 0.7;
                break;
        }

        return canvas;
    }
}
