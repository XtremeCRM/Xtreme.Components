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

    private bool _tiles, _icons, _adornments, _editorEnabled, _tooltips;
    private readonly HashSet<string> _tileKeys = new() { "status", "tech" };

    // Appointment editor (host-side example): the control raises edit / create requests and ships
    // no form. This one is SectionCard + DateTimePicker + EditorActionBar from the other packages.
    private readonly Border _editorPane = new() { Width = 320, Margin = new Thickness(16, 0, 0, 0), Visibility = Visibility.Collapsed };
    private AppointmentModel? _editing;
    private bool _editingIsNew;
    private string _editorBaseline = "";
    private TextBox _subject = null!, _memo = null!;
    private ComboBox _resource = null!;
    private Primitives.DateTimePicker _start = null!, _end = null!;
    private CheckBox _allDay = null!;
    private Primitives.EditorActionBar _actions = null!;
    private TextBlock _editorTitle = null!;

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
            // A fuller week: overlaps, a recurring stand-up, status flags, early and late slots.
            Appt(8,  "Stand-up",          monday.AddHours(8).AddMinutes(30),           0.25, 1, 2, 3),
            Appt(9,  "Quote: Whitfield",  monday.AddHours(10).AddMinutes(30),          1,    1),      // overlaps Site survey
            Appt(10, "Annual service",    monday.AddDays(1).AddHours(8),               2,    3),
            Appt(11, "Warranty visit",    monday.AddDays(2).AddHours(9),               1.5,  1),
            Appt(12, "Parts pickup",      monday.AddDays(2).AddHours(12),              0.75, 3),
            Appt(13, "Install: Osei",     monday.AddDays(2).AddHours(13).AddMinutes(30), 3, 1),
            Appt(14, "Estimate: Novak",   monday.AddDays(3).AddHours(14),              1,    2),
            Appt(15, "Leak callback",     monday.AddDays(4).AddHours(13),              1.5,  3),
            Appt(16, "Office hours",      monday.AddDays(5).AddHours(9),               3,    2),
            Appt(17, "Emergency: no heat", monday.AddDays(5).AddHours(16),             1.5,  1),
        ];
        // Appointment types carry their own colours (BarColor = the left bar, BackColor = the fill);
        // an appointment without them falls back to its resource's colour.
        foreach (var a in _appointments)
        {
            var (bar, back) = TypeColours(a.Subject);
            a.BarColor = bar;
            a.BackColor = back;
        }
        // Flags the renderer draws: a recurring marker, a tentative hatch, a locked item.
        _appointments.First(a => a.Id is 8).RecurrenceRule = "FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR";
        _appointments.First(a => a.Id is 14).IsTentative = true;
        _appointments.First(a => a.Id is 10).IsLocked = true;
        _appointments.First(a => a.Id is 3).IsComplete = true;

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
            var created = new AppointmentModel
            {
                Id = Guid.NewGuid(), Subject = "New appointment", Start = e.Start, End = e.End, IsAllDay = e.IsAllDay,
                ResourceIds = e.ResourceId is { } r ? [r] : [],
            };
            if (_editorEnabled)
            {
                OpenEditor(created, isNew: true);   // the editor adds it on Save
                return;
            }
            _appointments.Add(created);
            Log($"Create {e.Start:ddd h:mm tt} to {e.End:h:mm tt}");
            Refresh();
        };
        _scheduler.AppointmentEditRequested += (_, e) =>
        {
            if (!_editorEnabled) return;      // unhandled: the control keeps its default (nothing)
            e.Handled = true;
            OpenEditor(e.Appointment, isNew: false);
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
        var switches = new Primitives.WrapPanel { ColumnSpacing = 16, RowSpacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        switches.Children.Add(new TextBlock { Text = "Examples:", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 });
        switches.Children.Add(Switch("Resource picker", on => { _resourcePane.Visibility = on ? Visibility.Visible : Visibility.Collapsed; if (!on) { _hiddenResources.Clear(); foreach (var cb in _resourcePane.Children.OfType<CheckBox>()) cb.IsChecked = true; } Refresh(); }));
        switches.Children.Add(Switch("Tiles", on => { _tiles = on; Refresh(); }));
        switches.Children.Add(Switch("Icons", on => { _icons = on; Refresh(); }));
        switches.Children.Add(Switch("Adornments", on => { _adornments = on; Refresh(); }));
        switches.Children.Add(Switch("Tooltips", on => { _tooltips = on; Refresh(); }));
        switches.Children.Add(Switch("Appointment editor", on => { _editorEnabled = on; if (!on) CloseEditor(); }));

        var hint = new TextBlock
        {
            Text = "Hover an appointment for its tooltip. With the editor on, double-click an appointment to edit it, or click an empty slot to create one.",
            Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        };

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

        BuildEditor();

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SetColumn(_scheduler, 1);
        SetColumn(_editorPane, 2);
        body.Children.Add(_resourcePane);
        body.Children.Add(_scheduler);
        body.Children.Add(_editorPane);

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetRow(hint, 1);
        SetRow(body, 2);
        SetRow(_log, 3);
        Children.Add(switches);
        Children.Add(hint);
        Children.Add(body);
        Children.Add(_log);

        Refresh();
    }

    private readonly List<CheckBox> _switches = new();

    /// <summary>Turns every example switch on and opens the editor (the self-test renders the decorated path).</summary>
    public void EnableAllExamples()
    {
        foreach (var box in _switches) box.IsChecked = true;
        OpenEditor(_appointments[0], isNew: false);
    }

    // ---- Appointment editor -------------------------------------------------------------------

    private void BuildEditor()
    {
        _editorTitle = new TextBlock { Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] };
        _subject = new TextBox { Header = "Subject" };
        _resource = new ComboBox { Header = "Resource", ItemsSource = _resources.Select(r => r.Name).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
        _start = new Primitives.DateTimePicker { Header = "Start", MinuteStep = 15 };
        _end = new Primitives.DateTimePicker { Header = "End", MinuteStep = 15 };
        _allDay = new CheckBox { Content = "All day" };
        _memo = new TextBox { Header = "Memo", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        _actions = new Primitives.EditorActionBar { DeleteConfirmTitle = "Delete appointment?", DeleteConfirmMessage = "This only removes it from the sample list." };

        _subject.TextChanged += (_, _) => UpdateDirty();
        _memo.TextChanged += (_, _) => UpdateDirty();
        _resource.SelectionChanged += (_, _) => UpdateDirty();
        _start.ValueChanged += (_, _) => UpdateDirty();
        _end.ValueChanged += (_, _) => UpdateDirty();
        _allDay.Checked += (_, _) => { _start.ShowTime = _end.ShowTime = false; UpdateDirty(); };
        _allDay.Unchecked += (_, _) => { _start.ShowTime = _end.ShowTime = true; UpdateDirty(); };
        _actions.SaveRequested += (_, _) => SaveEditor();
        _actions.CancelRequested += (_, _) => CloseEditor();
        _actions.DeleteRequested += (_, _) =>
        {
            if (_editing is not null && !_editingIsNew) { _appointments.Remove(_editing); Log($"Deleted '{_editing.Subject}'"); }
            CloseEditor();
            Refresh();
        };

        var fields = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        fields.Children.Add(_editorTitle);
        fields.Children.Add(_subject);
        fields.Children.Add(_resource);
        fields.Children.Add(_allDay);
        fields.Children.Add(_start);
        fields.Children.Add(_end);
        fields.Children.Add(_memo);
        fields.Children.Add(_actions);

        _editorPane.Child = new Cards.SectionCard
        {
            Body = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
    }

    private void OpenEditor(AppointmentModel appointment, bool isNew)
    {
        _editing = appointment;
        _editingIsNew = isNew;
        _editorTitle.Text = isNew ? "New appointment" : "Edit appointment";
        _subject.Text = appointment.Subject;
        _memo.Text = appointment.Memo ?? "";
        _resource.SelectedIndex = appointment.ResourceIds.Count > 0 ? _resources.FindIndex(r => Equals(r.Id, appointment.ResourceIds[0])) : -1;
        _allDay.IsChecked = appointment.IsAllDay;
        _start.ShowTime = _end.ShowTime = !appointment.IsAllDay;
        _start.Date = DateOnly.FromDateTime(appointment.Start); _start.Time = TimeOnly.FromDateTime(appointment.Start);
        _end.Date = DateOnly.FromDateTime(appointment.End);     _end.Time = TimeOnly.FromDateTime(appointment.End);
        _actions.ShowDelete = !isNew;
        _editorBaseline = Signature();
        _actions.IsDirty = isNew;             // a new appointment can be saved as-is
        _editorPane.Visibility = Visibility.Visible;
        Log(isNew ? $"New appointment at {appointment.Start:ddd h:mm tt}: fill in the editor" : $"Editing '{appointment.Subject}' (double-click opened it)");
    }

    private void CloseEditor()
    {
        _editing = null;
        _editorPane.Visibility = Visibility.Collapsed;
    }

    private string Signature() =>
        $"{_subject.Text}|{_memo.Text}|{_resource.SelectedIndex}|{_allDay.IsChecked}|{_start.Date}|{_start.Time}|{_end.Date}|{_end.Time}";

    private void UpdateDirty()
    {
        if (_editing is null) return;
        _actions.IsDirty = _editingIsNew || Signature() != _editorBaseline;
    }

    private void SaveEditor()
    {
        if (_editing is null) return;
        var allDay = _allDay.IsChecked == true;
        var start = (_start.Date ?? DateOnly.FromDateTime(_editing.Start)).ToDateTime(allDay ? TimeOnly.MinValue : _start.Time ?? TimeOnly.FromDateTime(_editing.Start));
        var end = (_end.Date ?? DateOnly.FromDateTime(_editing.End)).ToDateTime(allDay ? TimeOnly.MinValue : _end.Time ?? TimeOnly.FromDateTime(_editing.End));
        if (allDay && end <= start) end = start.AddDays(1);
        if (end <= start) end = start.AddMinutes(30);

        _editing.Subject = string.IsNullOrWhiteSpace(_subject.Text) ? "Untitled" : _subject.Text.Trim();
        _editing.Memo = string.IsNullOrWhiteSpace(_memo.Text) ? null : _memo.Text;
        _editing.IsAllDay = allDay;
        _editing.Start = start;
        _editing.End = end;
        if (_resource.SelectedIndex >= 0) _editing.ResourceIds = [_resources[_resource.SelectedIndex].Id];

        if (_editingIsNew) _appointments.Add(_editing);
        Log($"{(_editingIsNew ? "Created" : "Saved")} '{_editing.Subject}' {start:ddd h:mm tt} to {end:h:mm tt}");
        CloseEditor();
        Refresh();
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

        // Tooltips: Details (label/value rows) and Description feed the built-in tooltip; the
        // Install appointment also gets a custom renderer to show the override seam.
        foreach (var a in _appointments)
        {
            var id = a.Id is int i ? i : 0;
            a.Details = _tooltips
                ? id switch
                {
                    1 => [new("Customer", "Rivera, Alex"), new("Address", "14 Harbour Rd"), new("Phone", "555-010-2030")],
                    2 => [new("Customer", "Chen, Bailey"), new("Order", "SO-2026-0042"), new("Crew", "Alex, Dana")],
                    3 => [new("Customer", "Morgan, Casey"), new("Plan", "Battery plan, 3 of 4")],
                    4 => [new("Location", "Training room B"), new("Seats", "12")],
                    6 => [new("Agenda", "Q4 schedule"), new("Room", "Main office")],
                    _ => [],
                }
                : [];
            a.Description = _tooltips && id == 5 ? "Check the water softener install went in clean." : null;
        }
        _scheduler.AppointmentTooltipRenderer = _tooltips ? CustomTooltip : null;

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

    /// <summary>Six appointment types, each a (bar, fill) pair that reads in light and dark.</summary>
    private static (Color Bar, Color Back) TypeColours(string subject)
    {
        var s = subject.ToLowerInvariant();
        if (s.StartsWith("install"))                                   return (C(0x1F, 0x6F, 0xC9), C(0xDC, 0xE9, 0xF9)); // install: blue
        if (s.Contains("service") || s.Contains("swap") || s.Contains("warranty") || s.Contains("survey"))
                                                                       return (C(0x2E, 0x8B, 0x57), C(0xDD, 0xF3, 0xE4)); // service: green
        if (s.StartsWith("quote") || s.StartsWith("estimate"))        return (C(0xD9, 0x8E, 0x04), C(0xFB, 0xEF, 0xD3)); // quote: amber
        if (s.StartsWith("emergency") || s.Contains("callback") || s.Contains("no heat"))
                                                                       return (C(0xC0, 0x39, 0x2B), C(0xF8, 0xDC, 0xD8)); // emergency: red
        if (s.Contains("meeting") || s.Contains("stand-up") || s.Contains("training") || s.Contains("office"))
                                                                       return (C(0x6C, 0x75, 0x7D), C(0xE6, 0xE9, 0xEC)); // internal: grey
        return (C(0x7B, 0x4F, 0xB5), C(0xEC, 0xE3, 0xF6));                                                              // other: purple
    }

    private static Color C(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);

    /// <summary>
    /// The tooltip override seam: return an element to replace the built-in tooltip for this
    /// appointment, or null to keep it. Only the Install job gets the custom card here, so hovering
    /// the others shows the default (subject, time, Details rows, Description).
    /// </summary>
    private FrameworkElement? CustomTooltip(Scheduler.Rendering.AppointmentContext ctx)
    {
        if (ctx.Appointment.Id is not 2) return null;
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = ctx.Appointment.Subject, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"{ctx.Appointment.Start:ddd d MMM, h:mm tt} to {ctx.Appointment.End:h:mm tt}", Opacity = 0.75 });
        var bar = new Grid { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), Margin = new Thickness(0, 4, 0, 0) };
        bar.Children.Add(new Border { Width = 90, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Microsoft.UI.Colors.SeaGreen) });
        panel.Children.Add(bar);
        panel.Children.Add(new TextBlock { Text = "Parts picked 3 of 5. Custom tooltip via AppointmentTooltipRenderer.", FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 220 });
        return panel;
    }

    private static AppointmentDecoration Adornment(string template) => template switch
    {
        "urgent" => new AppointmentDecoration { Text = "URGENT", Tooltip = "urgent", Position = DecorationPosition.Top, Alignment = DecorationAlignment.Far, BackColor = Microsoft.UI.Colors.Firebrick, ForeColor = Microsoft.UI.Colors.White, Bold = true, FontSize = 9 },
        "paid" => new AppointmentDecoration { Glyph = "", Text = "PAID", Tooltip = "paid", Position = DecorationPosition.FloatRight, Alignment = DecorationAlignment.Far, BackColor = Microsoft.UI.Colors.SeaGreen, ForeColor = Microsoft.UI.Colors.White, FontSize = 9 },
        _ => new AppointmentDecoration { Text = template, Tooltip = template, Position = DecorationPosition.FloatLeft },
    };

    private void Log(string text) => _log.Text = text;
}
