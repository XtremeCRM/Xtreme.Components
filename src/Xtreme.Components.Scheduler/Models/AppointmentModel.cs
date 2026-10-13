namespace Xtreme.Components.Scheduler.Models;

/// <summary>
/// A single schedulable item rendered by the <c>SchedulerControl</c>. This is the
/// control's own domain-agnostic model — it has no knowledge of any host application's
/// data types. Hosts map their own entities onto this model.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Start"/> / <see cref="End"/> are absolute instants expressed in UTC.
/// The control converts them to each resource's local wall-clock for display using
/// <see cref="ResourceModel.TimeZoneId"/> (see Phase 7). Treat them as UTC even though
/// the CLR <see cref="DateTime"/> type does not enforce <see cref="DateTimeKind"/>.
/// </para>
/// <para>
/// All-day events (<see cref="IsAllDay"/>) are floating: their date components are used
/// as-is and they are not timezone-shifted.
/// </para>
/// </remarks>
public sealed class AppointmentModel
{
    /// <summary>
    /// Host-defined identity. Any value type works (int, Guid, string); the control only
    /// uses it for equality/lookup via <see cref="object.Equals(object?)"/>. May be null
    /// for not-yet-persisted appointments (e.g. a drag-create ghost).
    /// </summary>
    public object? Id { get; set; }

    /// <summary>Title shown on the event block.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Optional longer text (tooltip / detail row).</summary>
    public string? Description { get; set; }

    /// <summary>Start instant (UTC). See remarks on <see cref="AppointmentModel"/>.</summary>
    public DateTime Start { get; set; }

    /// <summary>End instant (UTC). Must be &gt;= <see cref="Start"/>.</summary>
    public DateTime End { get; set; }

    /// <summary>When true the event renders in the all-day banner and is not timezone-shifted.</summary>
    public bool IsAllDay { get; set; }

    /// <summary>
    /// Resource ids this appointment belongs to. An event spanning multiple resources
    /// renders once per matching resource lane (Planner) / column. Compared against
    /// <see cref="ResourceModel.Id"/> by value equality. Empty = unassigned.
    /// </summary>
    public IList<object> ResourceIds { get; set; } = new List<object>();

    /// <summary>Accent/stripe color for the event block (e.g. a category color). Null = theme default.</summary>
    public Color? BarColor { get; set; }

    /// <summary>Fill/background color for the event block. Null = theme default.</summary>
    public Color? BackColor { get; set; }

    /// <summary>
    /// Optional recurrence rule (RFC 5545 RRULE or host-defined). When non-null the block
    /// shows a recurrence indicator. Expansion of occurrences is a host/view concern.
    /// </summary>
    public string? RecurrenceRule { get; set; }

    /// <summary>Optional recurrence exception dates (host-defined serialization).</summary>
    public string? RecurrenceException { get; set; }

    /// <summary>True when this appointment carries a recurrence rule.</summary>
    public bool IsRecurring => !string.IsNullOrWhiteSpace(RecurrenceRule);

    /// <summary>Duration of the appointment.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>Locked appointments show a lock glyph in the default template (host-defined meaning).</summary>
    public bool IsLocked { get; set; }

    /// <summary>Completed appointments show a check glyph in the default template.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Cancelled appointments show a cancel glyph in the default template.</summary>
    public bool IsCancelled { get; set; }

    /// <summary>Tentative appointments show a question-mark glyph in the default template.</summary>
    public bool IsTentative { get; set; }

    /// <summary>Confirmed appointments show a confirmed glyph in the default template.</summary>
    public bool IsConfirmed { get; set; }

    /// <summary>
    /// Extra host-defined status glyphs rendered alongside the standard ones (in the default
    /// template's indicator strip, and available to custom templates via the appointment context).
    /// </summary>
    public IList<AppointmentIndicator> Indicators { get; set; } = new List<AppointmentIndicator>();

    /// <summary>
    /// Host-defined labelled detail rows shown in the hover popup (e.g. participant roles).
    /// The control is domain-agnostic — it just renders them. Empty = none shown.
    /// </summary>
    public IList<AppointmentDetail> Details { get; set; } = new List<AppointmentDetail>();

    /// <summary>
    /// User/template-defined badges decorating the block (the host's "adornment" feature) — small
    /// positioned text/glyph/image tiles. Rendered natively by the default templates at each
    /// decoration's <see cref="AppointmentDecoration.Position"/>. Empty = none.
    /// </summary>
    public IList<AppointmentDecoration> Adornments { get; set; } = new List<AppointmentDecoration>();

    /// <summary>
    /// Data-driven content tiles decorating the block (the host's "tile" feature — e.g. report fields).
    /// Structurally identical to <see cref="Adornments"/>; kept as a distinct collection so the two host
    /// features stay separable. Rendered natively by the default templates. Empty = none.
    /// </summary>
    public IList<AppointmentDecoration> Tiles { get; set; } = new List<AppointmentDecoration>();

    /// <summary>
    /// Optional secondary/internal note shown in the hover popup below the main <see cref="Description"/>
    /// note (host-defined meaning, e.g. an internal memo). Null/blank = not shown.
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// Opaque host payload (the originating entity, a view-model, etc.). The control never reads
    /// it; it's carried through so custom templates / renderers can bind to app-specific data.
    /// </summary>
    public object? Tag { get; set; }
}
