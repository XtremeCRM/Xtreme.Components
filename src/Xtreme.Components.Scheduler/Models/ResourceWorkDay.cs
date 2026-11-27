namespace Xtreme.Components.Scheduler.Models;

/// <summary>
/// One resource's working hours on one day, supplied through <c>SchedulerControl.ResourceWorkDays</c>.
/// Day and Week shade that resource's column outside <see cref="Start"/>..<see cref="End"/> (or the whole
/// column when <see cref="IsOff"/>) and draw a thin line at the start and end; Month lists the resources
/// that are off on each day. A resource/day with no entry keeps the control's global
/// <c>WorkDayStart</c>/<c>WorkDayEnd</c> band. Domain-agnostic: the host resolves its own weekly
/// patterns, exceptions and defaults into these per-day rows.
/// </summary>
public sealed class ResourceWorkDay
{
    /// <summary>The resource, matched against <see cref="ResourceModel.Id"/> by value equality.</summary>
    public required object ResourceId { get; init; }

    /// <summary>The day (the time part is ignored).</summary>
    public required DateTime Date { get; init; }

    /// <summary>The resource does not work this day: the whole column is shaded and no lines are drawn.</summary>
    public bool IsOff { get; init; }

    /// <summary>Start of the working window, as a time of day (ignored when <see cref="IsOff"/>).</summary>
    public TimeSpan Start { get; init; }

    /// <summary>End of the working window, as a time of day; 24:00 at most (ignored when <see cref="IsOff"/>).</summary>
    public TimeSpan End { get; init; }

    /// <summary>The day departs from the resource's usual pattern (a one-day exception). Marked with a small
    /// dot on the middle of each hours line, or in the middle of the column for a day off.</summary>
    public bool IsOverride { get; init; }
}
