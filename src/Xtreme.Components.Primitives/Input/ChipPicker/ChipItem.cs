using Windows.UI;

namespace Xtreme.Components.Primitives;

public sealed record ChipItem
{
    public int ChipId { get; init; }
    public required string Name { get; init; }

    public bool IsSelected { get; set; } = false;

    /// <summary>
    /// XU-517: the selection is a NOT — "everything except this". Only meaningful while
    /// <see cref="IsSelected"/> is true, and only honored by hosts that opt in via
    /// <c>ChipPickerControl.AllowExclude</c> (an EntityFilter's rules carry an IsNot flag, so
    /// the Entity Type/Tag/Group pickers need to both show and edit exclusions). Every other
    /// consumer leaves this false and behaves exactly as before.
    /// </summary>
    public bool IsExcluded { get; set; } = false;

    /// <summary>
    /// False locks a selected item in: the picker dialog disables its checkbox (and its NOT toggle) once
    /// it is selected, so the user can add it but not take it out again. Unselected items stay pickable.
    /// </summary>
    public bool CanRemove { get; init; } = true;

    /// <summary>
    /// XU-586: the section this row is listed under in the picker dialog, e.g. an entity tag's EntityType.
    /// Null for every picker that does not group; the dialog only draws headers when some row has one.
    /// </summary>
    public string? GroupName { get; init; }

    /// <summary>
    /// XU-586: what the selected-chip pill on the picker BUTTON says, when that differs from
    /// <see cref="Name"/>. The dialog lists a grouped row under its header, but the button has no
    /// header, so two tags both named "Vendor" need their type there: "Vendor (Company)".
    /// </summary>
    public string? PillName { get; init; }

    /// <summary>The pill's text: <see cref="PillName"/> when set, else <see cref="Name"/>.</summary>
    public string PillText => string.IsNullOrEmpty(PillName) ? Name : PillName;

    // Colors, not Brushes (UI-safe for a plain record).
    /// <summary>Pill fill. Null (the default) uses the <c>XcChipBackgroundBrush</c> token (a neutral gray).</summary>
    public Color? BackgroundColor { get; init; }

    /// <summary>Pill text. Null (the default) uses the <c>XcChipForegroundBrush</c> token (black).</summary>
    public Color? ForegroundColor { get; init; }
}
