using Microsoft.UI.Xaml.Media;
using Xtreme.Components.Scheduler.Models;
using Xtreme.Components.Scheduler.Utilities;

namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>Which block shape an appointment is being rendered as.</summary>
public enum AppointmentSurface
{
    /// <summary>A full time-grid block (Day / Week body).</summary>
    Timed,

    /// <summary>A compact chip (Month cell, all-day row).</summary>
    Chip,

    /// <summary>A horizontal bar (Planner timeline).</summary>
    Bar,
}

/// <summary>
/// The binding target for appointment templates and the argument to a code renderer. It wraps an
/// <see cref="AppointmentModel"/> and precomputes the things a template can't express cleanly —
/// the fill / accent <see cref="Brush"/>es (with palette fallback), the auto-contrast
/// <see cref="Foreground"/>, and the formatted time strings — so the default template and any
/// custom one bind to ready-to-use values with no converters and no palette knowledge.
/// </summary>
public sealed class AppointmentContext
{
    internal AppointmentContext(AppointmentModel appt, SchedulerPalette palette, AppointmentSurface surface, bool showTime, double cornerRadius = 4, bool showTiles = true)
    {
        Appointment = appt;
        Surface = surface;
        ShowTime = showTime;
        CornerRadius = cornerRadius;
        ShowTiles = showTiles;
        BackBrush = appt.BackColor is Color back ? new SolidColorBrush(back) : palette.EventDefaultBack;
        BarBrush = appt.BarColor is Color bar ? new SolidColorBrush(bar) : palette.EventDefaultBar;
        Foreground = appt.BackColor is Color c ? SchedulerPalette.ContrastForeground(c) : palette.EventForeground;
        BorderBrush = palette.EventBorder;
    }

    /// <summary>The underlying appointment (use <see cref="AppointmentModel.Tag"/> for host data).</summary>
    public AppointmentModel Appointment { get; }

    /// <summary>The block shape being rendered (lets one renderer handle every surface).</summary>
    public AppointmentSurface Surface { get; }

    /// <summary>Corner radius (px) for this block — Settings-driven for timed blocks.</summary>
    public double CornerRadius { get; }

    /// <summary>Whether this surface wants the time shown (chips use it; timed blocks always do).</summary>
    public bool ShowTime { get; }

    /// <summary>Whether data-driven <see cref="AppointmentModel.Tiles"/> render (Settings "Show tiles").
    /// Adornments always render.</summary>
    public bool ShowTiles { get; }

    /// <summary>Fill brush — the appointment's BackColor, or the theme default.</summary>
    public Brush BackBrush { get; }

    /// <summary>Accent/stripe brush — the appointment's BarColor, or the theme default.</summary>
    public Brush BarBrush { get; }

    /// <summary>Readable text brush auto-contrasted against <see cref="BackBrush"/>.</summary>
    public Brush Foreground { get; }

    /// <summary>Outline brush for the block, so it reads distinctly against the grid.</summary>
    public Brush BorderBrush { get; }

    // ---- Convenience pass-throughs for binding ----
    public string Subject => Appointment.Subject;

    /// <summary>The event note (the appointment's description), shown under the divider by default.</summary>
    public string? Note => Appointment.Description;
    public DateTime Start => Appointment.Start;
    public DateTime End => Appointment.End;
    public bool IsAllDay => Appointment.IsAllDay;
    public bool IsRecurring => Appointment.IsRecurring;
    public bool IsLocked => Appointment.IsLocked;
    public bool IsComplete => Appointment.IsComplete;
    public bool IsCancelled => Appointment.IsCancelled;
    public bool IsTentative => Appointment.IsTentative;
    public bool IsConfirmed => Appointment.IsConfirmed;
    public IList<AppointmentIndicator> Indicators => Appointment.Indicators;

    /// <summary>Host-defined labelled detail rows (e.g. participant roles) for the hover popup.</summary>
    public IList<AppointmentDetail> Details => Appointment.Details;

    /// <summary>User/template badges (the host's "adornment" feature) decorating the block.</summary>
    public IList<AppointmentDecoration> Adornments => Appointment.Adornments;

    /// <summary>Data-driven content tiles (the host's "tile" feature) decorating the block.</summary>
    public IList<AppointmentDecoration> Tiles => Appointment.Tiles;

    /// <summary>Secondary/internal note shown below the main <see cref="Note"/> in the hover popup.</summary>
    public string? Memo => Appointment.Memo;

    public object? Tag => Appointment.Tag;

    /// <summary>"9:00 AM" — the start time only.</summary>
    public string StartTime => Start.ToString("h:mm tt");

    /// <summary>"9:00 AM – 10:00 AM" for timed items, "All day" for all-day items.</summary>
    public string TimeRange => IsAllDay ? "All day" : $"{Start:h:mm tt} – {End:h:mm tt}";

    /// <summary>Compact start time via <see cref="SchedulerTimeFormatter"/>: "10a", "12:30p".</summary>
    public string CompactStart => SchedulerTimeFormatter.Compact(Start);

    /// <summary>Compact span via the swappable <see cref="SchedulerTimeFormatter.RangeFormatter"/>:
    /// "10a-12:30p" for timed items, "All day" for all-day items.</summary>
    public string CompactTimeRange => IsAllDay ? "All day" : SchedulerTimeFormatter.RangeFormatter(Start, End);
}
