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
    /// IANA time zone id (e.g. <c>"America/New_York"</c>). Used by the Planner only: when set, this
    /// resource's lane reads <see cref="AppointmentModel.Start"/>/<see cref="AppointmentModel.End"/> as UTC,
    /// draws them in this zone, and reports gesture times back as UTC. Null (the default) = the lane draws
    /// the times as given, like every other view. See the remarks on <see cref="AppointmentModel"/>.
    /// </summary>
    public string? TimeZoneId { get; set; }
}
