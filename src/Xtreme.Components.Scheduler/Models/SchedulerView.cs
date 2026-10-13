namespace Xtreme.Components.Scheduler.Models;

/// <summary>The calendar view currently displayed by the <c>SchedulerControl</c>.</summary>
public enum SchedulerView
{
    /// <summary>Single day, time grid.</summary>
    Day,

    /// <summary>Seven days, time grid.</summary>
    Week,

    /// <summary>Calendar month, day cells.</summary>
    Month,

    /// <summary>Flat, date-grouped list of upcoming appointments.</summary>
    Agenda,

    /// <summary>Multi-resource lanes with a nested time grid.</summary>
    Planner,
}
