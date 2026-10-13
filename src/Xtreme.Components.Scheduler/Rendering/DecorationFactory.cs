using System.IO;
using Windows.Foundation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// Renders an appointment's positioned <em>adornments</em> (<see cref="AppointmentModel.Adornments"/>) — the
/// host's user/template badge feature — as an overlay pinned over the block. Each shows an image, a glyph, or
/// a text pill at its <see cref="DecorationPosition"/>/<see cref="DecorationAlignment"/> plus pixel offset.
/// The overlay is non-hit-testable so it never interferes with the view's drag/selection overlay.
/// (Data-driven <em>tiles</em> are rendered separately by <see cref="EventBlockFactory"/> in the block's
/// right gutter column — opposite the left status-icon strip.)
/// </summary>
internal static class DecorationFactory
{
    private const double BaseInset = 2;

    /// <summary>
    /// The adornment overlay a view can LIFT off an adorned block (produced by <see cref="Apply"/>) to
    /// re-host it in a shared top layer that paints above neighbouring appointments/columns. Views that
    /// don't lift it leave it nested (and thus clipped by the column). The time grid lifts it; Month/
    /// Planner/Agenda keep it nested.
    /// </summary>
    public static readonly DependencyProperty OverlayProperty =
        DependencyProperty.RegisterAttached("Overlay", typeof(UIElement), typeof(DecorationFactory), new PropertyMetadata(null));

    public static UIElement? GetOverlay(DependencyObject o) => (UIElement?)o.GetValue(OverlayProperty);
    public static void SetOverlay(DependencyObject o, UIElement? value) => o.SetValue(OverlayProperty, value);

    /// <summary>
    /// If <paramref name="ctx"/> carries any adornments, wrap the block's child so they render on top of it.
    /// No-op when there are none (the common case), so plain appointments keep the same visual tree.
    /// </summary>
    public static FrameworkElement Apply(Border block, AppointmentContext ctx)
    {
        if (ctx.Adornments.Count == 0)
        {
            return block;
        }

        var overlay = new Grid { IsHitTestVisible = false };
        foreach (var d in ctx.Adornments)
        {
            if (Build(d, ctx) is { } el) overlay.Children.Add(el);
        }

        if (overlay.Children.Count == 0)
        {
            return block;
        }

        // The block Border clips its child to its rounded bounds (CornerRadius), so an adornment nested
        // inside it gets cut at the edge. Put the overlay as a SIBLING of the block in an unclipped outer
        // Grid (sized to the block) so a badge can extend PAST the appointment border instead of being
        // clipped. The block keeps clipping its own content; only the adornment layer is free to overhang.
        // Carry the block's identity onto the wrapper so the view's selection (which matches blocks by Tag)
        // still recognises an adorned block — otherwise its red selection outline never appears.
        var root = new Grid { Tag = block.Tag, DataContext = block.DataContext };
        root.Children.Add(block);
        root.Children.Add(overlay);
        // Expose the overlay so a host view can lift it into a shared, always-on-top layer (see remarks on
        // OverlayProperty). Left nested by default; the lifter reparents it and clears this.
        SetOverlay(root, overlay);
        return root;
    }

    private static FrameworkElement? Build(AppointmentDecoration d, AppointmentContext ctx)
    {
        var (h, v) = Anchor(d.Position, d.Alignment);
        FrameworkElement? content =
            d.Image is { Length: > 0 } bytes ? BuildImage(bytes)
            : !string.IsNullOrEmpty(d.Glyph) ? BuildGlyph(d, ctx, h, v)
            : !string.IsNullOrWhiteSpace(d.Text) ? BuildText(d, ctx, h, v)
            : null;

        if (content is null)
        {
            return null;
        }

        Position(content, d, h, v);
        if (!string.IsNullOrEmpty(d.Tooltip)) ToolTipService.SetToolTip(content, d.Tooltip);
        return content;
    }

