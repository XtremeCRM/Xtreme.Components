namespace Xtreme.Components.Scheduler.Contracts;

/// <summary>
/// Implemented by items dragged into the scheduler from an external surface (e.g. an
/// "unscheduled queue" panel). When such an item is dropped on a time slot the control
/// raises its external-drop event so the host can create a real appointment.
/// </summary>
/// <remarks>
/// Finalized in Phase 5. The control never persists anything itself — it only reports the
/// drop (item + target resource + snapped slot) and lets the host decide what to create.
/// </remarks>
public interface ISchedulerDroppable
{
    /// <summary>Subject to seed the new appointment with.</summary>
    string Subject { get; }

    /// <summary>Default duration applied from the drop slot when the item has no intrinsic length.</summary>
    TimeSpan DefaultDuration { get; }

    /// <summary>Opaque host payload echoed back on drop so the host can resolve the source item.</summary>
    object? Payload { get; }
}
