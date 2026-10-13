using Microsoft.UI.Xaml.Controls.Primitives;
using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// The hover popup for views whose pointer overlay covers the blocks (Day/Week and the Planner), so a
/// per-block <see cref="ToolTip"/> never sees the pointer. A ToolTip can't be positioned in Uno
/// (PlacementTarget is unimplemented), so the content is hosted in a <see cref="Popup"/> placed at the
/// hover point, inside the overlay so offsets share the pointer's coordinate space.
/// </summary>
internal sealed class HoverCard
{
    private readonly Popup _popup = new();
    private readonly FrameworkElement _overlay;
    private readonly SchedulerPalette _palette;
    private readonly AppointmentTemplating _templating;

    public HoverCard(Panel overlay, SchedulerPalette palette, AppointmentTemplating templating)
    {
        _overlay = overlay;
        _palette = palette;
        _templating = templating;
        overlay.Children.Add(_popup);
    }

    /// <summary>The scroller around the overlay, so the card is kept inside the visible viewport (the overlay
    /// spans the whole scrollable content, so a naive pointer-relative offset bleeds off-screen).</summary>
    public ScrollViewer? Scroll { get; set; }

    public void Hide() => _popup.IsOpen = false;

    /// <summary>Show the card for <paramref name="appointment"/> near <paramref name="pt"/> (overlay coordinates),
    /// or hide it when null.</summary>
    public void Show(AppointmentModel? appointment, Point pt)
    {
        if (appointment is null)
        {
            Hide();
            return;
        }

        // The popup has no chrome of its own, so wrap the content in a themed card.
        var card = new Border
        {
            Background = _palette.SurfaceBackground,
            BorderBrush = _palette.EventBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            MaxWidth = 360,
            Child = EventBlockFactory.BuildTooltipContent(appointment, _palette, _templating),
        };
        _popup.Child = card;

        // Default placement is down-and-right of the pointer. If the card would spill past the right/bottom
        // edge of the visible viewport, flip it to the other side of the pointer, then clamp it on screen.
        card.Measure(new Size(card.MaxWidth, double.PositiveInfinity));
        var size = card.DesiredSize;

        var viewLeft = Scroll?.HorizontalOffset ?? 0;
        var viewTop = Scroll?.VerticalOffset ?? 0;
        var viewW = Scroll is { ViewportWidth: > 0 } ? Scroll.ViewportWidth : _overlay.ActualWidth;
        var viewH = Scroll is { ViewportHeight: > 0 } ? Scroll.ViewportHeight : _overlay.ActualHeight;

        const double gap = 16;
        var x = pt.X + gap;
        if (x + size.Width > viewLeft + viewW)
        {
            x = pt.X - gap - size.Width; // flip to the left of the pointer
        }
        x = Math.Max(viewLeft + 2, Math.Min(x, viewLeft + viewW - size.Width - 2));

        var y = pt.Y + 8;
        if (y + size.Height > viewTop + viewH)
        {
            y = pt.Y - 8 - size.Height; // flip above the pointer
        }
        y = Math.Max(viewTop + 2, Math.Min(y, viewTop + viewH - size.Height - 2));

        _popup.HorizontalOffset = x;
        _popup.VerticalOffset = y;
        _popup.IsOpen = true;
    }
}
