using Microsoft.UI.Xaml.Media;

namespace Xtreme.Components.Scheduler.Models;

/// <summary>
/// A small status glyph shown on an appointment block (e.g. an attachment, priority or sync icon).
/// The default templates render these in the block's indicator strip; custom templates can bind to
/// <see cref="AppointmentModel.Indicators"/> and lay them out however they like.
/// </summary>
public sealed class AppointmentIndicator
{
    /// <summary>The icon glyph, e.g. <c>""</c> (a Segoe Fluent / MDL2 codepoint).</summary>
    public string Glyph { get; set; } = string.Empty;

    /// <summary>Glyph color. Null = inherit the block's auto-contrast foreground.</summary>
    public Color? Color { get; set; }

    /// <summary>Optional tooltip shown on hover.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Glyph font. Null = the symbol theme font (Segoe Fluent Icons / MDL2 Assets).</summary>
    public FontFamily? FontFamily { get; set; }
}
