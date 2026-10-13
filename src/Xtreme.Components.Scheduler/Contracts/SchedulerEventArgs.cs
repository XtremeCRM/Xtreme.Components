using Windows.Foundation;
using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Contracts;

/// <summary>
/// Raised when the user drags an appointment to a new time and/or resource. The control does
/// not mutate the appointment — the host applies the change and refreshes if it accepts it.
/// </summary>
public sealed class AppointmentMoveArgs : EventArgs
{
    public required AppointmentModel Appointment { get; init; }

    /// <summary>Proposed new start (UTC semantics, same as <see cref="AppointmentModel.Start"/>).</summary>
    public required DateTime NewStart { get; init; }

    /// <summary>Proposed new end, preserving the original duration.</summary>
    public required DateTime NewEnd { get; init; }

    /// <summary>The resource lane the drag started in (null in non-resource views).</summary>
    public object? FromResourceId { get; init; }

    /// <summary>The resource lane the drag ended in (equals <see cref="FromResourceId"/> for an in-lane move).</summary>
    public object? ToResourceId { get; init; }

    /// <summary>True when Ctrl was held (copy drag): the host should duplicate the appointment at the
    /// new position and leave the original in place, rather than relocating it.</summary>
    public bool IsCopy { get; init; }

    /// <summary>True when Shift was held at drop. The control ascribes no meaning to it; a host may use
    /// it to skip a confirmation step and commit the change immediately.</summary>
    public bool WithShift { get; init; }
}

/// <summary>Raised when the user drags an appointment's top or bottom edge to change its duration.</summary>
public sealed class AppointmentResizeArgs : EventArgs
{
    public required AppointmentModel Appointment { get; init; }

    /// <summary>Proposed new start (changed when the top edge is dragged).</summary>
    public required DateTime NewStart { get; init; }

    /// <summary>Proposed new end (changed when the bottom edge is dragged).</summary>
    public required DateTime NewEnd { get; init; }

    /// <summary>True when Shift was held at drop. The control ascribes no meaning to it; a host may use
    /// it to skip a confirmation step and commit the change immediately.</summary>
    public bool WithShift { get; init; }
}

/// <summary>One appointment's resolved target in a multi-selection move (see <see cref="AppointmentsMoveArgs"/>).
/// Each item may move between a different resource lane while the group shares one (time, lane) delta.</summary>
public sealed class AppointmentMoveItem
{
    public required AppointmentModel Appointment { get; init; }
    public required DateTime NewStart { get; init; }
    public required DateTime NewEnd { get; init; }
    public object? FromResourceId { get; init; }
    public object? ToResourceId { get; init; }
}

/// <summary>Raised when the user drags a multi-selection: every selected item lands at its own position
/// plus the shared (time, lane) delta. The host applies the per-item resource change and persists atomically.</summary>
public sealed class AppointmentsMoveArgs : EventArgs
{
    public required IReadOnlyList<AppointmentMoveItem> Items { get; init; }

    /// <summary>True when Ctrl was held (copy drag): the host should duplicate each appointment at its
    /// new position and leave the originals in place, rather than relocating them.</summary>
    public bool IsCopy { get; init; }

    /// <summary>True when Shift was held at drop. The control ascribes no meaning to it; a host may use
    /// it to skip a confirmation step and commit the change immediately.</summary>
    public bool WithShift { get; init; }
}

/// <summary>One appointment's resolved target in a multi-selection resize (see <see cref="AppointmentsResizeArgs"/>).</summary>
public sealed class AppointmentResizeItem
{
    public required AppointmentModel Appointment { get; init; }
    public required DateTime NewStart { get; init; }
    public required DateTime NewEnd { get; init; }
}

/// <summary>Raised when the user resizes a multi-selection: the same edge-delta is applied to every
/// selected appointment.</summary>
public sealed class AppointmentsResizeArgs : EventArgs
{
    public required IReadOnlyList<AppointmentResizeItem> Items { get; init; }

    /// <summary>True when Shift was held at drop. The control ascribes no meaning to it; a host may use
    /// it to skip a confirmation step and commit the change immediately.</summary>
    public bool WithShift { get; init; }
}

/// <summary>Raised when the user deletes the current (single or multi) selection.</summary>
public sealed class AppointmentsDeleteArgs : EventArgs
{
    public required IReadOnlyList<AppointmentModel> Appointments { get; init; }

