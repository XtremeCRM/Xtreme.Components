namespace Xtreme.Components.Cards;

/// <summary>What a <see cref="SectionCard"/>'s tab strip does when the tab headers do not fit.</summary>
public enum SectionCardHeaderFit
{
    /// <summary>Headers stay whole; tabs that do not fit move into NavigationView's overflow menu.</summary>
    Overflow = 0,

    /// <summary>
    /// Headers are shortened with <see cref="LabelAbbreviation"/> until the strip fits, measured at
    /// the strip's real font, down to icon only for tabs that have an icon. Only if even that does not
    /// fit do tabs move into the overflow menu.
    /// </summary>
    Abbreviate = 1,
}
