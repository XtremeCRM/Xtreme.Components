using Microsoft.UI.Xaml.Media;

namespace Xtreme.Components.Scheduler.Models;

/// <summary>Where a decoration anchors on the appointment block.</summary>
/// <remarks>
/// The edge values (<see cref="Left"/>/<see cref="Top"/>/<see cref="Right"/>/<see cref="Bottom"/>) and
/// the <c>Float*</c> values both render as an overlay pinned to that side; the distinction is preserved
/// from Legacy's tile model (an edge tile would reserve space, a float tile overlays) but this control
/// overlays all of them for v1. Reserving edge space is a deferred refinement.
/// </remarks>
public enum DecorationPosition
{
    Left,
    Top,
    Right,
    Bottom,
    FloatLeft,
    FloatCenter,
    FloatRight,
}

/// <summary>Alignment of a decoration along its anchored edge (near = top/left, far = bottom/right).</summary>
public enum DecorationAlignment
{
    Near,
    Middle,
    Far,
}

/// <summary>
/// A small positioned tile drawn on top of an appointment block — the domain-agnostic primitive behind
/// the host's <c>Tile</c> and <c>Adornment</c> features. A decoration shows (in priority order) an
/// <see cref="Image"/>, then a <see cref="Glyph"/>, then <see cref="Text"/>, anchored at
/// <see cref="Position"/>/<see cref="Alignment"/> with a pixel <see cref="OffsetX"/>/<see cref="OffsetY"/>.
/// The default templates render <see cref="AppointmentModel.Adornments"/> and
/// <see cref="AppointmentModel.Tiles"/>; custom templates can lay them out however they like via the
/// appointment context.
/// </summary>
public sealed class AppointmentDecoration
{
    /// <summary>Text label (used when no <see cref="Image"/>/<see cref="Glyph"/> is set).</summary>
    public string? Text { get; set; }

    /// <summary>Icon-font glyph (a Segoe Fluent / MDL2 codepoint). Used when no <see cref="Image"/> is set.</summary>
    public string? Glyph { get; set; }

    /// <summary>Glyph font. Null = the symbol theme font (Segoe Fluent Icons / MDL2 Assets).</summary>
    public FontFamily? GlyphFontFamily { get; set; }

    /// <summary>Raw image bytes (PNG/JPG/…). Takes precedence over <see cref="Glyph"/>/<see cref="Text"/>.</summary>
    public byte[]? Image { get; set; }

    /// <summary>Where the decoration anchors on the block. Defaults to the top-left.</summary>
    public DecorationPosition Position { get; set; } = DecorationPosition.FloatLeft;

    /// <summary>Alignment along the anchored edge.</summary>
    public DecorationAlignment Alignment { get; set; } = DecorationAlignment.Near;

    /// <summary>Horizontal pixel offset from the anchor (positive = right).</summary>
    public int OffsetX { get; set; }

    /// <summary>Vertical pixel offset from the anchor (positive = down).</summary>
    public int OffsetY { get; set; }

    /// <summary>Background fill for a text decoration. Null = transparent.</summary>
    public Color? BackColor { get; set; }

    /// <summary>Foreground (text/glyph) color. Null = the block's auto-contrast foreground.</summary>
    public Color? ForeColor { get; set; }

    /// <summary>Border color for a text decoration. Null = no border.</summary>
    public Color? BorderColor { get; set; }

    /// <summary>Font size for a text/glyph decoration.</summary>
    public double FontSize { get; set; } = 10;

    /// <summary>Whether a text decoration renders bold (used by data tiles).</summary>
    public bool Bold { get; set; }

    /// <summary>Optional tooltip shown on hover.</summary>
    public string? Tooltip { get; set; }
}