    /// <summary>True when Shift was held during the delete. The control ascribes no meaning to it; a host
    /// may use it to skip its delete-confirmation prompt (mirrors <see cref="AppointmentsMoveArgs.WithShift"/>).</summary>
    public bool WithShift { get; init; }
}

/// <summary>Raised when an external <see cref="ISchedulerDroppable"/> item is dropped on a time slot.</summary>
public sealed class ExternalDropArgs : EventArgs
{
    public required ISchedulerDroppable Item { get; init; }

    /// <summary>The snapped slot start the item was dropped on.</summary>
    public required DateTime Start { get; init; }

    /// <summary>Slot start + the item's <see cref="ISchedulerDroppable.DefaultDuration"/>.</summary>
    public required DateTime End { get; init; }

    /// <summary>The resource lane the slot belongs to (null in non-resource views).</summary>
    public object? ResourceId { get; init; }
}

/// <summary>
/// Raised when the user clicks/taps an appointment (i.e. asks to edit it). The control selects the
/// appointment regardless; this hook is for opening an editor. A host that opens its own editor
/// should set <see cref="Handled"/> = true; if no handler sets it, the control falls back to its
/// own default editor.
/// </summary>
public sealed class AppointmentEditRequestedArgs : EventArgs
{
    public required AppointmentModel Appointment { get; init; }

    /// <summary>Set true to tell the control a host is showing the editor — suppresses the default.</summary>
    public bool Handled { get; set; }
}

/// <summary>Raised when the user clicks/taps an appointment. <see cref="WithCtrl"/> is true when the
/// Ctrl/Cmd modifier was held (multi-select toggle); a plain click is a single-select + edit request.</summary>
public sealed class AppointmentClickArgs : EventArgs
{
    public required AppointmentModel Appointment { get; init; }

    /// <summary>True when Ctrl/Cmd was held — toggle this appointment in/out of the selection set.</summary>
    public bool WithCtrl { get; init; }
}

/// <summary>
/// Raised when the user right-clicks (or otherwise requests a context menu on) an appointment. The
/// control selects the appointment (visual) and reports it here; the host decides what menu to show —
/// the library is domain-agnostic and builds none itself. Anchor a host menu at <see cref="Position"/>.
/// </summary>
public sealed class AppointmentContextRequestedArgs : EventArgs
{
    public required AppointmentModel Appointment { get; init; }

    /// <summary>The click position, relative to the <see cref="Microsoft.UI.Xaml.XamlRoot"/> content, so the
    /// host can anchor its context menu at the click. Null when unknown (e.g. the overlay isn't yet in a
    /// live tree). Transform it into the target's space before showing.</summary>
    public Point? Position { get; init; }
}

/// <summary>Raised when the user clicks/taps or drag-selects an empty slot to create an appointment.</summary>
public sealed class AppointmentCreateArgs : EventArgs
{
    public required DateTime Start { get; init; }

    public required DateTime End { get; init; }

    /// <summary>The resource lane the slot belongs to (null in non-resource views).</summary>
    public object? ResourceId { get; init; }

    /// <summary>True when the slot was in the all-day region.</summary>
    public bool IsAllDay { get; init; }

    /// <summary>The click position, relative to the <see cref="Microsoft.UI.Xaml.XamlRoot"/> content, so the
    /// host can anchor a popup (e.g. an event-type picker) at the click. Null when unknown (e.g. a
    /// synthesized create). Transform it into the target's space before showing.</summary>
    public Point? Position { get; init; }
}

/// <summary>
/// Raised when the user single-clicks an empty time-grid slot (Day/Week). A single click no longer
/// creates — it just reports the clicked lane + time so the host can track a "selected resource" for
/// the new-appointment panel. A SECOND click within the double-click window raises
/// <see cref="SchedulerControl.AppointmentCreateRequested"/> instead.
/// </summary>
public sealed class EmptySlotSelectedArgs : EventArgs
{
    /// <summary>The snapped slot start the user clicked.</summary>
    public required DateTime Start { get; init; }

    /// <summary>The resource lane the slot belongs to (null in non-resource views).</summary>
    public object? ResourceId { get; init; }

    /// <summary>The click position, relative to the <see cref="Microsoft.UI.Xaml.XamlRoot"/> content (so the
    /// host can anchor a popup at the click). Null when unknown.</summary>
    public Point? Position { get; init; }
}
