namespace Xtreme.Components.Primitives;

/// <summary>The arrangement a <see cref="PagingControl"/> uses, requested or shown.</summary>
public enum PagingLayoutMode
{
    /// <summary>Pick by width: compact below <c>CompactThreshold</c>, overflow below <c>OverflowThreshold</c>, else inline.
    /// Only meaningful as a request (<see cref="PagingControl.LayoutMode"/>); <see cref="PagingControl.ShownLayout"/> is never Auto.</summary>
    Auto,

    /// <summary>Everything in one row: size selector, first/previous/numbered/next/last, Go to, summary.</summary>
    Inline,

    /// <summary>Navigation buttons inline; the size selector and Go to move into a flyout.</summary>
    Overflow,

    /// <summary>Previous / next around a "Page n of m" button that opens a flyout with the rest.</summary>
    Compact,
}
