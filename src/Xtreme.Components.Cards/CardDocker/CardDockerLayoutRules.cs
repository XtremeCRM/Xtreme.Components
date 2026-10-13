using System.Text;

namespace Xtreme.Components.Cards;

/// <summary>
/// The pure half of <see cref="CardDockerControl"/>: normalization and every arrangement edit
/// (drop, keyboard move), as plain functions over a <see cref="CardDockerLayout"/>.
/// <para>
/// Kept free of any XAML type on purpose, so the rules can be exercised headlessly by the unit
/// tests. The control only decides WHICH edit the user asked for; what that edit does to the
/// layout lives here.
/// </para>
/// </summary>
internal static class CardDockerLayoutRules
{
    internal const int MinPanelHeight = 60;
    internal const int MaxPanelHeight = 1200;

    /// <summary>
    /// Brings a layout into a state the renderer can trust: unknown or duplicate ids are dropped,
    /// empty panels disappear, cards nobody placed go to the tray, columns are clamped (remembering
    /// where a clamped panel came from) and Sort is dense per column.
    /// </summary>
    public static void Normalize(CardDockerLayout layout, IReadOnlyList<string> cardIds, int columnCount)
    {
        var known = cardIds.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // First occurrence wins: a card listed in two panels (or a panel and the tray) would
        // otherwise be drawn twice and could never be dragged out of both.
        foreach (var panel in layout.Panels)
            panel.CardIds.RemoveAll(id => !known.Contains(id) || !seen.Add(id));

        layout.TrayCardIds.RemoveAll(id => !known.Contains(id) || !seen.Add(id));
        layout.Panels.RemoveAll(p => p.CardIds.Count == 0);

        // A card nobody placed is set aside rather than invented into a panel: the host decides
        // where new cards belong, and silently materializing them here would fight that.
        foreach (var id in cardIds.Where(id => seen.Add(id)))
            layout.TrayCardIds.Add(id);

        var last = Math.Max(0, columnCount - 1);

        foreach (var panel in layout.Panels)
        {
            panel.MinHeight = Math.Clamp(panel.MinHeight, MinPanelHeight, MaxPanelHeight);
            if (panel.Column < 0) panel.Column = 0;
            if (panel.PreferredColumn < 0) panel.PreferredColumn = null;

            // A parked panel goes home as soon as the column count allows it, else it waits in the
            // last column. This is what makes lowering and then raising ColumnCount a round trip.
            // (A panel parked in the last column and then moved by the user is no longer parked:
            // MovePanel clears PreferredColumn.)
            if (panel.PreferredColumn is { } preferred)
            {
                if (preferred <= last)
                {
                    panel.Column = preferred;
                    panel.PreferredColumn = null;
                }
                else if (panel.Column != last)
                {
                    // Parked, but the count changed under it (3 → 2 → 1): send it back through the
                    // overflow pass from its preferred column so it lands in original order again.
                    // A panel already parked in the last column keeps its place, so a user's
                    // reordering of that column is not undone by the next normalization.
                    panel.Column = preferred;
                }
            }
        }

        // Panels newly past the last column are parked below what that column already holds, in
        // their original column-then-Sort order (ordering by Sort alone would interleave them).
        var overflow = layout.Panels.Where(p => p.Column > last).OrderBy(p => p.Column).ThenBy(p => p.Sort).ToList();
        if (overflow.Count > 0)
        {
            var next = layout.Panels.Where(p => p.Column == last).Select(p => p.Sort).DefaultIfEmpty(-1).Max() + 1;
            foreach (var panel in overflow)
            {
                panel.PreferredColumn = panel.Column;
                panel.Column = last;
                panel.Sort = next++;
            }
        }

        for (var column = 0; column <= last; column++)
        {
            var inColumn = PanelsIn(layout, column);
            for (var i = 0; i < inColumn.Count; i++) inColumn[i].Sort = i;
        }
    }

    /// <summary>A column's panels, top to bottom (stable on list order for equal Sort).</summary>
    public static List<CardDockerPanel> PanelsIn(CardDockerLayout layout, int column)
        => layout.Panels.Where(p => p.Column == column).OrderBy(p => p.Sort).ToList();

    public static CardDockerPanel? PanelOf(CardDockerLayout layout, string cardId)
        => layout.Panels.FirstOrDefault(p => p.CardIds.Contains(cardId));