    private static FrameworkElement BuildImage(byte[] bytes)
    {
        var bmp = new BitmapImage();
        _ = LoadImageAsync(bmp, bytes);
        return new Image
        {
            Source = bmp,
            Stretch = Stretch.None, // actual image size (Legacy parity) — admins size the template image
        };
    }

    private static async Task LoadImageAsync(BitmapImage bmp, byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
        }
        catch
        {
            // A malformed/unsupported image just renders nothing — never crash the block.
        }
    }

    private static FrameworkElement BuildGlyph(AppointmentDecoration d, AppointmentContext ctx, HorizontalAlignment h, VerticalAlignment v)
    {
        var icon = new FontIcon
        {
            Glyph = d.Glyph ?? string.Empty,
            FontSize = d.FontSize,
            Foreground = d.ForeColor is Color fc ? new SolidColorBrush(fc) : ctx.Foreground,
        };
        if (d.GlyphFontFamily is not null) icon.FontFamily = d.GlyphFontFamily;
        return Chip(icon, d, h, v);
    }

    private static FrameworkElement BuildText(AppointmentDecoration d, AppointmentContext ctx, HorizontalAlignment h, VerticalAlignment v)
    {
        var text = new TextBlock
        {
            Text = d.Text,
            FontSize = d.FontSize,
            Foreground = d.ForeColor is Color fc ? new SolidColorBrush(fc) : ctx.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        return Chip(text, d, h, v);
    }

    // A brush that holds full opacity over the pill's INNER side (the part above its OWNING appointment) and
    // fades to ~30% across the OUTER side — the part that overhangs the neighbouring appointment, since the
    // pill is centred on its anchor point. A corner anchor (e.g. FloatRight+Far, or a top/bottom-edge pill)
    // fades DIAGONALLY toward that corner; a pure edge fades straight out; a centred decoration doesn't
    // overhang so it stays solid. (Uno Skia has no OpacityMask; LinearGradientBrush IS supported, so we tint
    // the fill/border — the text keeps a solid brush so it's always legible.)
    private static Brush FadeBrush(Color color, HorizontalAlignment h, VerticalAlignment v)
    {
        if (h == HorizontalAlignment.Center && v == VerticalAlignment.Center)
        {
            return new SolidColorBrush(color); // no overhang (e.g. FloatCenter) — solid
        }

        // Outer point = the overhanging edge/corner (faded); inner = toward the appointment centre (opaque).
        double outerX = h == HorizontalAlignment.Left ? 0 : h == HorizontalAlignment.Right ? 1 : 0.5;
        double outerY = v == VerticalAlignment.Top ? 0 : v == VerticalAlignment.Bottom ? 1 : 0.5;
        double innerX = h == HorizontalAlignment.Left ? 1 : h == HorizontalAlignment.Right ? 0 : 0.5;
        double innerY = v == VerticalAlignment.Top ? 1 : v == VerticalAlignment.Bottom ? 0 : 0.5;

        var faded = Color.FromArgb((byte)(color.A * 0.30), color.R, color.G, color.B);
        return new LinearGradientBrush
        {
            StartPoint = new Point(innerX, innerY),
            EndPoint = new Point(outerX, outerY),
            GradientStops =
            {
                new GradientStop { Color = color, Offset = 0.0 },  // inner — opaque over its own appointment
                new GradientStop { Color = color, Offset = 0.5 },  // hold to the anchor edge/corner
                new GradientStop { Color = faded, Offset = 1.0 },  // outer — faded over the neighbour
            },
        };
    }

    // A padded, optionally filled/bordered pill behind a glyph or text decoration. Legacy drew these as
    // ellipses (TextTileShape.Ellipse); we approximate with a fatter pill — generous horizontal padding
    // and fully-rounded ends (CornerRadius tracks half the settled height, so short chips read as ovals).
    // Fill/border fade away from the anchor so the overhang doesn't obscure the neighbouring appointment.
    private static Border Chip(FrameworkElement child, AppointmentDecoration d, HorizontalAlignment h, VerticalAlignment v)
    {
        var chip = new Border
        {
            Child = child,
            Padding = new Thickness(7, 3, 7, 3),
            CornerRadius = new CornerRadius(9), // pre-layout fallback; SizeChanged sets the true half-height
            Background = d.BackColor is Color bc ? FadeBrush(bc, h, v) : null,
            BorderBrush = d.BorderColor is Color brc ? FadeBrush(brc, h, v) : null,
            BorderThickness = d.BorderColor is null ? new Thickness(0) : new Thickness(1),
        };
        // Keep the ends fully rounded as the height settles → a Legacy-style oval/pill regardless of font size.
        chip.SizeChanged += (_, e) => chip.CornerRadius = new CornerRadius(e.NewSize.Height / 2);
        return chip;
    }

    // Place the CENTER of the decoration at the anchor point (Legacy parity): e.g. bottom-right puts the
    // image's centre on the block's bottom-right corner, so it overhangs by half its size. The half-size
    // shift is a RenderTransform (post-layout — doesn't disturb siblings), reapplied when the element's
    // size is known, plus the template's pixel offset.
    private static void Position(FrameworkElement el, AppointmentDecoration d, HorizontalAlignment h, VerticalAlignment v)
    {
        el.HorizontalAlignment = h;
        el.VerticalAlignment = v;

        void Reposition()
        {
            double dx = d.OffsetX;
            double dy = d.OffsetY;
            if (h == HorizontalAlignment.Left) dx -= el.ActualWidth / 2;
            else if (h == HorizontalAlignment.Right) dx += el.ActualWidth / 2;
            if (v == VerticalAlignment.Top) dy -= el.ActualHeight / 2;
            else if (v == VerticalAlignment.Bottom) dy += el.ActualHeight / 2;
            el.RenderTransform = new TranslateTransform { X = dx, Y = dy };
        }

        el.SizeChanged += (_, _) => Reposition();
        Reposition(); // initial (size is 0 until measured; SizeChanged corrects it once laid out)
    }

    private static (HorizontalAlignment, VerticalAlignment) Anchor(DecorationPosition pos, DecorationAlignment align)
        => pos switch
        {
            DecorationPosition.Left or DecorationPosition.FloatLeft => (HorizontalAlignment.Left, AlongVertical(align)),
            DecorationPosition.Right or DecorationPosition.FloatRight => (HorizontalAlignment.Right, AlongVertical(align)),
            // Legacy centres a FloatCenter tile on the block BOTH ways, ignoring Near/Far (its
            // CenterFloatTileLayoutProfile re-centres the area on the block's vertical centre unconditionally).
            // Honouring the alignment here anchored it to the top edge, so a centred "SOLD!"/"CANCELLED" stamp
            // (stored as C|N) overhung upward by half and looked shifted-up/clipped. Always centre vertically.
            DecorationPosition.FloatCenter => (HorizontalAlignment.Center, VerticalAlignment.Center),
            DecorationPosition.Top => (AlongHorizontal(align), VerticalAlignment.Top),
            DecorationPosition.Bottom => (AlongHorizontal(align), VerticalAlignment.Bottom),
            _ => (HorizontalAlignment.Left, VerticalAlignment.Top),
        };

    private static VerticalAlignment AlongVertical(DecorationAlignment a) => a switch
    {
        DecorationAlignment.Far => VerticalAlignment.Bottom,
        DecorationAlignment.Middle => VerticalAlignment.Center,
        _ => VerticalAlignment.Top,
    };

    private static HorizontalAlignment AlongHorizontal(DecorationAlignment a) => a switch
    {
        DecorationAlignment.Far => HorizontalAlignment.Right,
        DecorationAlignment.Middle => HorizontalAlignment.Center,
        _ => HorizontalAlignment.Left,
    };
}
