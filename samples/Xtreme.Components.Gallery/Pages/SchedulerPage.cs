using Xtreme.Components.Scheduler.Controls;
using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Gallery.Pages;

/// <summary>
/// A live scheduler over a hand-written week. Every user edit arrives as an event; the page applies
/// it to its own list and re-assigns <see cref="SchedulerControl.Appointments"/> so the view re-renders.
/// The control never mutates the data itself.
/// </summary>
public sealed partial class SchedulerPage : Grid
{
    private readonly SchedulerControl _scheduler = new();
    private readonly List<AppointmentModel> _appointments;
    private readonly TextBlock _log = new() { Opacity = 0.75, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 6, 0, 0) };

    public SchedulerPage()
    {
        var monday = DateTime.Today.AddDays(-((7 + (int)DateTime.Today.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        var resources = new[]
        {
            new ResourceModel { Id = 1, Name = "Alex",   Color = Microsoft.UI.Colors.RoyalBlue },
            new ResourceModel { Id = 2, Name = "Bailey", Color = Microsoft.UI.Colors.SeaGreen, TimeZoneId = "America/New_York" },
            new ResourceModel { Id = 3, Name = "Casey",  Color = Microsoft.UI.Colors.DarkOrange },
        };
        _appointments =
        [
            Appt(1, "Site survey",      monday.AddHours(9),               2,   1),
            Appt(2, "Install",          monday.AddDays(1).AddHours(13),   3,   1),
            Appt(3, "Battery swap",     monday.AddDays(1).AddHours(10),   1,   2),
            Appt(4, "Training",         monday.AddDays(2).AddHours(14),   1.5, 2),
            Appt(5, "Follow-up call",   monday.AddDays(3).AddHours(11),   0.5, 3),
            Appt(6, "Team meeting",     monday.AddDays(4).AddHours(9),    1,   1, 2, 3),
            new() { Id = 7, Subject = "Trade show", Start = monday.AddDays(3), End = monday.AddDays(5), IsAllDay = true, ResourceIds = { 3 } },
        ];

        _scheduler.SchedulerResources = resources;
        _scheduler.Appointments = _appointments.ToList();
        _scheduler.CurrentView = SchedulerView.Week;
        _scheduler.SelectedDate = new DateTimeOffset(monday);

        _scheduler.AppointmentCreateRequested += (_, e) =>
        {
            _appointments.Add(new AppointmentModel
            {
                Id = Guid.NewGuid(), Subject = "New appointment", Start = e.Start, End = e.End,
                ResourceIds = e.ResourceId is { } r ? [r] : [],
            });
            Log($"Create {e.Start:ddd h:mm tt} to {e.End:h:mm tt}");
            Refresh();
        };
        _scheduler.AppointmentMoveRequested += (_, e) =>
        {
            e.Appointment.Start = e.NewStart;
            e.Appointment.End = e.NewEnd;
            if (e.ToResourceId is not null && !Equals(e.FromResourceId, e.ToResourceId))
                e.Appointment.ResourceIds = e.Appointment.ResourceIds.Where(id => !Equals(id, e.FromResourceId)).Append(e.ToResourceId).ToList();
            Log($"Move '{e.Appointment.Subject}' to {e.NewStart:ddd h:mm tt}");
            Refresh();
        };
        _scheduler.AppointmentResizeRequested += (_, e) =>
        {
            e.Appointment.Start = e.NewStart;
            e.Appointment.End = e.NewEnd;
            Log($"Resize '{e.Appointment.Subject}' to {e.NewStart:h:mm tt} - {e.NewEnd:h:mm tt}");
            Refresh();
        };

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetRow(_log, 1);
        Children.Add(_scheduler);
        Children.Add(_log);
    }

    private static AppointmentModel Appt(int id, string subject, DateTime start, double hours, params int[] resources)
        => new() { Id = id, Subject = subject, Start = start, End = start.AddHours(hours), ResourceIds = resources.Cast<object>().ToList() };

    private void Refresh() => _scheduler.Appointments = _appointments.ToList();
    private void Log(string text) => _log.Text = text;
}
