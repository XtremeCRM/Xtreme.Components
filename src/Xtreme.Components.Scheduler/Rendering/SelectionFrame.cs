using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// Wraps a chip (Month, Agenda, all-day row) or a Planner bar so the view can show the "selected" outline
/// without a re-render: a sibling red border drawn inside the block's bounds, collapsed until selected. The
/// frame carries the host's ORIGINAL appointment (a zoned Planner lane renders a shifted copy), so the
/// selection matches what the host set and what the events report.
/// </summary>
internal sealed partial class SelectionFrame : Grid
{
    /// <summary>The selection outline red, shared with the time grid's selected-block outline.</summary>
    public static readonly Color SelectionRed = Color.FromArgb(0xFF, 0xCC, 0x10, 0x10);

    private readonly Border _outline;

    public SelectionFrame(FrameworkElement block, AppointmentModel appointment, double cornerRadius)
    {
        Appointment = appointment;
        Tag = appointment;
        HorizontalAlignment = block.HorizontalAlignment;
        VerticalAlignment = block.VerticalAlignment;
        Margin = block.Margin;
        block.Margin = new Thickness(0);

        _outline = new Border
        {
            BorderBrush = new SolidColorBrush(SelectionRed),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(cornerRadius),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Children.Add(block);
        Children.Add(_outline);
    }

    /// <summary>The appointment this frame represents (the host's own instance).</summary>
    public AppointmentModel Appointment { get; }

    /// <summary>Show or hide the selected outline.</summary>
    public void SetSelected(bool selected) =>
        _outline.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Outline every frame whose appointment is in <paramref name="selected"/> (same instance or
    /// same host Id) and clear the rest.</summary>
    public static void Apply(IEnumerable<SelectionFrame> frames, IReadOnlyCollection<AppointmentModel>? selected)
    {
        foreach (var f in frames)
        {
            f.SetSelected(selected is { Count: > 0 } && selected.Any(s => AppointmentIdentity.Same(f.Appointment, s)));
        }
    }
}

/// <summary>How the views decide two appointment references are the same logical event.</summary>
internal static class AppointmentIdentity
{
    /// <summary>The same instance, or the same non-null host Id (a multi-resource event renders once per
    /// lane but shares one Id).</summary>
    public static bool Same(AppointmentModel a, AppointmentModel b) =>
        ReferenceEquals(a, b) || (a.Id is not null && Equals(a.Id, b.Id));
}
