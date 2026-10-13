# Scheduler

`Xtreme.Components.Scheduler` is a calendar / resource scheduler for Uno Platform, drawn entirely
from WinUI primitives (no WebView, no third-party grid). It renders Day, Week, Month, Agenda and a
multi-resource Planner view over a list of appointments and a list of resources.

- Live demo: [gallery, Scheduler page](https://xtremecrm.github.io/Xtreme.Components/)
- Sample source used throughout this guide:
  [`samples/Xtreme.Components.Gallery/Pages/SchedulerPage.cs`](https://github.com/XtremeCRM/Xtreme.Components/blob/main/samples/Xtreme.Components.Gallery/Pages/SchedulerPage.cs)
- Package layout and conventions: [architecture.md](architecture.md)
- Colours and theme tokens: [theming.md](theming.md)

Namespaces:

| Namespace | Contains |
|---|---|
| `Xtreme.Components.Scheduler.Controls` | `SchedulerControl`, `SchedulerToolbar`, the view controls |
| `Xtreme.Components.Scheduler.Models` | `AppointmentModel`, `ResourceModel`, decorations, enums, toolbar models |
| `Xtreme.Components.Scheduler.Contracts` | Event argument types, `ISchedulerDroppable` |
| `Xtreme.Components.Scheduler.Rendering` | `AppointmentContext` (the template / renderer data context) |
| `Xtreme.Components.Scheduler.Interaction` | `ExternalDropHandler` (external drag-drop bridge) |
| `Xtreme.Components.Scheduler.Utilities` | `SchedulerTimeFormatter`, `TimeZoneConverter` |

---

## 1. Overview and the core design rule

**The control never mutates your data.** Every user gesture (drag to move, drag an edge to resize,
drag or double-click to create, drop an external item, press Delete, right-click) is reported as an
event carrying the *proposed* change. The host decides whether to accept it, updates its own data,
and re-assigns the lists so the control re-renders. If the host ignores an event, nothing changes on
screen.

The same rule extends to UI the control could be expected to own but does not:

- **No context menu.** Right-click raises `AppointmentContextRequested`; the host builds the menu.
- **No appointment editor.** Double-click raises `AppointmentEditRequested`; the host shows its own form.
- **No resource picker.** The host filters `SchedulerResources`.
- **No persistence.** Settings the user changes in the toolbar (view, time scale, work hours...) are
  written back to the control's dependency properties; saving them is the host's job.

Minimal page:

```xml
<Page xmlns:scheduler="using:Xtreme.Components.Scheduler.Controls">
    <scheduler:SchedulerControl x:Name="Scheduler" CurrentView="Week" FirstDayOfWeek="Monday" />
</Page>
```

```csharp
using Xtreme.Components.Scheduler.Models;

Scheduler.SchedulerResources = new[]
{
    new ResourceModel { Id = 1, Name = "Alex",   Color = Microsoft.UI.Colors.RoyalBlue },
    new ResourceModel { Id = 2, Name = "Bailey", Color = Microsoft.UI.Colors.SeaGreen },
};

Scheduler.Appointments = new[]
{
    new AppointmentModel
    {
        Id = 1,
        Subject = "Site survey",
        Start = monday.AddHours(9),
        End = monday.AddHours(11),
        ResourceIds = { 1 },
    },
};
```

Merge the theme dictionaries first; see [theming.md, section 1](theming.md#1-merge-the-dictionaries).

---

## 2. Data

### AppointmentModel

A plain, domain-agnostic class. Map your own entities onto it; carry the original in `Tag`.

| Property | Type | Default | Notes |
|---|---|---|---|
| `Id` | `object?` | `null` | Any value type (int, Guid, string). Compared with `Equals`. Used to treat a multi-resource appointment as one event. |
| `Subject` | `string` | `""` | Block title. |
| `Description` | `string?` | `null` | Shown as the note in the default tooltip (exposed as `AppointmentContext.Note`). |
| `Memo` | `string?` | `null` | Secondary "Internal memo" section in the default tooltip. |
| `Start` / `End` | `DateTime` | | See [Times and time zones](#times-and-time-zones). `End` must be >= `Start`. |
| `IsAllDay` | `bool` | `false` | Renders in the all-day banner; never time-zone shifted. |
| `ResourceIds` | `IList<object>` | empty list | Resource ids this appointment belongs to. One block is drawn per matching resource lane. Empty = unassigned. |
| `BarColor` | `Color?` | `null` | Left accent bar. Null = theme default. |
| `BackColor` | `Color?` | `null` | Block fill. Null = theme default. Text colour is auto-contrasted against it. |
| `RecurrenceRule` | `string?` | `null` | Any non-blank value shows the recurrence glyph. The control does **not** expand occurrences; supply each occurrence as its own appointment. |
| `RecurrenceException` | `string?` | `null` | Carried, not interpreted. |
| `IsRecurring` | `bool` (read-only) | | `RecurrenceRule` is non-blank. |
| `Duration` | `TimeSpan` (read-only) | | `End - Start`. |
| `IsLocked` | `bool` | `false` | Lock strip; cannot be selected, moved, resized or deleted. Right-click still raises the context event. |
| `IsComplete`, `IsConfirmed`, `IsCancelled`, `IsTentative` | `bool` | `false` | Each shows a built-in status glyph. |
| `Indicators` | `IList<AppointmentIndicator>` | empty | Extra status glyphs. See [section 8](#8-icons-and-adornments). |
| `Tiles` | `IList<AppointmentDecoration>` | empty | Data tiles. See [section 7](#7-tiles). |
| `Adornments` | `IList<AppointmentDecoration>` | empty | Positioned badges. See [section 8](#8-icons-and-adornments). |
| `Details` | `IList<AppointmentDetail>` | empty | Label/value rows in the default tooltip. See [section 9](#9-tooltips). |
| `Tag` | `object?` | `null` | Opaque host payload. Never read by the control; available to templates and renderers. |

### ResourceModel

| Property | Type | Default | Notes |
|---|---|---|---|
| `Id` | `object` | `0` | Matched against `AppointmentModel.ResourceIds` by value equality. |
| `Name` | `string` | `""` | Lane / column header. |
| `Color` | `Color?` | `null` | Header chip colour. |
| `TimeZoneId` | `string?` | `null` | IANA id such as `"America/New_York"`. Used by the Planner only (below). |

### Times and time zones

`AppointmentModel` documents `Start`/`End` as UTC instants, but the control only converts in one
place:

- **Planner view, resource with `TimeZoneId`**: the value is treated as UTC and shown in that zone's
  wall-clock (`TimeZoneConverter.ToZone`). Times reported back by Planner gestures in that lane are
  converted back to UTC (`TimeZoneConverter.FromZone`).
- **Everywhere else** (Day, Week, Month, Agenda, and Planner lanes with no `TimeZoneId`): the
  `DateTime` value is drawn as-is. There is no UTC-to-local conversion, and event args report times
  on the same basis.

In practice: if you do not use `TimeZoneId`, pass the wall-clock times you want displayed (the gallery
uses local times). If you do use it, keep in mind that Day/Week will still draw the raw value.

`TimeZoneConverter.ShortLabel(ianaId)` returns the last segment of an id ("New York") for your own
headers.

### Assigning data and re-rendering

`Appointments` and `SchedulerResources` are `IReadOnlyList<T>` dependency properties. The view is
rebuilt when the property value changes, so **mutating a model in place or adding to the same list
instance does not re-render**. Assign a new list after every change. The gallery funnels every edit
through one method:

```csharp
private void Refresh()
{
    // ...apply host-side state to the models...
    _scheduler.SchedulerResources = _resources.Where(r => !_hiddenResources.Contains(r.Id)).ToList();
    _scheduler.Appointments = _appointments.ToList();   // a new list instance triggers the rebuild
}
```

The vertical and horizontal scroll position survives a re-render, so live updates do not jump the
grid back to the morning.

---

## 3. Views and view properties

`CurrentView` selects one of `SchedulerView.Day`, `Week`, `Month`, `Agenda`, `Planner`. The toolbar's
view switcher writes the same property.

| View | Shows | Interaction |
|---|---|---|
| `Day` | `VisibleDays` consecutive days (default 1) from `SelectedDate`, or the `SelectedDates` set | Full: select, multi-select, move, resize, create, external drop, context menu, hover tooltip |
| `Week` | 7 days starting on `FirstDayOfWeek` | Same as Day |
| `Month` | The month containing `SelectedDate`, as day cells with chips | Display only (chip tooltips) |
| `Agenda` | Date-grouped list over `VisibleDays` or `SelectedDates` | Display only |
| `Planner` | One week of horizontal resource lanes | Move (including across lanes), resize, drag-create, external drop, context menu. No click-select, edit request or multi-select. Interaction exists for `GroupMode.ByResource` only and not when read-only; `ByDay` is display-only. |

In Day and Week, resources become side-by-side columns within each day (or days within each
resource, per `GroupMode`) when `SchedulerResources` is non-empty.

### Dependency properties

| Property | Type | Default | Purpose |
|---|---|---|---|
| `Appointments` | `IReadOnlyList<AppointmentModel>?` | `null` | Items to display. Read-only input. |
| `SchedulerResources` | `IReadOnlyList<ResourceModel>?` | `null` | Resource lanes / columns. Named to avoid clashing with `FrameworkElement.Resources`. |
| `CurrentView` | `SchedulerView` | `Week` | Active view. |
| `SelectedDate` | `DateTimeOffset` | today (set in the constructor) | Anchor date. Time part is ignored. Prev/Next/Today write it. |
| `SelectedDates` | `IReadOnlyList<DateTime>?` | `null` | Non-contiguous dates shown side by side (e.g. "the next four Saturdays"). Honoured by Day and Agenda only; switching to Week/Month/Planner clears it. Overrides `SelectedDate` + `VisibleDays`. |
| `VisibleDays` | `int` | `1` | Day count for Day and Agenda. |
| `FirstDayOfWeek` | `DayOfWeek` | `Sunday` | First day for Week and Planner. The toolbar offers Sunday or Monday. |
| `GroupMode` | `SchedulerGroupMode` | `ByResource` | Header hierarchy: `ByResource` (resource, then day) or `ByDay` (day, then resource). |
| `HideIdleResources` | `bool` | `false` | Hide resource columns with nothing in the visible range (Day/Week). |
| `WorkDayStart` | `TimeSpan` | `08:00` | Start of the shaded working-hours band (Day/Week). |
| `WorkDayEnd` | `TimeSpan` | `17:00` | End of the band. |
| `TimeIncrement` | `TimeIncrementConfig` | `null` (treated as `TimeIncrementConfig.Default`) | Time scale for Day/Week/Planner. |
| `ColumnMinWidth` | `double` | `80` | Minimum column width (px) in Day/Week. Narrower than this, the body scrolls horizontally. |
| `IsReadOnly` | `bool` | `false` | Disables move, resize, create, external drop and Delete. Click-select, tooltips and right-click still work. |
| `SingleClickCreates` | `bool` | `false` | Quick-add: a single click on an empty Day/Week slot raises `AppointmentCreateRequested` (normally a double-click). |
| `SelectedAppointment` | `AppointmentModel?` | `null` | The single selected appointment; null when zero or several are selected. Settable (set null to clear). |
| `SelectedAppointments` | `IReadOnlyList<AppointmentModel>?` | `null` | Full selection set. Read-only to the host. |
| `ShowTiles` | `bool` | `true` | Whether `AppointmentModel.Tiles` render. |
| `AppointmentCornerRadius` | `double` | `4` | Corner radius of timed blocks and the selection outline. |
| `AppointmentTemplate` | `DataTemplate?` | `null` | Day/Week block template. |
| `AppointmentChipTemplate` | `DataTemplate?` | `null` | Month cell and all-day banner chip template. |
| `AppointmentBarTemplate` | `DataTemplate?` | `null` | Planner bar template. |
| `AppointmentTooltipTemplate` | `DataTemplate?` | `null` | Hover popup template. |

`TimeIncrementConfig` has `MajorTick` (default 1 h, labelled lines), `MinorTick` (default 30 min,
gridlines and drag snap), `PixelsPerMinute` (default 0.8, so one hour is 48 px), plus the computed
`MinorSlotHeight`, `MajorSlotHeight` and `DayHeight`.

```csharp
Scheduler.CurrentView = SchedulerView.Day;
Scheduler.VisibleDays = 3;
Scheduler.SelectedDate = new DateTimeOffset(DateTime.Today);
Scheduler.TimeIncrement = new TimeIncrementConfig
{
    MajorTick = TimeSpan.FromHours(1),
    MinorTick = TimeSpan.FromMinutes(15),
    PixelsPerMinute = 1.2,
};

// Four Saturdays side by side
Scheduler.SelectedDates = Enumerable.Range(0, 4).Select(i => nextSaturday.AddDays(7 * i)).ToList();
```

```xml
<scheduler:SchedulerControl CurrentView="Planner"
                            GroupMode="ByResource"
                            FirstDayOfWeek="Monday"
                            HideIdleResources="True"
                            WorkDayStart="07:00:00"
                            WorkDayEnd="18:00:00"
                            ColumnMinWidth="120" />
```

The toolbar Settings flyout edits `GroupMode`, `HideIdleResources`, `TimeIncrement`,
`ColumnMinWidth`, `AppointmentCornerRadius`, `ShowTiles`, `FirstDayOfWeek` and the work hours, and
writes the values straight into these properties. Read them back when you want to persist the user's
choices.

---

## 4. Interaction events

All events are raised by `SchedulerControl`. None of them changes data.

### Move and resize

| Event | Args | Raised when |
|---|---|---|
| `AppointmentMoveRequested` | `AppointmentMoveArgs` | One appointment dragged to a new time and/or resource. |
| `AppointmentResizeRequested` | `AppointmentResizeArgs` | One appointment's top or bottom edge (left/right in the Planner) dragged. |
| `AppointmentsMoveRequested` | `AppointmentsMoveArgs` | A multi-selection dragged (Day/Week). Every item shares one time/lane delta. |
| `AppointmentsResizeRequested` | `AppointmentsResizeArgs` | A multi-selection resized (Day/Week). |

`AppointmentMoveArgs`: `Appointment`, `NewStart`, `NewEnd` (duration preserved), `FromResourceId`,
`ToResourceId`, `IsCopy` (Ctrl was held: duplicate instead of relocating), `WithShift` (Shift was held;
no built-in meaning, e.g. use it to skip a confirmation). `AppointmentResizeArgs`: `Appointment`,
`NewStart`, `NewEnd`, `WithShift`. The batch args carry `Items` (`AppointmentMoveItem` /
`AppointmentResizeItem`, each with its own `NewStart`/`NewEnd` and, for moves, resource ids) plus the
same flags.

Gestures: drags snap to `TimeIncrement.MinorTick`; holding Alt snaps to one minute. In Day/Week, resizing
works on the already-selected appointment only, so touching edges of two blocks are never ambiguous.

```csharp
_scheduler.AppointmentMoveRequested += (_, e) =>
{
    if (e.IsCopy) { /* insert a duplicate at e.NewStart instead */ return; }

    e.Appointment.Start = e.NewStart;
    e.Appointment.End = e.NewEnd;
    if (e.ToResourceId is not null && !Equals(e.FromResourceId, e.ToResourceId))
        e.Appointment.ResourceIds = e.Appointment.ResourceIds
            .Where(id => !Equals(id, e.FromResourceId))
            .Append(e.ToResourceId)
            .ToList();
    Refresh();
};

_scheduler.AppointmentResizeRequested += (_, e) =>
{
    e.Appointment.Start = e.NewStart;
    e.Appointment.End = e.NewEnd;
    Refresh();
};
```

In a Planner lane whose resource has a `TimeZoneId`, the `Appointment` in the args is a shifted copy,
not your instance. Resolve your record by `Appointment.Id` rather than by reference.

### Create

`AppointmentCreateRequested` (`AppointmentCreateArgs`: `Start`, `End`, `ResourceId`, `IsAllDay`,
`Position`) is raised by:

- dragging across empty time (Day/Week/Planner): a ranged create;
- double-clicking an empty Day/Week slot: a 60-minute create at that slot;
- a single click on an empty Day/Week slot when `SingleClickCreates` is true.

`EmptySlotSelected` (`EmptySlotSelectedArgs`: `Start`, `ResourceId`, `Position`) is raised on every
single click of an empty Day/Week slot, before any create. Use it to track "the selected resource" for
a New button, or to anchor a popup at `Position`.

```csharp
_scheduler.AppointmentCreateRequested += (_, e) =>
{
    _appointments.Add(new AppointmentModel
    {
        Id = Guid.NewGuid(), Subject = "New appointment",
        Start = e.Start, End = e.End, IsAllDay = e.IsAllDay,
        ResourceIds = e.ResourceId is { } r ? [r] : [],
    });
    Refresh();
};

_scheduler.EmptySlotSelected += (_, e) => _currentResourceId = e.ResourceId;
```

### External drag-drop

Items dragged in from outside the scheduler (an unscheduled queue, a customer list) implement
`ISchedulerDroppable` (`Subject`, `DefaultDuration`, `Payload`). The drag source puts the item into the
`DataPackage` with `ExternalDropHandler.SetDroppable`; dropping it on a Day, Week or Planner slot raises
`ExternalAppointmentDropped` (`ExternalDropArgs`: `Item`, `Start`, `End` = `Start + DefaultDuration`,
`ResourceId`). Drop is disabled when `IsReadOnly` is true. The payload is the live object, so this
works in-process only.

```csharp
using Xtreme.Components.Scheduler.Contracts;
using Xtreme.Components.Scheduler.Interaction;

public sealed class QueueItem : ISchedulerDroppable
{
    public string Subject { get; init; } = "";
    public TimeSpan DefaultDuration { get; init; } = TimeSpan.FromHours(1);
    public object? Payload { get; init; }   // e.g. your work-order id
}

var row = new Border { CanDrag = true, Child = new TextBlock { Text = item.Subject } };
row.DragStarting += (_, e) => ExternalDropHandler.SetDroppable(e.Data, item);

_scheduler.ExternalAppointmentDropped += (_, e) =>
{
    _appointments.Add(new AppointmentModel
    {
        Id = Guid.NewGuid(), Subject = e.Item.Subject, Start = e.Start, End = e.End,
        ResourceIds = e.ResourceId is { } r ? [r] : [], Tag = e.Item.Payload,
    });
    Refresh();
};
```

### Selection and delete

Selection is owned by the control (Day/Week click; right-click in any interactive view):

- click selects one appointment; Ctrl+click toggles it in or out of a multi-selection;
- locked appointments cannot be selected;
- `SelectionChanged` (`IReadOnlyList<AppointmentModel>`) fires on every change;
- `SelectedAppointment` is the single selection (null for none or several); setting it from code
  replaces the selection without stealing focus;
- `SelectedAppointments` is the full set.

Pressing Delete while the scheduler has focus, or calling `DeleteSelected(withShift)`, raises
`AppointmentsDeleteRequested` (`AppointmentsDeleteArgs`: `Appointments`, `WithShift`) for the
non-locked selected items and clears the selection. Nothing is removed until you remove it.

```csharp
_scheduler.AppointmentsDeleteRequested += async (_, e) =>
{
    if (!e.WithShift && !await ConfirmAsync($"Delete {e.Appointments.Count} appointment(s)?")) return;
    foreach (var a in e.Appointments) _appointments.Remove(a);
    Refresh();
};

deleteButton.Click += (_, _) => _scheduler.DeleteSelected();
```

---

## 5. Context menu

`AppointmentContextRequested` (`AppointmentContextRequestedArgs`: `Appointment`, `Position`) is raised
on right-click in Day and Week (also when `IsReadOnly` is true) and in the Planner (only when it is
interactive: not read-only, `GroupMode.ByResource`). It is raised for locked appointments too; any
other appointment is selected first. The control ships no menu: build any
`FlyoutBase` you like, with whatever items your domain needs.

`Position` is relative to the **window root** (`XamlRoot.Content`), not to the scheduler.
`ShowAt(target, options)` expects a position relative to `target`, so transform it first. This is the
gallery's code:

```csharp
using Microsoft.UI.Xaml.Controls.Primitives;

_scheduler.AppointmentContextRequested += (_, e) =>
{
    var menu = BuildContextMenu(e.Appointment);

    // Position is relative to the window root; ShowAt wants it relative to the target.
    if (e.Position is { } rootPos && _scheduler.XamlRoot?.Content is UIElement root)
    {
        var local = root.TransformToVisual(_scheduler).TransformPoint(rootPos);
        menu.ShowAt(_scheduler, new FlyoutShowOptions { Position = local, Placement = FlyoutPlacementMode.Right });
    }
    else
    {
        menu.ShowAt(_scheduler);
    }
};
```

The menu is rebuilt per request, so items can reflect the appointment's current state and be
disabled when they do not apply:

```csharp
private MenuFlyout BuildContextMenu(AppointmentModel a)
{
    var menu = new MenuFlyout();

    var edit = new MenuFlyoutItem { Text = "Edit…", Icon = new SymbolIcon(Symbol.Edit), IsEnabled = _editorEnabled };
    edit.Click += (_, _) => OpenEditor(a, isNew: false);
    menu.Items.Add(edit);

    var complete = new ToggleMenuFlyoutItem { Text = "Complete", IsChecked = a.IsComplete };
    complete.Click += (_, _) => { a.IsComplete = !a.IsComplete; Refresh(); };
    menu.Items.Add(complete);

    menu.Items.Add(new MenuFlyoutSeparator());

    var delete = new MenuFlyoutItem { Text = "Delete", Icon = new SymbolIcon(Symbol.Delete), IsEnabled = !a.IsLocked };
    delete.Click += (_, _) => { _appointments.Remove(a); Refresh(); };
    menu.Items.Add(delete);

    return menu;
}
```

`AppointmentCreateArgs.Position` and `EmptySlotSelectedArgs.Position` use the same root-relative
coordinates; apply the same transform to anchor a popup at an empty-slot click.

---

## 6. Appointment editor

The Scheduler has **no editor**. A double-click on an appointment raises `AppointmentEditRequested`
(`AppointmentEditRequestedArgs`: `Appointment`, `Handled`); set `Handled = true` when you open your
own. (The args' documentation mentions a default editor fallback; it is not implemented, so an
unhandled request does nothing.) Use any UI: a side pane, a `ContentDialog`, a separate page.

The gallery opens a side pane built from other packages in this repository: a `SectionCard`
(`Xtreme.Components.Cards`) holding text boxes, two `DateTimePicker`s and an `EditorActionBar`
(`Xtreme.Components.Primitives`). It opens the same form for an existing appointment
(`AppointmentEditRequested`) and for a new one (`AppointmentCreateRequested`), and adds the new item
only on Save. Trimmed:

```csharp
using Xtreme.Components.Cards;
using Xtreme.Components.Primitives;

_scheduler.AppointmentEditRequested += (_, e) =>
{
    e.Handled = true;
    OpenEditor(e.Appointment, isNew: false);
};

_scheduler.AppointmentCreateRequested += (_, e) =>
{
    var created = new AppointmentModel
    {
        Id = Guid.NewGuid(), Subject = "New appointment", Start = e.Start, End = e.End, IsAllDay = e.IsAllDay,
        ResourceIds = e.ResourceId is { } r ? [r] : [],
    };
    OpenEditor(created, isNew: true);   // added to the list on Save
};

private void BuildEditor()
{
    _subject = new TextBox { Header = "Subject" };
    _start = new DateTimePicker { Header = "Start", MinuteStep = 15 };
    _end = new DateTimePicker { Header = "End", MinuteStep = 15 };
    _actions = new EditorActionBar { DeleteConfirmTitle = "Delete appointment?" };

    _subject.TextChanged += (_, _) => UpdateDirty();
    _start.ValueChanged += (_, _) => UpdateDirty();
    _end.ValueChanged += (_, _) => UpdateDirty();
    _actions.SaveRequested += (_, _) => SaveEditor();
    _actions.CancelRequested += (_, _) => CloseEditor();
    _actions.DeleteRequested += (_, _) => { _appointments.Remove(_editing!); CloseEditor(); Refresh(); };

    var fields = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
    fields.Children.Add(_subject);
    fields.Children.Add(_start);
    fields.Children.Add(_end);
    fields.Children.Add(_actions);

    _editorPane.Child = new SectionCard { Body = new ScrollViewer { Content = fields } };
}

private void OpenEditor(AppointmentModel appointment, bool isNew)
{
    _editing = appointment;
    _editingIsNew = isNew;
    _subject.Text = appointment.Subject;
    _start.Date = DateOnly.FromDateTime(appointment.Start); _start.Time = TimeOnly.FromDateTime(appointment.Start);
    _end.Date = DateOnly.FromDateTime(appointment.End);     _end.Time = TimeOnly.FromDateTime(appointment.End);
    _actions.ShowDelete = !isNew;
    _editorBaseline = Signature();
    _actions.IsDirty = isNew;               // Save stays disabled until something changes
    _editorPane.Visibility = Visibility.Visible;
}

private void SaveEditor()
{
    var start = _start.Date!.Value.ToDateTime(_start.Time ?? TimeOnly.MinValue);
    var end = _end.Date!.Value.ToDateTime(_end.Time ?? TimeOnly.MinValue);
    if (end <= start) end = start.AddMinutes(30);

    _editing!.Subject = _subject.Text.Trim();
    _editing.Start = start;
    _editing.End = end;
    if (_editingIsNew) _appointments.Add(_editing);
    CloseEditor();
    Refresh();
}
```

To keep the editor in step with the selection, handle `SelectionChanged` and close or retarget the
form when the selection is empty or has more than one item. Set `SelectedAppointment = null` when the
editor closes if you want the highlight cleared.

---

## 7. Tiles

Tiles are small data labels drawn in the right-hand gutter of a block (and repeated in the default
tooltip). They come from `AppointmentModel.Tiles`, a list of `AppointmentDecoration`. The host
computes them; the control only draws them. A tile renders its `Image` if set, otherwise its `Text`
as a pill using `ForeColor`, `FontSize` and `Bold`. Tiles are laid out as a strip; `Position`,
`Alignment`, offsets and `Glyph` do not affect where or how a tile is drawn in the block.

`ShowTiles` (default `true`, also a switch in the Settings flyout) hides or shows all tiles.

`SetTileOptions(IReadOnlyList<SchedulerTileOption>)` adds a "Visible tiles" toolbar button listing one
toggle per option (`Key`, `Label`, `IsOn`); an empty list hides the button. Toggling raises
`TileOptionToggled` with a new `SchedulerTileOption` carrying the new `IsOn`. The control does not
filter tiles itself: rebuild `Tiles` from your enabled keys and re-assign `Appointments`.

Tile data does not have to live on the appointment. The gallery's "Customer balance" tile is looked up
from separate accounts data, joined by the appointment id:

```csharp
private static readonly Dictionary<int, decimal> CustomerBalances = new() { [2] = 1250.00m, [3] = 89.95m, /* ... */ };
private readonly HashSet<string> _tileKeys = new() { "status", "balance" };

_scheduler.TileOptionToggled += (_, option) =>
{
    if (option.IsOn) _tileKeys.Add(option.Key); else _tileKeys.Remove(option.Key);
    Refresh();
};

private void Refresh()
{
    foreach (var a in _appointments)
        a.Tiles = Tiles(a.Id is int i ? i : 0).ToList();

    _scheduler.ShowTiles = true;
    _scheduler.SetTileOptions(
    [
        new SchedulerTileOption { Key = "status",  Label = "Status",           IsOn = _tileKeys.Contains("status") },
        new SchedulerTileOption { Key = "balance", Label = "Customer balance", IsOn = _tileKeys.Contains("balance") },
    ]);
    _scheduler.Appointments = _appointments.ToList();
}

private IEnumerable<AppointmentDecoration> Tiles(int id)
{
    if (_tileKeys.Contains("status"))
        yield return new AppointmentDecoration { Text = id % 2 == 0 ? "Scheduled" : "Confirmed", Tooltip = "Status tile" };

    if (_tileKeys.Contains("balance") && CustomerBalances.TryGetValue(id, out var due))
        yield return new AppointmentDecoration
        {
            Text = due > 0 ? $"{due:C0} due" : "Paid up",
            Bold = due > 0,
            ForeColor = due > 0 ? Microsoft.UI.Colors.Firebrick : Microsoft.UI.Colors.DarkGreen,
            Tooltip = "Customer balance, looked up by event id",
        };
}
```

---

## 8. Icons and adornments

### Built-in status glyphs and indicators

The default templates draw an indicator strip on each block. It always starts with the built-in
glyphs for `IsRecurring`, `IsLocked`, `IsComplete`, `IsConfirmed`, `IsCancelled` and `IsTentative`,
followed by `AppointmentModel.Indicators`:

| `AppointmentIndicator` | Type | Notes |
|---|---|---|
| `Glyph` | `string` | Symbol-font code point, e.g. `""`. |
| `Color` | `Color?` | Null = the block's contrast foreground. |
| `Tooltip` | `string?` | Hover text. |
| `FontFamily` | `FontFamily?` | Null = the symbol theme font. |

```csharp
a.RecurrenceRule = "FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR";   // recurrence glyph
a.IsTentative = true;                                   // question-mark glyph
a.Indicators = [new AppointmentIndicator { Glyph = "", Tooltip = "Site photos attached", Color = Microsoft.UI.Colors.SteelBlue }];
```

### Adornments

`AppointmentModel.Adornments` are badges overlaid on the block at a position. Each is an
`AppointmentDecoration`, rendered in priority order `Image`, then `Glyph` (`GlyphFontFamily`), then
`Text`, and anchored by:

- `Position` (`DecorationPosition`): `Left`, `Top`, `Right`, `Bottom`, `FloatLeft` (default),
  `FloatCenter`, `FloatRight`. All values currently overlay; edge positions do not reserve space.
- `Alignment` (`DecorationAlignment`): `Near` (default), `Middle`, `Far` along that edge.
- `OffsetX` / `OffsetY`: pixel nudges.
- Styling: `BackColor`, `ForeColor`, `BorderColor`, `FontSize` (default 10), `Bold`, `Tooltip`.

Adornments always render (they are not affected by `ShowTiles`). In Day/Week they may extend past the
block edge; in the other views they are clipped to the block.

```csharp
a.Adornments.Add(new AppointmentDecoration
{
    Text = "URGENT", Tooltip = "urgent",
    Position = DecorationPosition.Top, Alignment = DecorationAlignment.Far,
    BackColor = Microsoft.UI.Colors.Firebrick, ForeColor = Microsoft.UI.Colors.White,
    Bold = true, FontSize = 9,
});
```

### Toolbar adornment buttons

`ToolbarAdornments` is an `ObservableCollection<SchedulerToolbarButton>` rendered as its own toolbar
group (a row of buttons when wide, an "Adornments" drop-down when narrow). Clicking one raises
`AdornmentToggleRequested` with the button; the host decides what it means. The gallery toggles the
named adornment on the selected appointment:

```csharp
_scheduler.ToolbarAdornments.Add(new SchedulerToolbarButton { Id = "urgent", Glyph = "", Label = "Urgent", Tooltip = "Toggle URGENT on the selection" });
_scheduler.ToolbarAdornments.Add(new SchedulerToolbarButton { Id = "paid",   Glyph = "", Label = "Paid",   Tooltip = "Toggle PAID on the selection" });

_scheduler.AdornmentToggleRequested += (_, button) =>
{
    var target = _scheduler.SelectedAppointment;
    if (target is null) return;
    var existing = target.Adornments.FirstOrDefault(a => a.Tooltip == button.Id);
    if (existing is not null) target.Adornments.Remove(existing);
    else target.Adornments.Add(Adornment(button.Id!));
    Refresh();
};
```

The adornment group uses `Id`, `Glyph`, `Image`, `Label`, `Tooltip` and `IsEnabled`. `IsToggle`,
`IsChecked`, `Command` and `MenuItems` have no effect there; they apply to `ToolbarButtons`
([section 11](#11-toolbar-and-the-view-controls)).

---

## 9. Tooltips

Every appointment has a hover popup. The default one shows the subject (with the status glyphs), the
date, the time range with duration, then, when present, `Details` rows (`Label: Value`, rows with a
blank value are skipped), the `Description`, the `Memo` under "Internal memo", and the tiles and
adornments. In Day/Week the popup is driven by the pointer overlay and clamped to the visible area;
in the other views it is a standard `ToolTip`.

Override it, in priority order:

1. `AppointmentTooltipRenderer` (`Func<AppointmentContext, FrameworkElement?>`, code only): return an
   element, or `null` to fall through for that appointment.
2. `AppointmentTooltipTemplate` (`DataTemplate`, DataContext = `AppointmentContext`).
3. The default popup.

```csharp
a.Details = [new AppointmentDetail("Customer", "Rivera, Alex"), new AppointmentDetail("Phone", "555-010-2030")];
a.Description = "Check the water softener install went in clean.";

_scheduler.AppointmentTooltipRenderer = ctx =>
{
    if (ctx.Appointment.Id is not 2) return null;   // everything else keeps the default popup
    var panel = new StackPanel { Spacing = 4, Padding = new Thickness(4) };
    panel.Children.Add(new TextBlock { Text = ctx.Subject, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
    panel.Children.Add(new TextBlock { Text = ctx.TimeRange, Opacity = 0.75 });
    return panel;
};
```

```xml
<scheduler:SchedulerControl.AppointmentTooltipTemplate>
    <DataTemplate>
        <StackPanel Spacing="2">
            <TextBlock Text="{Binding Subject}" FontWeight="SemiBold" />
            <TextBlock Text="{Binding TimeRange}" Opacity="0.75" />
            <TextBlock Text="{Binding Note}" TextWrapping="Wrap" />
        </StackPanel>
    </DataTemplate>
</scheduler:SchedulerControl.AppointmentTooltipTemplate>
```

---

## 10. Templates and appearance

Each block shape has its own template, and all of them bind to an `AppointmentContext`:

| Surface (`AppointmentSurface`) | Where | Template property |
|---|---|---|
| `Timed` | Day/Week body | `AppointmentTemplate` |
| `Chip` | Month cells, all-day banner | `AppointmentChipTemplate` |
| `Bar` | Planner lanes | `AppointmentBarTemplate` |

Resolution order per block: `AppointmentRenderer` (code; switch on `ctx.Surface`, return `null` to
fall through), then the surface's template, then the built-in default. Custom content is wrapped in
the standard chrome (fill, border, corner radius), so hit-testing, selection and drag still work.

`AppointmentContext` exposes ready-to-bind values so templates need no converters:

- Brushes: `BackBrush` (from `BackColor` or the theme), `BarBrush` (from `BarColor`), `Foreground`
  (auto-contrast against the fill), `BorderBrush`.
- Text: `Subject`, `Note` (= `Description`), `Memo`, `StartTime` ("9:00 AM"), `TimeRange`
  ("9:00 AM – 10:00 AM" or "All day"), `CompactStart` ("9a"), `CompactTimeRange` ("9-10:30a").
- Flags: `IsAllDay`, `IsRecurring`, `IsLocked`, `IsComplete`, `IsCancelled`, `IsTentative`,
  `IsConfirmed`, `ShowTime`, `ShowTiles`.
- Collections and data: `Indicators`, `Details`, `Tiles`, `Adornments`, `Tag`, `Appointment`,
  `Start`, `End`, `Surface`, `CornerRadius`.

```xml
<scheduler:SchedulerControl x:Name="Scheduler" AppointmentCornerRadius="6">
    <scheduler:SchedulerControl.AppointmentTemplate>
        <DataTemplate>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="4" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Border Background="{Binding BarBrush}" />
                <StackPanel Grid.Column="1" Padding="6,2">
                    <TextBlock Text="{Binding Subject}" Foreground="{Binding Foreground}" FontWeight="SemiBold" />
                    <TextBlock Text="{Binding CompactTimeRange}" Foreground="{Binding Foreground}" FontSize="11" />
                </StackPanel>
            </Grid>
        </DataTemplate>
    </scheduler:SchedulerControl.AppointmentTemplate>
</scheduler:SchedulerControl>
```

```csharp
_scheduler.AppointmentRenderer = ctx => ctx.Surface switch
{
    AppointmentSurface.Bar => new TextBlock { Text = ctx.Subject, Foreground = ctx.Foreground, Margin = new Thickness(6, 0, 0, 0) },
    _ => null,   // keep the defaults for timed blocks and chips
};
```

### Colours

- Per appointment: `BarColor` and `BackColor`. The gallery colours by appointment type:

  ```csharp
  foreach (var a in _appointments)
  {
      var (bar, back) = TypeColours(a.Subject);
      a.BarColor = bar;
      a.BackColor = back;
  }
  ```

- Per resource: `ResourceModel.Color` for the header chip.
- Grid, headers, selection and default event colours come from the scheduler palette, overridable with
  `XcScheduler<Role>Brush` resources per theme. See
  [theming.md, Scheduler roles](theming.md#scheduler-roles-xcschedulerrolebrush-optional).
- `AppointmentCornerRadius` sets the radius of timed blocks and the selection outline (chips and bars
  use a fixed radius).
- Compact time strings come from `SchedulerTimeFormatter.RangeFormatter`, a static
  `Func<DateTime, DateTime, string>` you can replace (for example with a 24-hour format). It is
  process-wide and affects every scheduler instance.

  ```csharp
  SchedulerTimeFormatter.RangeFormatter = (s, e) => $"{s:HH:mm}-{e:HH:mm}";
  ```

---

## 11. Toolbar and the view controls

`SchedulerControl` contains its own `SchedulerToolbar`: Previous / Today / Next, a period button that
opens a multi-date picker (click, Ctrl-click, Shift-click or range mode; a scattered pick sets
`SelectedDates`), the view switcher, host buttons, the adornment group, the "Visible tiles" button and
the Settings flyout. The embedded toolbar instance is not exposed; customise it through the members
below.

### Custom toolbar buttons

`ToolbarButtons` (`ObservableCollection<SchedulerToolbarButton>`) adds buttons between the view
switcher and Settings. A click runs the button's `Command` (with `CommandParameter`) if set, then raises
`ToolbarButtonClicked`.

| `SchedulerToolbarButton` | Notes |
|---|---|
| `Id` | Your identifier; not shown. |
| `Glyph` / `Image` | Icon. `Image` (raw bytes) wins over `Glyph`. |
| `Label` | Caption beside or instead of the icon. |
| `Tooltip` | Hover text; recommended for icon-only buttons. |
| `Command`, `CommandParameter` | Optional command. |
| `IsEnabled` | Default `true`. |
| `IsToggle`, `IsChecked` | Shows a pressed (accent) state. The host owns the state; properties are `init`-only, so replace the button in the collection to change it. |
| `MenuItems` | Turns the button into a drop-down; picking an entry raises `ToolbarButtonClicked` with that entry. |

```csharp
_scheduler.ToolbarButtons.Add(new SchedulerToolbarButton { Id = "map", Glyph = "", Tooltip = "Show map" });
_scheduler.ToolbarButtons.Add(new SchedulerToolbarButton
{
    Id = "export", Label = "Export",
    MenuItems = [new() { Id = "csv", Label = "CSV" }, new() { Id = "ics", Label = "iCalendar" }],
});

_scheduler.ToolbarButtonClicked += (_, b) =>
{
    switch (b.Id)
    {
        case "map": ToggleMap(); break;
        case "csv": ExportCsv(); break;
        case "ics": ExportIcs(); break;
    }
};
```

### Standalone use

`SchedulerToolbar` is public and can be placed on its own: it exposes `Set*` methods to reflect state
(`SetActiveView`, `SetPeriodLabel`, `SetCurrentDates`, `SetGroupMode`, `SetHideIdle`, `SetShowTiles`,
`SetTileOptions`, `SetTimeIncrement`, `SetColumnMinWidth`, `SetCornerRadius`, `SetFirstDayOfWeek`,
`SetWorkHours`, `SetResourceOptionsVisible`, `SetCustomButtons`, `SetAdornmentButtons`) and events for
each user action. Most applications should use the toolbar built into `SchedulerControl`.

`TimeGridView`, `PlannerView`, `MonthView` and `AgendaView` are public classes with public events, but
their data-loading method is internal, so they cannot be populated outside `SchedulerControl`. Treat
them as implementation detail and use `SchedulerControl` with `CurrentView`.

---

## 12. Member reference

Generated from `PublicAPI.Unshipped.txt`. The `*Property` dependency-property identifiers exist for
every DP listed and are omitted.

### SchedulerControl properties

| Member | Type | Access |
|---|---|---|
| `Appointments` | `IReadOnlyList<AppointmentModel>?` | get/set |
| `SchedulerResources` | `IReadOnlyList<ResourceModel>?` | get/set |
| `CurrentView` | `SchedulerView` | get/set |
| `SelectedDate` | `DateTimeOffset` | get/set |
| `SelectedDates` | `IReadOnlyList<DateTime>?` | get/set |
| `VisibleDays` | `int` | get/set |
| `FirstDayOfWeek` | `DayOfWeek` | get/set |
| `GroupMode` | `SchedulerGroupMode` | get/set |
| `HideIdleResources` | `bool` | get/set |
| `WorkDayStart`, `WorkDayEnd` | `TimeSpan` | get/set |
| `TimeIncrement` | `TimeIncrementConfig` | get/set |
| `ColumnMinWidth` | `double` | get/set |
| `IsReadOnly` | `bool` | get/set |
| `SingleClickCreates` | `bool` | get/set |
| `SelectedAppointment` | `AppointmentModel?` | get/set |
| `SelectedAppointments` | `IReadOnlyList<AppointmentModel>?` | get |
| `ShowTiles` | `bool` | get/set |
| `AppointmentCornerRadius` | `double` | get/set |
| `AppointmentTemplate`, `AppointmentChipTemplate`, `AppointmentBarTemplate`, `AppointmentTooltipTemplate` | `DataTemplate?` | get/set |
| `AppointmentRenderer`, `AppointmentTooltipRenderer` | `Func<AppointmentContext, FrameworkElement?>?` | get/set (not DPs) |
| `ToolbarButtons` | `ObservableCollection<SchedulerToolbarButton>` | get |
| `ToolbarAdornments` | `ObservableCollection<SchedulerToolbarButton>` | get |

### SchedulerControl events

| Event | Args |
|---|---|
| `AppointmentMoveRequested` | `AppointmentMoveArgs` |
| `AppointmentResizeRequested` | `AppointmentResizeArgs` |
| `AppointmentsMoveRequested` | `AppointmentsMoveArgs` |
| `AppointmentsResizeRequested` | `AppointmentsResizeArgs` |
| `AppointmentCreateRequested` | `AppointmentCreateArgs` |
| `EmptySlotSelected` | `EmptySlotSelectedArgs` |
| `ExternalAppointmentDropped` | `ExternalDropArgs` |
| `AppointmentEditRequested` | `AppointmentEditRequestedArgs` |
| `AppointmentContextRequested` | `AppointmentContextRequestedArgs` |
| `SelectionChanged` | `IReadOnlyList<AppointmentModel>` |
| `AppointmentsDeleteRequested` | `AppointmentsDeleteArgs` |
| `ToolbarButtonClicked` | `SchedulerToolbarButton` |
| `AdornmentToggleRequested` | `SchedulerToolbarButton` |
| `TileOptionToggled` | `SchedulerTileOption` |

### SchedulerControl methods

| Method | Purpose |
|---|---|
| `DeleteSelected(bool withShift = false)` | Raise `AppointmentsDeleteRequested` for the non-locked selection and clear it. No-op when read-only or nothing is selected. |
| `SetTileOptions(IReadOnlyList<SchedulerTileOption> options)` | Populate the "Visible tiles" toolbar menu; empty hides it. |

### Event argument types (`Contracts`)

| Type | Members |
|---|---|
| `AppointmentMoveArgs` | `Appointment`, `NewStart`, `NewEnd`, `FromResourceId`, `ToResourceId`, `IsCopy`, `WithShift` |
| `AppointmentResizeArgs` | `Appointment`, `NewStart`, `NewEnd`, `WithShift` |
| `AppointmentsMoveArgs` | `Items` (`AppointmentMoveItem`: `Appointment`, `NewStart`, `NewEnd`, `FromResourceId`, `ToResourceId`), `IsCopy`, `WithShift` |
| `AppointmentsResizeArgs` | `Items` (`AppointmentResizeItem`: `Appointment`, `NewStart`, `NewEnd`), `WithShift` |
| `AppointmentsDeleteArgs` | `Appointments`, `WithShift` |
| `AppointmentCreateArgs` | `Start`, `End`, `ResourceId`, `IsAllDay`, `Position` |
| `EmptySlotSelectedArgs` | `Start`, `ResourceId`, `Position` |
| `ExternalDropArgs` | `Item`, `Start`, `End`, `ResourceId` |
| `AppointmentEditRequestedArgs` | `Appointment`, `Handled` (settable) |
| `AppointmentContextRequestedArgs` | `Appointment`, `Position` |
| `AppointmentClickArgs` | `Appointment`, `WithCtrl` (raised by `TimeGridView`; `SchedulerControl` consumes it) |
| `ISchedulerDroppable` | `Subject`, `DefaultDuration`, `Payload` |

`Position` values are `Windows.Foundation.Point?` relative to `XamlRoot.Content`.

### Models

| Type | Members |
|---|---|
| `AppointmentModel` | `Id`, `Subject`, `Description`, `Memo`, `Start`, `End`, `IsAllDay`, `ResourceIds`, `BarColor`, `BackColor`, `RecurrenceRule`, `RecurrenceException`, `IsRecurring`, `Duration`, `IsLocked`, `IsComplete`, `IsConfirmed`, `IsCancelled`, `IsTentative`, `Indicators`, `Details`, `Tiles`, `Adornments`, `Tag` |
| `ResourceModel` | `Id`, `Name`, `Color`, `TimeZoneId` |
| `AppointmentIndicator` | `Glyph`, `Color`, `Tooltip`, `FontFamily` |
| `AppointmentDetail` | `Label`, `Value`; constructors `()` and `(label, value)` |
| `AppointmentDecoration` | `Text`, `Glyph`, `GlyphFontFamily`, `Image`, `Position`, `Alignment`, `OffsetX`, `OffsetY`, `BackColor`, `ForeColor`, `BorderColor`, `FontSize`, `Bold`, `Tooltip` |
| `DecorationPosition` | `Left`, `Top`, `Right`, `Bottom`, `FloatLeft`, `FloatCenter`, `FloatRight` |
| `DecorationAlignment` | `Near`, `Middle`, `Far` |
| `SchedulerView` | `Day`, `Week`, `Month`, `Agenda`, `Planner` |
| `SchedulerGroupMode` | `ByResource`, `ByDay` |
| `TimeIncrementConfig` | `MajorTick`, `MinorTick`, `PixelsPerMinute`, `MinorSlotHeight`, `MajorSlotHeight`, `DayHeight`, static `Default` |
| `SchedulerTileOption` | `Key`, `Label`, `IsOn` (init-only) |
| `SchedulerToolbarButton` | `Id`, `Glyph`, `Image`, `Label`, `Tooltip`, `Command`, `CommandParameter`, `IsEnabled`, `IsToggle`, `IsChecked`, `MenuItems` (init-only) |

### Rendering, interaction and utilities

| Type | Members |
|---|---|
| `AppointmentContext` | `Appointment`, `Surface`, `CornerRadius`, `ShowTime`, `ShowTiles`, `BackBrush`, `BarBrush`, `Foreground`, `BorderBrush`, `Subject`, `Note`, `Memo`, `Start`, `End`, `IsAllDay`, `IsRecurring`, `IsLocked`, `IsComplete`, `IsCancelled`, `IsTentative`, `IsConfirmed`, `Indicators`, `Details`, `Adornments`, `Tiles`, `Tag`, `StartTime`, `TimeRange`, `CompactStart`, `CompactTimeRange` |
| `AppointmentSurface` | `Timed`, `Chip`, `Bar` |
| `ExternalDropHandler` | `DataKey` (const), `SetDroppable(DataPackage, ISchedulerDroppable)` |
| `SchedulerTimeFormatter` | `RangeFormatter` (settable), `Compact(DateTime)`, `CompactRange(DateTime, DateTime)` |
| `TimeZoneConverter` | `ToZone(DateTime utc, string? ianaId)`, `FromZone(DateTime zoned, string? ianaId)`, `ShortLabel(string? ianaId)` |
| `EventBlockLayout` / `EventLayout` | Pure overlap-packing used by the views: `Layout(day, appointments, minVisibleMinutes)`, `LayoutRange(...)`, `DefaultMinVisibleMinutes`. Usable for custom renderers or tests. |

Also public but internal in spirit (rendering building blocks, not needed by consumers):
`DayColumnPanel`, `TimelineLanePanel`, `AllDayBanner`, `ResizeGrip`, and the view controls described in
[section 11](#11-toolbar-and-the-view-controls).
