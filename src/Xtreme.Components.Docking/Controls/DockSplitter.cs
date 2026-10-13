using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Xtreme.Components.Core;

namespace Xtreme.Components.Docking.Controls;

/// <summary>
/// The draggable divider between a dock region and its neighbour — rendered as a SEAM, not a band:
/// the two sides genuinely touch along a thin dark border line, and the only chrome is a centred
/// grab handle (a small tab with a grip bar only — no pin, no chevron; those affordances live in the
/// pane headers).
/// </summary>
/// <remarks>
/// <para>Follows the same pattern as the scheduler's <c>ResizeGrip</c>, for the same two reasons:
/// <c>Thumb</c> is sealed and cannot carry a cursor, and manipulation events stay reliable on Uno
/// WASM where raw <c>PointerMoved</c> is not dependable during a held drag.</para>
/// <para>The splitter's grid track collapses to NOTHING: the root is <see cref="HitSpan"/> thick
/// with negative margins cancelling it out, so the neighbors meet flush along a thin dark line
/// while the hit area still straddles the seam.
/// That also means it overhangs both neighbours and must draw above them (ZIndex).</para>
/// <para><see cref="Dragged"/> reports the CUMULATIVE translation from the start of the drag, not a
/// per-frame delta, so the host applies <c>startExtent + cumulative</c>. Summing per-frame deltas
/// against a possibly-stale measured size accumulates error and makes the pane drift away from the
/// cursor.</para>
/// <para>Public so app screens can use the same seam between their own resizable sections instead of
/// inventing a second divider look.</para>
/// </remarks>
public sealed partial class DockSplitter : Grid
{
    private const double HitSpan = 16d;         // grab thickness straddling the seam (8 each side)
    private const double HandleThickness = 14d;
    private const double HandleLength = 72d;
    private const double GripLength = 28d;

    /// <summary>
    /// How far content must keep clear of an OWNED seam's handle, on the neighbor's side: the
    /// handle protrudes its full thickness over the neighbor, plus a little breathing room. The host
    /// insets the neighbor by this, so no consumer ever pads for the handle itself.
    /// </summary>
    internal const double OwnedSeamClearance = HandleThickness + 2d;

    /// <summary>Clearance on EACH side of an unowned seam, whose handle straddles the line.</summary>
    internal const double StraddleClearance = HandleThickness / 2d + 2d;

    /// <summary>
    /// The inset the content beyond an owned seam needs, on its side facing the seam: a Left pane's
    /// handle reaches right, over the neighbour's LEFT edge, and so on.
    /// </summary>
    internal static Thickness ClearanceOppositeOf(DockEdge attachEdge) => attachEdge switch
    {
        DockEdge.Left => new Thickness(OwnedSeamClearance, 0, 0, 0),
        DockEdge.Right => new Thickness(0, 0, OwnedSeamClearance, 0),
        DockEdge.Top => new Thickness(0, OwnedSeamClearance, 0, 0),
        DockEdge.Bottom => new Thickness(0, 0, 0, OwnedSeamClearance),
        _ => new Thickness(0),
    };

    private readonly Orientation _orientation;

    /// <summary>Raised when a drag begins; the host snapshots the current extent here.</summary>
    public event EventHandler? DragStarted;

    /// <summary>
    /// Raised on each drag step with the cumulative movement along the splitter's axis, in DIPs. The
    /// consumer decides what the sign means — a far-side region has to invert it.
    /// </summary>
    public event Action<double>? Dragged;

    /// <summary>Raised when the drag ends; the host persists the final extent here.</summary>
    public event EventHandler? DragCompleted;

