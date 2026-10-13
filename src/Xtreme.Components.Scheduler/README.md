# Xtreme.Components.Scheduler

A scheduler / calendar control for [Uno Platform](https://platform.uno) built from WinUI
primitives only: no WebView, no third-party licence. Part of
[Xtreme.Components](https://github.com/XtremeCRM/Xtreme.Components).

**Full guide:** [docs/scheduler.md](https://github.com/XtremeCRM/Xtreme.Components/blob/main/docs/scheduler.md) covers every option with samples.

## Features

- **Five views:** Day, Week, Month, Agenda, and a multi-resource **Planner** with a two-tier
  header that flips between resource→day and day→resource (`GroupMode`).
- **Interaction:** click-select and multi-select, double-click edit requests, right-click context
  requests, drag-to-move (including cross-resource), drag-the-edge-to-resize, and
  double-click/drag-to-create, all reported as events in every view where they apply. The control
  never mutates your data and ships no editor or menu: the host owns those.
- **External drag-drop:** drop items from an outside surface (an unscheduled queue, say) onto a
  slot via `ISchedulerDroppable`.
- **Per-resource time zones (Planner):** a lane whose resource sets `ResourceModel.TimeZoneId` (IANA)
  reads its appointments as UTC and shows that zone's wall-clock.
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
    new ResourceModel { Id = 2, Name = "Bailey", Color = Colors.SeaGreen },
};

Scheduler.Appointments = new[]
{
    new AppointmentModel
    {
        Id = 100,
        Subject = "Install",
        Start = DateTime.Today.AddHours(9),
        End   = DateTime.Today.AddHours(11),
        ResourceIds = { 1 },
    },
};

Scheduler.AppointmentMoveRequested   += (_, e) => { /* persist e.NewStart / e.ToResourceId */ };
Scheduler.AppointmentResizeRequested += (_, e) => { /* persist e.NewStart / e.NewEnd */ };
Scheduler.AppointmentCreateRequested += (_, e) => { /* create at e.Start / e.ResourceId */ };
Scheduler.ExternalAppointmentDropped += (_, e) => { /* create from e.Item at e.Start */ };
Scheduler.AppointmentEditRequested   += (_, e) => { /* open your editor for e.Appointment */ };
```

## Public surface

| Member | Purpose |
|---|---|
| `Appointments` | The events to display (read-only input). |
| `SchedulerResources` | Resource lanes / grouping keys (named to avoid `FrameworkElement.Resources`). |
| `CurrentView` | `Day` / `Week` / `Month` / `Agenda` / `Planner`. |
| `SelectedDate` | The anchor date the view is built around. |
| `GroupMode` | Resource header hierarchy (Day, Week, Planner): `ByResource` or `ByDay`. |
| `TimeIncrement` | Major/minor ticks and pixels-per-minute. |
| `IsReadOnly` | Suppresses editing gestures; selection, edit requests and right-click still work. |
| `AppointmentMoveRequested` / `…ResizeRequested` / `…CreateRequested` | User edits. |
| `ExternalAppointmentDropped` | An `ISchedulerDroppable` dropped on a slot. |
| `AppointmentEditRequested` / `AppointmentContextRequested` | Double-click / right-click on an appointment. |

Only a Planner lane whose resource sets `TimeZoneId` converts times (treating them as UTC and
showing that zone's wall-clock). Every other view and lane draws `Start`/`End` exactly as given,
so pass local times unless you use per-resource zones. See the guide's Data section.

Targets `net10.0` only; the consuming Uno head supplies the renderer. MIT licence.
