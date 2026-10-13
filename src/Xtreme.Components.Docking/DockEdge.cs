namespace Xtreme.Components.Docking;

/// <summary>
/// Which side of the <see cref="Controls.DockHost"/> document a <see cref="Controls.DockItem"/> sits on.
/// </summary>
/// <remarks>
/// Flags, because <see cref="Controls.DockItem.AllowedDockEdges"/> needs to express a SET of legal
/// destinations ("this pane may only ever live left or right"), while
/// <see cref="Controls.DockItem.Edge"/> carries exactly one of them as current state. The capability
/// vs. state split is the same one the rest of the API follows — see
/// <see cref="Controls.DockItem.AllowFloat"/> (capability) against
/// <see cref="Controls.DockItem.IsFloating"/> (state).
/// </remarks>
[Flags]
public enum DockEdge
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8,

    /// <summary>Left | Right — the common case for a list/detail blade.</summary>
    Horizontal = Left | Right,

    /// <summary>Top | Bottom.</summary>
    Vertical = Top | Bottom,

    All = Left | Right | Top | Bottom,
}

/// <summary>Helpers for reasoning about an edge's axis.</summary>
public static class DockEdgeExtensions
{
    /// <summary>
    /// True when the edge docks along the horizontal axis (Left/Right), meaning the item's
    /// <see cref="Controls.DockItem.DockLength"/> is a WIDTH. Top/Bottom make it a HEIGHT.
    /// </summary>
    public static bool IsHorizontal(this DockEdge edge) => edge is DockEdge.Left or DockEdge.Right;

    /// <summary>
    /// True for the edges that sit AFTER the document in layout order (Right/Bottom). Used to decide
    /// which side of the region the splitter goes on, and to invert the drag sign — dragging right
    /// GROWS a left-docked pane but SHRINKS a right-docked one.
    /// </summary>
    public static bool IsFarSide(this DockEdge edge) => edge is DockEdge.Right or DockEdge.Bottom;

    /// <summary>Reduces a flags value to a single edge, for callers that were handed a set.</summary>
    public static DockEdge FirstOrDefault(this DockEdge edges, DockEdge fallback = DockEdge.Left)
    {
        if (edges.HasFlag(DockEdge.Left)) return DockEdge.Left;
        if (edges.HasFlag(DockEdge.Right)) return DockEdge.Right;
        if (edges.HasFlag(DockEdge.Top)) return DockEdge.Top;
        if (edges.HasFlag(DockEdge.Bottom)) return DockEdge.Bottom;
        return fallback;
    }
}
