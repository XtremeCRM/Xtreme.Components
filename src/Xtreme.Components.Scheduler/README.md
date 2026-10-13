# Xtreme.Components.Scheduler

A scheduler / calendar control for [Uno Platform](https://platform.uno) built from WinUI
primitives only: no WebView, no third-party licence. Part of
[Xtreme.Components](https://github.com/XtremeCRM/Xtreme.Components).

## Features

- **Five views:** Day, Week, Month, Agenda, and a multi-resource **Planner** with a two-tier
  header that flips between resource→day and day→resource (`GroupMode`).
- **Interaction:** drag-to-move (including cross-resource), drag-the-edge-to-resize, and
  click/drag-to-create, all reported as events. The control never mutates your data.
- **External drag-drop:** drop items from an outside surface (an unscheduled queue, say) onto a
  slot via `ISchedulerDroppable`.
- **Per-resource time zones:** a UTC event renders at the correct wall-clock in each resource's
  lane (`ResourceModel.TimeZoneId`, IANA).
- **Data-driven:** all-day and multi-day spanning events, overlap column-splitting,
  recurring-event indicators, configurable time increments, read-only mode, templatable
  appointment tiles, light and dark palette.

## Quick start

```xml
<Page xmlns:scheduler="using:Xtreme.Components.Scheduler.Controls">
    <scheduler:SchedulerControl x:Name="Scheduler" CurrentView="Week" />
</Page>
```

```csharp
using Xtreme.Components.Scheduler.Models;

Scheduler.SchedulerResources = new[]
{
    new ResourceModel { Id = 1, Name = "Alex",   Color = Colors.RoyalBlue },
    new ResourceModel { Id = 2, Name = "Bailey", Color = Colors.SeaGreen, TimeZoneId = "America/New_York" },
};

Scheduler.Appointments = new[]
{
    new AppointmentModel
    {
        Id = 100,
        Subject = "Install",
        Start = DateTime.UtcNow.Date.AddHours(15),   // UTC
        End   = DateTime.UtcNow.Date.AddHours(17),
        ResourceIds = { 1 },
    },
};

Scheduler.AppointmentMoveRequested   += (_, e) => { /* persist e.NewStart / e.ToResourceId */ };
Scheduler.AppointmentResizeRequested += (_, e) => { /* persist e.NewStart / e.NewEnd */ };
Scheduler.AppointmentCreateRequested += (_, e) => { /* create at e.Start / e.ResourceId */ };
Scheduler.ExternalAppointmentDropped += (_, e) => { /* create from e.Item at e.Start */ };
```

## Public surface

| Member | Purpose |
|---|---|
| `Appointments` | The events to display (read-only input). |
| `SchedulerResources` | Resource lanes / grouping keys (named to avoid `FrameworkElement.Resources`). |
| `CurrentView` | `Day` / `Week` / `Month` / `Agenda` / `Planner`. |
| `SelectedDate` | The anchor date the view is built around. |
| `GroupMode` | Planner header hierarchy: `ByResource` or `ByDay`. |
| `TimeIncrement` | Major/minor ticks and pixels-per-minute. |
| `IsReadOnly` | Suppresses all interaction. |
| `AppointmentMoveRequested` / `…ResizeRequested` / `…CreateRequested` | User edits. |
| `ExternalAppointmentDropped` | An `ISchedulerDroppable` dropped on a slot. |

Times on `AppointmentModel` are UTC instants; the Planner converts them to each resource's
`TimeZoneId`. All other views render in the viewer's local time.

Targets `net10.0` only; the consuming Uno head supplies the renderer. MIT licence.