    /// <param name="orientation">
    /// <see cref="Orientation.Vertical"/> draws a vertical seam that resizes horizontally (Left/Right
    /// regions); <see cref="Orientation.Horizontal"/> draws a horizontal seam that resizes vertically.
    /// </param>
    /// <param name="theme">
    /// The owner's <c>ActualTheme</c>, used for the first frame because a splitter is constructed
    /// before it enters the tree, where its own <c>ActualTheme</c> is not yet meaningful. After that
    /// the handle follows the splitter's own <c>ActualThemeChanged</c>. Drives the handle surface —
    /// see <see cref="ComponentTheme"/> for why this is not an app-resource lookup.
    /// </param>
    /// <param name="attachEdge">
    /// Which edge's pane the handle BELONGS to: the handle sits flush against the seam on that side
    /// and protrudes over the neighbor, rounded only on the protruding side.
    /// <see cref="DockEdge.None"/> (splitters
    /// between layers or side-by-side slots, where neither side owns the seam) straddles the seam
    /// with all corners rounded.
    /// </param>
    public DockSplitter(Orientation orientation, ElementTheme theme, DockEdge attachEdge = DockEdge.None)
    {
        _orientation = orientation;
        var vertical = orientation == Orientation.Vertical;

        if (vertical)
        {
            Width = HitSpan;
            Margin = new Thickness(-HitSpan / 2, 0, -HitSpan / 2, 0);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
            ManipulationMode = ManipulationModes.TranslateX;
        }
        else
        {
            Height = HitSpan;
            Margin = new Thickness(0, -HitSpan / 2, 0, -HitSpan / 2);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
            ManipulationMode = ManipulationModes.TranslateY;
        }

        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Canvas.SetZIndex(this, 50); // overhangs both neighbours; the seam chrome must draw above them

        // Transparent rather than unset: an unset Background leaves the panel hit-test-invisible across
        // its bounds, so the drag would silently never start.
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        var seamBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x00, 0x00, 0x00));

        // The seam line is drawn as TWO segments that stop at the handle rather than one full run —
        // the line must go AROUND the handle, never under it, so the handle reads as an
        // opening in the seam that belongs to its pane. Track layout [* | handle | *] keeps the gap
        // centred at any length.
        var visual = new Grid { IsHitTestVisible = false };
        if (vertical)
        {
            visual.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            visual.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HandleLength) });
            visual.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }
        else
        {
            visual.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            visual.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HandleLength) });
            visual.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var segment = 0; segment <= 2; segment += 2)
        {
            var line = new Border { Background = seamBrush };
            if (vertical)
            {
                line.Width = 1;
                line.HorizontalAlignment = HorizontalAlignment.Center;
                line.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetRow(line, segment);
            }
            else
            {
                line.Height = 1;
                line.HorizontalAlignment = HorizontalAlignment.Stretch;
                line.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(line, segment);
            }

            visual.Children.Add(line);
        }

        Children.Add(visual);

        // Which way the handle protrudes: away from the pane it belongs to, over the neighbour.
        var protrude = attachEdge switch
        {
            DockEdge.Left => 1.0,   // pane is left of the seam — protrude right, over the document
            DockEdge.Right => -1.0,
            DockEdge.Top => 1.0,    // pane above — protrude down
            DockEdge.Bottom => -1.0,
            _ => 0.0,               // unowned seam — straddle it
        };

        var corners = (attachEdge, vertical) switch
        {
            (DockEdge.Left, true) => new CornerRadius(0, 7, 7, 0),
            (DockEdge.Right, true) => new CornerRadius(7, 0, 0, 7),
            (DockEdge.Top, false) => new CornerRadius(0, 0, 7, 7),
            (DockEdge.Bottom, false) => new CornerRadius(7, 7, 0, 0),
            _ => new CornerRadius(7),
        };

        // No border on the flush side: the handle OPENS into the pane it belongs to instead of
        // being sealed off by its own outline.
        var handleBorder = (attachEdge, vertical) switch
        {
            (DockEdge.Left, true) => new Thickness(0, 1, 1, 1),
            (DockEdge.Right, true) => new Thickness(1, 1, 0, 1),
            (DockEdge.Top, false) => new Thickness(1, 0, 1, 1),
            (DockEdge.Bottom, false) => new Thickness(1, 1, 1, 0),
            _ => new Thickness(1),
        };

        // Pane-surface color so the handle reads as a solid tab of the pane's surface, with the
        // seam's own color wrapping it as the border.
        var handle = new Border
        {
            Width = vertical ? HandleThickness : HandleLength,
            Height = vertical ? HandleLength : HandleThickness,
            CornerRadius = corners,
            Background = ComponentTheme.Surface(theme),
            BorderBrush = seamBrush,
            BorderThickness = handleBorder,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, // the whole seam drags; the handle is purely the affordance
            RenderTransform = new TranslateTransform
            {
                X = vertical ? protrude * HandleThickness / 2 : 0,
                Y = vertical ? 0 : protrude * HandleThickness / 2,
            },
            Child = new Border
            {
                Width = vertical ? 3 : GripLength,
                Height = vertical ? GripLength : 3,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0x80, 0x80, 0x80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        Children.Add(handle);

        // The constructor theme covers the first frame; once in the tree the splitter follows its own
        // ActualTheme, so a live light/dark switch repaints the handle without the owner rebuilding it.
        ActualThemeChanged += (_, _) => handle.Background = ComponentTheme.Surface(ActualTheme);

        ManipulationStarted += (_, _) => DragStarted?.Invoke(this, EventArgs.Empty);
        ManipulationDelta += OnManipulationDelta;
        ManipulationCompleted += (_, _) => DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        => Dragged?.Invoke(_orientation == Orientation.Vertical
            ? e.Cumulative.Translation.X
            : e.Cumulative.Translation.Y);
}
