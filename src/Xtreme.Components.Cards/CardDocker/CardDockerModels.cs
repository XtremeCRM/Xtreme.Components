using Microsoft.UI.Xaml.Controls;

namespace Xtreme.Components.Cards;

/// <summary>
/// One card the user can arrange. Deliberately identified by an opaque <see cref="Id"/> string:
/// Xtreme.Components has no access to the app's control keys (it has no project references at all),
/// so the host owns the meaning of the id and this control only ever moves it around.
/// </summary>
public sealed class CardDockerCard
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>
    /// Makes the card's icon. A FACTORY rather than an <c>IconElement</c>, because the control
    /// rebuilds its tree on every change and a XAML element can only have one parent — handing the
    /// same instance back twice would silently drop it from one of the two places it was used.
    /// </summary>
    public Func<IconElement?>? CreateIcon { get; init; }
}

/// <summary>One panel: the cards grouped inside it, and where it sits in the column grid.</summary>
public sealed class CardDockerPanel
{
    public required string Id { get; init; }

    /// <summary>
    /// Zero-based column. Clamped to the control's <c>ColumnCount</c> on load; a clamped panel
    /// remembers where it came from in <see cref="PreferredColumn"/>.
    /// </summary>
    public int Column { get; set; }

    /// <summary>Top-to-bottom position within the column. Rewritten densely after every change.</summary>
    public int Sort { get; set; }

    /// <summary>
    /// The column this panel returns to once the control has enough columns again, or null when it
    /// already sits where it belongs. Set by the control when lowering <c>ColumnCount</c> parks the
    /// panel in the last column; cleared when the count rises far enough (the panel goes home) or
    /// when the user moves the panel explicitly. A host rarely sets it, but it is part of the
    /// layout so the memory survives a <c>Layout</c> round trip and persistence.
    /// </summary>
    public int? PreferredColumn { get; set; }

    /// <summary>Card ids, left to right — the order they appear inside the panel.</summary>
    public List<string> CardIds { get; init; } = new();

    /// <summary>
    /// How tall the panel should be at minimum, in DIPs. Part of the arrangement rather than a host
    /// concept — unlike per-width visibility, "this panel needs room" is exactly what this control is
    /// for. Clamped to <see cref="CardDockerControl.MinPanelHeight"/>..<see
    /// cref="CardDockerControl.MaxPanelHeight"/> on load.
    /// </summary>
    public int MinHeight { get; set; } = 150;
}

/// <summary>
/// One arrangement: the panels and the cards set aside in the tray.
/// <para>
/// This is a SINGLE arrangement — the control shows one column count at a time. A host that keeps a
/// separate arrangement per width (as the General Info blade does) owns that mapping and hands this
/// control whichever one is being edited.
/// </para>
/// </summary>
public sealed class CardDockerLayout
{
    public List<CardDockerPanel> Panels { get; init; } = new();

    /// <summary>Cards the user has set aside — in no panel, shown in the tray.</summary>
    public List<string> TrayCardIds { get; init; } = new();

    public CardDockerLayout Clone() => new()
    {
        Panels = Panels
            .Select(p => new CardDockerPanel
            {
                Id = p.Id,
                Column = p.Column,
                Sort = p.Sort,
                PreferredColumn = p.PreferredColumn,
                MinHeight = p.MinHeight,
                CardIds = new List<string>(p.CardIds),
            })
            .ToList(),
        TrayCardIds = new List<string>(TrayCardIds),
    };
}
