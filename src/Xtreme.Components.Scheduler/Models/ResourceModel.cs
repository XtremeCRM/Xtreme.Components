namespace Xtreme.Components.Scheduler.Models;

/// <summary>
/// A schedulable resource — a lane/column in the Planner view and a grouping key for
/// appointments. Domain-agnostic: a host maps its own people / rooms / equipment onto this.
/// </summary>
public sealed class ResourceModel
{
    /// <summary>
    /// Host-defined identity, matched against <see cref="AppointmentModel.ResourceIds"/> by
    /// value equality. Any value type works (int, Guid, string).
    /// </summary>
    public object Id { get; set; } = 0;

    /// <summary>Display name for the resource header.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Lane accent color (header chip + default event color). Null = theme default.</summary>
    public Color? Color { get; set; }

    /// <summary>
    /// IANA timezone id (e.g. <c>"America/New_York"</c>) used to render this resource's
    /// appointments in its local wall-clock. Null = render in the control's local timezone.
    /// Wired into rendering in Phase 7.
    /// </summary>
    public string? TimeZoneId { get; set; }
}
