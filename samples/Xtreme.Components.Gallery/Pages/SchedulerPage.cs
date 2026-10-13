using Xtreme.Components.Scheduler.Controls;
using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Gallery.Pages;

/// <summary>
/// A live scheduler over a hand-written week, with a row of example switches above it:
/// <list type="bullet">
/// <item><b>Resource picker</b>: a host-side list of checkboxes beside the scheduler that filters
/// <see cref="SchedulerControl.SchedulerResources"/>. The library ships no picker on purpose; this is
/// the pattern an app follows.</item>
/// <item><b>Tiles</b>: data-driven <see cref="AppointmentModel.Tiles"/> plus the toolbar's tile
/// options (<see cref="SchedulerControl.SetTileOptions"/> / <see cref="SchedulerControl.TileOptionToggled"/>)
/// and <see cref="SchedulerControl.ShowTiles"/>.</item>
/// <item><b>Icons</b>: <see cref="AppointmentModel.Indicators"/>, the small glyph row on a block.</item>
/// <item><b>Adornments</b>: positioned <see cref="AppointmentModel.Adornments"/> and the toolbar's
/// adornment toggle group (<see cref="SchedulerControl.ToolbarAdornments"/> /
/// <see cref="SchedulerControl.AdornmentToggleRequested"/>) acting on the selected appointment.</item>
/// </list>
/// Every user edit still arrives as an event; the page applies it and re-assigns the list.
/// </summary>
public sealed partial class SchedulerPage : Grid
{
    private readonly SchedulerControl _scheduler = new();
    private readonly List<AppointmentModel> _appointments;
    private readonly List<ResourceModel> _resources;
    private readonly HashSet<object> _hiddenResources = new();
    private readonly StackPanel _resourcePane = new() { Spacing = 6, Margin = new Thickness(0, 0, 16, 0), MinWidth = 150, Visibility = Visibility.Collapsed };
    private readonly TextBlock _log = new() { Opacity = 0.75, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 6, 0, 0) };

    private bool _tiles, _icons, _adornments;
    private readonly HashSet<string> _tileKeys = new() { "status", "tech" };

    public SchedulerPage()
    {
        var monday = DateTime.Today.AddDays(-((7 + (int)DateTime.Today.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        _resources =
        [
            new ResourceModel { Id = 1, Name = "Alex",   Color = Microsoft.UI.Colors.RoyalBlue },
            new ResourceModel { Id = 2, Name = "Bailey", Color = Microsoft.UI.Colors.SeaGreen, TimeZoneId = "America/New_York" },
            new ResourceModel { Id = 3, Name = "Casey",  Color = Microsoft.UI.Colors.DarkOrange },
        ];
        _appointments =
        [
            Appt(1, "Site survey",    monday.AddHours(9),               2,   1),
            Appt(2, "Install",        monday.AddDays(1).AddHours(13),   3,   1),
            Appt(3, "Battery swap",   monday.AddDays(1).AddHours(10),   1,   2),
            Appt(4, "Training",       monday.AddDays(2).AddHours(14),   1.5, 2),
            Appt(5, "Follow-up call", monday.AddDays(3).AddHours(11),   0.5, 3),
            Appt(6, "Team meeting",   monday.AddDays(4).AddHours(9),    1,   1, 2, 3),
            new() { Id = 7, Subject = "Trade show", Start = monday.AddDays(3), End = monday.AddDays(5), IsAllDay = true, ResourceIds = { 3 } },
        ];

        _scheduler.CurrentView = SchedulerView.Week;
        _scheduler.SelectedDate = new DateTimeOffset(monday);

        // The toolbar's adornment toggle group: each button names a template; the handler applies or
        // removes that adornment on whatever is selected.
        _scheduler.ToolbarAdornments.Add(new SchedulerToolbarButton { Id = "urgent", Glyph = "", Label = "Urgent", Tooltip = "Toggle the URGENT ribbon on the selected appointment", IsToggle = true });
        _scheduler.ToolbarAdornments.Add(new SchedulerToolbarButton { Id = "paid", Glyph = "", Label = "Paid", Tooltip = "Toggle the PAID stamp on the selected appointment", IsToggle = true });
        _scheduler.AdornmentToggleRequested += (_, button) =>
        {
            var target = _scheduler.SelectedAppointment ?? _appointments[0];
            var existing = target.Adornments.FirstOrDefault(a => Equals(a.Tooltip, button.Id));
            if (existing is not null) target.Adornments.Remove(existing);
            else target.Adornments.Add(Adornment(button.Id!));
            Log($"{(existing is null ? "Added" : "Removed")} '{button.Label}' on '{target.Subject}'");
            Refresh();
        };

        _scheduler.TileOptionToggled += (_, option) =>
        {
            if (option.IsOn) _tileKeys.Add(option.Key); else _tileKeys.Remove(option.Key);
            Log($"Tile option '{option.Label}' {(option.IsOn ? "on" : "off")}");
            Refresh();
        };

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

        // Example switches
        var switches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 0, 0, 8) };
        switches.Children.Add(new TextBlock { Text = "Examples:", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 });
        switches.Children.Add(Switch("Resource picker", on => { _resourcePane.Visibility = on ? Visibility.Visible : Visibility.Collapsed; if (!on) { _hiddenResources.Clear(); foreach (var cb in _resourcePane.Children.OfType<CheckBox>()) cb.IsChecked = true; } Refresh(); }));
        switches.Children.Add(Switch("Tiles", on => { _tiles = on; Refresh(); }));
        switches.Children.Add(Switch("Icons", on => { _icons = on; Refresh(); }));
        switches.Children.Add(Switch("Adornments", on => { _adornments = on; Refresh(); }));

        // Resource picker: plain checkboxes, one per resource, filtering what the scheduler shows.
        _resourcePane.Children.Add(new TextBlock { Text = "Resources", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        foreach (var resource in _resources)
        {
            var box = new CheckBox { Content = resource.Name, IsChecked = true, Tag = resource.Id };
            box.Checked += (_, _) => { _hiddenResources.Remove(resource.Id); Refresh(); };
            box.Unchecked += (_, _) => { _hiddenResources.Add(resource.Id); Refresh(); };
            _resourcePane.Children.Add(box);
        }
        _resourcePane.Children.Add(new TextBlock { Text = "The library ships no picker; the host filters SchedulerResources.", Opacity = 0.6, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetColumn(_scheduler, 1);
        body.Children.Add(_resourcePane);
        body.Children.Add(_scheduler);

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetRow(body, 1);
        SetRow(_log, 2);
        Children.Add(switches);
        Children.Add(body);
        Children.Add(_log);

        Refresh();
    }

    private readonly List<CheckBox> _switches = new();

    /// <summary>Turns every example switch on (the self-test uses it to render the decorated path).</summary>
    public void EnableAllExamples()
    {
        foreach (var box in _switches) box.IsChecked = true;
    }

    private CheckBox Switch(string label, Action<bool> changed)
    {
        var box = new CheckBox { Content = label, MinWidth = 0 };
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        _switches.Add(box);
        return box;
    }

    private static AppointmentModel Appt(int id, string subject, DateTime start, double hours, params int[] resources)
        => new() { Id = id, Subject = subject, Start = start, End = start.AddHours(hours), ResourceIds = resources.Cast<object>().ToList() };

    /// <summary>Re-applies the example switches to the data and re-assigns both lists so the view re-renders.</summary>
    private void Refresh()
    {
        foreach (var a in _appointments)
        {
            var id = a.Id is int i ? i : 0;

            // Icons: the small glyph row inside a block.
            a.Indicators = _icons
                ? id switch
                {
                    1 => [Icon("", "Site photos attached", Microsoft.UI.Colors.SteelBlue)],
                    2 => [Icon("", "Parts on back-order", Microsoft.UI.Colors.DarkOrange), Icon("", "Customer present", null)],
                    3 => [Icon("", "Confirmed", Microsoft.UI.Colors.SeaGreen)],
                    5 => [Icon("", "Call", null)],
                    6 => [Icon("", "All hands", null)],
                    _ => [],
                }
                : [];

            // Tiles: data-driven labels under the subject, gated by the toolbar's tile options.
            a.Tiles = _tiles
                ? new List<AppointmentDecoration>(Tiles(id))
                : [];

            // Adornments: positioned stamps and ribbons. Toolbar toggles add more on the selection.
            if (_adornments)
            {
                if (id == 2 && a.Adornments.All(d => d.Tooltip != "urgent")) a.Adornments.Add(Adornment("urgent"));
                if (id == 4 && a.Adornments.All(d => d.Tooltip != "paid")) a.Adornments.Add(Adornment("paid"));
                if (id == 6 && a.Adornments.All(d => d.Tooltip != "count"))
                    a.Adornments.Add(new AppointmentDecoration { Text = "3", Tooltip = "count", Position = DecorationPosition.FloatRight, Alignment = DecorationAlignment.Near, BackColor = Microsoft.UI.Colors.SlateGray, ForeColor = Microsoft.UI.Colors.White, Bold = true });
            }
            else
            {
                a.Adornments.Clear();
            }
        }

        _scheduler.ShowTiles = _tiles;
        _scheduler.SetTileOptions(_tiles
            ? [new SchedulerTileOption { Key = "status", Label = "Status", IsOn = _tileKeys.Contains("status") },
               new SchedulerTileOption { Key = "tech", Label = "Technician", IsOn = _tileKeys.Contains("tech") }]
            : []);

        _scheduler.SchedulerResources = _resources.Where(r => !_hiddenResources.Contains(r.Id)).ToList();
        _scheduler.Appointments = _appointments.ToList();
    }

    private IEnumerable<AppointmentDecoration> Tiles(int id)
    {
        if (_tileKeys.Contains("status"))
            yield return new AppointmentDecoration { Text = id % 2 == 0 ? "Scheduled" : "Confirmed", Position = DecorationPosition.Bottom, BackColor = id % 2 == 0 ? Microsoft.UI.Colors.LightSteelBlue : Microsoft.UI.Colors.PaleGreen, ForeColor = Microsoft.UI.Colors.Black, Tooltip = "Status tile" };
        if (_tileKeys.Contains("tech"))
            yield return new AppointmentDecoration { Glyph = "", Text = id switch { 1 or 2 => "Alex", 3 or 4 => "Bailey", _ => "Casey" }, Position = DecorationPosition.Bottom, Tooltip = "Technician tile" };
    }

    private static AppointmentIndicator Icon(string glyph, string tip, Color? color) => new() { Glyph = glyph, Tooltip = tip, Color = color };

    private static AppointmentDecoration Adornment(string template) => template switch
    {
        "urgent" => new AppointmentDecoration { Text = "URGENT", Tooltip = "urgent", Position = DecorationPosition.Top, Alignment = DecorationAlignment.Far, BackColor = Microsoft.UI.Colors.Firebrick, ForeColor = Microsoft.UI.Colors.White, Bold = true, FontSize = 9 },
        "paid" => new AppointmentDecoration { Glyph = "", Text = "PAID", Tooltip = "paid", Position = DecorationPosition.FloatRight, Alignment = DecorationAlignment.Far, BackColor = Microsoft.UI.Colors.SeaGreen, ForeColor = Microsoft.UI.Colors.White, FontSize = 9 },
        _ => new AppointmentDecoration { Text = template, Tooltip = template, Position = DecorationPosition.FloatLeft },
    };

    private void Log(string text) => _log.Text = text;
}
