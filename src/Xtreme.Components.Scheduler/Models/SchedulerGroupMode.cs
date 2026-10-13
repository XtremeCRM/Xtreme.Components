namespace Xtreme.Components.Scheduler.Models;

/// <summary>
/// Header hierarchy for the Planner view: which axis is the outer grouping.
/// </summary>
public enum SchedulerGroupMode
{
    /// <summary>Outer = resource, inner = day. One lane per resource, days nested within.</summary>
    ByResource,

    /// <summary>Outer = day, inner = resource. One column per day, resources nested within.</summary>
    ByDay,
}
