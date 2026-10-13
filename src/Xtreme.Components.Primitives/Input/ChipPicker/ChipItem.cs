using Microsoft.UI;
using Windows.UI;

namespace Xtreme.Components.Primitives;

public sealed record ChipItem
{
    public int ChipId { get; init; }
    public required string Name { get; init; }

    public bool IsSelected { get; set; } = false;

    /// <summary>
    /// XU-517: the selection is a NOT — "everything except this". Only meaningful while
    /// <see cref="IsSelected"/> is true, and only honoured by hosts that opt in via
    /// <c>ChipPickerControl.AllowExclude</c> (an EntityFilter's rules carry an IsNot flag, so
    /// the Entity Type/Tag/Group pickers need to both show and edit exclusions). Every other
    /// consumer leaves this false and behaves exactly as before.
    /// </summary>
    public bool IsExcluded { get; set; } = false;

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

    // CHANGE: store Colors, not Brushes (UI-safe)
    // Default to a neutral grey pill (reads in both light and dark) when no color is supplied;
    // callers that pass an explicit color override this.
    public Color? BackgroundColor { get; init; } = Color.FromArgb(0xFF, 0xE1, 0xE1, 0xE1);
    public Color? ForegroundColor { get; init; } = Colors.Black;
}
