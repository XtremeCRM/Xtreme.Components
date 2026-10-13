namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// The appointment-rendering configuration the views hand to <see cref="EventBlockFactory"/>:
/// an optional code renderer (highest priority) and a per-surface <see cref="DataTemplate"/>.
/// Resolution order per block: renderer → matching template → built-in default.
/// </summary>
internal sealed class AppointmentTemplating
{
    /// <summary>Template for full time-grid blocks (Day / Week body).</summary>
    public DataTemplate? TimedTemplate { get; init; }

    /// <summary>Template for compact chips (Month cell, all-day row).</summary>
    public DataTemplate? ChipTemplate { get; init; }

    /// <summary>Template for Planner timeline bars.</summary>
    public DataTemplate? BarTemplate { get; init; }

    /// <summary>Template for the hover popup shown over any appointment (all surfaces share one).</summary>
    public DataTemplate? TooltipTemplate { get; init; }

    /// <summary>
    /// Code renderer. Returns the block content for the given context, or null to fall through to
    /// the template / built-in default. Switch on <see cref="AppointmentContext.Surface"/> to vary
    /// by shape.
    /// </summary>
    public Func<AppointmentContext, FrameworkElement?>? Renderer { get; init; }

    /// <summary>
    /// Code renderer for the hover popup. Returns the popup content for the context, or null to
    /// fall through to <see cref="TooltipTemplate"/> / the built-in default popup.
    /// </summary>
    public Func<AppointmentContext, FrameworkElement?>? TooltipRenderer { get; init; }

    /// <summary>Corner radius (px) for timed event blocks (and the selection outline). Settings-driven.</summary>
    public double CornerRadius { get; init; } = 4;

    /// <summary>Whether data-driven <see cref="Models.AppointmentModel.Tiles"/> are rendered. Settings-driven
    /// (the host's "Show tiles" toggle). Adornments are always shown. Default true.</summary>
    public bool ShowTiles { get; init; } = true;

    /// <summary>The all-defaults configuration (no custom renderer or templates).</summary>
    public static readonly AppointmentTemplating None = new();
}
