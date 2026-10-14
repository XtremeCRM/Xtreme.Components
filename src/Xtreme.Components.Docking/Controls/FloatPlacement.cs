namespace Xtreme.Components.Docking.Controls;

/// <summary>How much a window's frame adds around its client area, in physical pixels.</summary>
internal readonly record struct FrameInsets(int Left, int Top, int Right, int Bottom)
{
    public int Horizontal => Left + Right;

    public int Vertical => Top + Bottom;
}

/// <summary>Everything the planner knows about one float, in physical screen pixels.</summary>
internal sealed record FloatPlacementRequest
{
    /// <summary>Where the pane's chrome sits right now, when it is docked and measurable.</summary>
    public DockWindowBounds? Pane { get; init; }

    /// <summary>The pane's last floating window frame, from this session or a saved layout.</summary>
    public DockWindowBounds? Remembered { get; init; }

    /// <summary>The cursor, for a float that should land under it (tear-out, open-space release).</summary>
    public (int X, int Y)? Cursor { get; init; }

    /// <summary>How far into the pane, horizontally, the user grabbed it — kept under the cursor.</summary>
    public int GrabX { get; init; }

    /// <summary>The new window's frame thickness around its client area.</summary>
    public FrameInsets Insets { get; init; }

    /// <summary>Client size to use when neither the pane nor a remembered frame gives one.</summary>
    public (int Width, int Height) FallbackClientSize { get; init; }

    /// <summary>The work area of every connected monitor. Empty = unknown; no fitting is done.</summary>
    public IReadOnlyList<DockWindowBounds> WorkAreas { get; init; } = [];
}

/// <summary>
/// Where a floating window opens and how big it is. Pure arithmetic on physical screen pixels, so the
/// rules are testable without a window: <see cref="DockHost"/> gathers the inputs from the platform and
/// applies the result.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Size.</b> A pane that has floated before reopens at its remembered size. A first float takes
/// the size the pane had while docked, so it doesn't jump, capped at <see cref="MaxWorkAreaFraction"/> of
/// the monitor so a pane docked across a whole wide screen doesn't open as a full-screen window.</item>
/// <item><b>Position.</b> Under the cursor when there is one (the cursor keeps its grip on the title
/// bar), otherwise at the remembered position, otherwise exactly over the pane it came from.</item>
/// <item><b>Fit.</b> The window is moved fully onto the monitor it overlaps most — or the nearest one,
/// when a saved position is on a monitor that has since been unplugged. A window under the cursor is
/// only kept from going above the work area, so it never jumps away from the pointer carrying it.</item>
/// </list>
/// </remarks>
internal static class FloatPlacement
{
    /// <summary>A first float is never larger than this share of its monitor's work area.</summary>
    public const double MaxWorkAreaFraction = 0.8;

    public const int MinWidth = 200;
    public const int MinHeight = 150;

    public static DockWindowBounds Plan(FloatPlacementRequest request)
    {
        var insets = request.Insets;

        // Size first: position rules depend on it.
        int width, height;
        if (request.Remembered is { } remembered)
        {
            width = remembered.Width;
            height = remembered.Height;
        }
        else
        {
            var client = request.Pane is { } pane
                ? (pane.Width, pane.Height)
                : request.FallbackClientSize;
            width = client.Item1 + insets.Horizontal;
            height = client.Item2 + insets.Vertical;
        }

        int x, y;
        if (request.Cursor is { } cursor)
        {
            // Keep the grab point under the cursor, at mid-caption height. A frame with no caption to
            // measure (custom chrome) still gets the cursor a little way in from the top.
            var grab = Math.Clamp(request.GrabX, 16, Math.Max(16, width - insets.Horizontal - 16));
            x = cursor.X - insets.Left - grab;
            y = cursor.Y - Math.Max(insets.Top / 2, 12);
        }
        else if (request.Remembered is { } r)
        {
            x = r.X;
            y = r.Y;
        }
        else if (request.Pane is { } pane)
        {
            // The client area lands exactly where the pane was.
            x = pane.X - insets.Left;
            y = pane.Y - insets.Top;
        }
        else
        {
            var area = request.WorkAreas.Count > 0 ? request.WorkAreas[0] : new DockWindowBounds(0, 0, width, height);
            x = area.X + (area.Width - width) / 2;
            y = area.Y + (area.Height - height) / 2;
        }

        if (request.WorkAreas.Count == 0)
            return new DockWindowBounds(x, y, Math.Max(width, MinWidth), Math.Max(height, MinHeight));

        var target = request.Cursor is { } c
            ? AreaContaining(request.WorkAreas, c.X, c.Y) ?? Nearest(request.WorkAreas, new DockWindowBounds(x, y, width, height))
            : MostOverlapped(request.WorkAreas, new DockWindowBounds(x, y, width, height));

        // A remembered size was the user's choice — it only has to fit the monitor. A first float is
        // also kept to a sensible share of it.
        var maxWidth = request.Remembered is null ? (int)(target.Width * MaxWorkAreaFraction) : target.Width;
        var maxHeight = request.Remembered is null ? (int)(target.Height * MaxWorkAreaFraction) : target.Height;
        width = Math.Clamp(width, Math.Min(MinWidth, maxWidth), Math.Max(maxWidth, 1));
        height = Math.Clamp(height, Math.Min(MinHeight, maxHeight), Math.Max(maxHeight, 1));

        if (request.Cursor is not null)
        {
            // Only the top is enforced: the caption must stay reachable, and moving the window sideways
            // would pull it out from under the pointer that is carrying it.
            y = Math.Max(y, target.Y);
        }
        else
        {
            x = Math.Clamp(x, target.X, target.X + target.Width - width);
            y = Math.Clamp(y, target.Y, target.Y + target.Height - height);
        }

        return new DockWindowBounds(x, y, width, height);
    }

    private static DockWindowBounds? AreaContaining(IReadOnlyList<DockWindowBounds> areas, int x, int y)
    {
        foreach (var area in areas)
        {
            if (x >= area.X && x < area.X + area.Width && y >= area.Y && y < area.Y + area.Height)
                return area;
        }

        return null;
    }

    private static DockWindowBounds MostOverlapped(IReadOnlyList<DockWindowBounds> areas, DockWindowBounds frame)
    {
        DockWindowBounds? best = null;
        long bestOverlap = 0;
        foreach (var area in areas)
        {
            var overlap = Overlap(area, frame);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = area;
            }
        }

        return best ?? Nearest(areas, frame);
    }

    private static DockWindowBounds Nearest(IReadOnlyList<DockWindowBounds> areas, DockWindowBounds frame)
    {
        var cx = frame.X + frame.Width / 2.0;
        var cy = frame.Y + frame.Height / 2.0;
        return areas.MinBy(a =>
        {
            var dx = Math.Max(Math.Max(a.X - cx, 0), cx - (a.X + a.Width));
            var dy = Math.Max(Math.Max(a.Y - cy, 0), cy - (a.Y + a.Height));
            return dx * dx + dy * dy;
        });
    }

    private static long Overlap(DockWindowBounds a, DockWindowBounds b)
    {
        var w = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        var h = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0 ? (long)w * h : 0;
    }
}