    /// <summary>
    /// Moves a card into a panel at a slot (0 = before the first card, Count = after the last). The
    /// slot is counted with the card still in place, so a drop on a card's own right half is a no-op.
    /// </summary>
    public static void MoveCardToPanel(CardDockerLayout layout, string cardId, string panelId, int slot)
    {
        var target = layout.Panels.FirstOrDefault(p => p.Id == panelId);
        if (target is null) return;   // The panel went away; leave the card where it is.

        var source = PanelOf(layout, cardId);
        var sourceIndex = source?.CardIds.IndexOf(cardId) ?? -1;

        source?.CardIds.Remove(cardId);
        layout.TrayCardIds.Remove(cardId);

        // Removing the card first shifts every later slot in the same panel left by one.
        if (ReferenceEquals(source, target) && sourceIndex >= 0 && sourceIndex < slot) slot--;

        target.CardIds.Insert(Math.Clamp(slot, 0, target.CardIds.Count), cardId);
    }

    /// <summary>
    /// Puts a card in a panel of its own at the foot of a column. A card that is already alone in
    /// its panel takes that panel along (same id, same height) instead of minting a new one, so
    /// "move the only card" and "move the panel" are the same edit.
    /// </summary>
    public static void MoveCardToNewPanel(CardDockerLayout layout, string cardId, int column)
    {
        var source = PanelOf(layout, cardId);

        if (source is { CardIds.Count: 1 })
        {
            source.Column = column;
            source.PreferredColumn = null;
            source.Sort = int.MaxValue;
            return;
        }

        source?.CardIds.Remove(cardId);
        layout.TrayCardIds.Remove(cardId);

        layout.Panels.Add(new CardDockerPanel
        {
            Id = NewPanelId(layout),
            Column = column,
            Sort = int.MaxValue,
            CardIds = { cardId },
        });
    }

    /// <summary>Sets a card aside: removed from its panel, appended to the tray.</summary>
    public static void MoveCardToTray(CardDockerLayout layout, string cardId)
    {
        PanelOf(layout, cardId)?.CardIds.Remove(cardId);
        layout.TrayCardIds.Remove(cardId);
        layout.TrayCardIds.Add(cardId);
    }

    /// <summary>
    /// Moves a whole panel to a slot in a column (0 = top, Count = foot). As with cards, the slot is
    /// counted with the panel still in place. An explicit move is the user placing the panel, so any
    /// remembered <see cref="CardDockerPanel.PreferredColumn"/> is forgotten.
    /// </summary>
    public static void MovePanel(CardDockerLayout layout, string panelId, int column, int slot)
    {
        var panel = layout.Panels.FirstOrDefault(p => p.Id == panelId);
        if (panel is null) return;

        var target = PanelsIn(layout, column);
        var sourceIndex = target.IndexOf(panel);
        if (sourceIndex >= 0)
        {
            target.RemoveAt(sourceIndex);
            if (sourceIndex < slot) slot--;
        }

        target.Insert(Math.Clamp(slot, 0, target.Count), panel);

        panel.Column = column;
        panel.PreferredColumn = null;
        for (var i = 0; i < target.Count; i++) target[i].Sort = i;
    }

    /// <summary>First unused id of the form p1, p2, ...</summary>
    public static string NewPanelId(CardDockerLayout layout)
    {
        var existing = layout.Panels.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = 1; ; i++)
        {
            var candidate = $"p{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    /// <summary>
    /// A canonical text form of a layout: panels in reading order with every field, then the tray.
    /// Two layouts with equal signatures are the same arrangement, whatever order their
    /// <see cref="CardDockerLayout.Panels"/> lists happen to be in. Used to tell a real edit from
    /// a no-op one.
    /// </summary>
    public static string Signature(CardDockerLayout layout)
    {
        var sb = new StringBuilder();
        foreach (var p in layout.Panels.OrderBy(p => p.Column).ThenBy(p => p.Sort).ThenBy(p => p.Id, StringComparer.Ordinal))
        {
            sb.Append(p.Id).Append('|').Append(p.Column).Append('|').Append(p.Sort).Append('|')
              .Append(p.PreferredColumn?.ToString() ?? "-").Append('|').Append(p.MinHeight).Append('|')
              .AppendJoin('\u001f', p.CardIds).Append('\u001e');
        }

        sb.Append("tray|").AppendJoin('\u001f', layout.TrayCardIds);
        return sb.ToString();
    }
}
