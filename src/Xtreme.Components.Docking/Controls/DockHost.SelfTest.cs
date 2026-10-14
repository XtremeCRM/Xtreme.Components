using Microsoft.UI.Xaml;

namespace Xtreme.Components.Docking.Controls;

/// <summary>Points in the drag and float paths where a self-test can inject a failure.</summary>
internal enum DockFault
{
    /// <summary>Applying a resolved drop to the items.</summary>
    ApplyDrop,

    /// <summary>Presenting a floating window (both the pooled and the fresh attempt).</summary>
    FloatWindow,
}

/// <summary>
/// Probes the gallery's <c>--selftest</c> drives the drag pipeline through, with no pointer: the drag
/// paths need a live visual tree, so they can't run in the headless unit tests. Internal, visible to
/// the gallery only.
/// </summary>
public partial class DockHost
{
    /// <summary>When set, a fault point it answers true for throws. Self-tests only.</summary>
    internal Func<DockFault, bool>? FaultInjector { get; set; }

    private void ThrowIfFaulted(DockFault point)
    {
        if (FaultInjector?.Invoke(point) == true)
            throw new InvalidOperationException($"Injected fault: {point}");
    }

    internal bool IsDragActive => _dragItem is not null;

    /// <summary>Every guide, plate, ghost and preview the current drag has on the overlay.</summary>
    internal int DragVisualCount
        => _dragZones.Count + _documentGuides.Count + _clusterGuides.Count + _guidePlates.Count + _clusterPlates.Count
           + (_dragGhost is null ? 0 : 1) + (_dragPreview is null ? 0 : 1);

    internal int OverlayChildCount => _overlayLayer.Children.Count;

    internal int EdgeMarkerCount => _dragZones.Count;

    internal int DocumentGuideCount => _documentGuides.Count;

    internal int ClusterGuideCount => _clusterGuides.Count;

    /// <summary>Starts a header drag on <paramref name="item"/> as a press-and-move past the threshold would.</summary>
    internal bool SelfTestBeginDrag(DockItem item)
    {
        if (item.Parent is not Microsoft.UI.Xaml.Controls.Grid chrome) return false;
        var header = chrome.Children.OfType<DockItemHeader>().FirstOrDefault();
        if (header is null) return false;

        BeginHeaderDrag(item, header, new Windows.Foundation.Point(20, 11));
        return _dragItem is not null;
    }

    /// <summary>Moves the drag's pointer to <paramref name="point"/> in overlay coordinates.</summary>
    internal void SelfTestMoveTo(Windows.Foundation.Point point)
        => UpdateHeaderDrag(new Windows.Foundation.Point(point.X - _dragPointer.X, point.Y - _dragPointer.Y));

    internal void SelfTestRelease() => CommitHeaderDrag();

    internal void SelfTestCancel() => CancelHeaderDrag();

    internal void SelfTestRebuild() => Rebuild();

    /// <summary>Center of the current drag's marker for <paramref name="edge"/>, if it shows one.</summary>
    internal Windows.Foundation.Point? SelfTestMarkerCenter(DockEdge edge)
    {
        if (!_dragZones.Any(z => z.Tag is DockEdge e && e == edge)) return null;
        var (x, y) = MarkerCenter(edge);
        return new Windows.Foundation.Point(x, y);
    }

    /// <summary>Center of the current drag's document-compass button for an edge and band.</summary>
    internal Windows.Foundation.Point? SelfTestDocumentGuideCenter(DockEdge edge, DockBand band)
    {
        foreach (var (_, e, b, hit) in _documentGuides)
        {
            if (e == edge && b == band)
                return new Windows.Foundation.Point(hit.X + hit.Width / 2, hit.Y + hit.Height / 2);
        }

        return null;
    }

    /// <summary>The center of the document in overlay coordinates — open space for a drop.</summary>
    internal Windows.Foundation.Point SelfTestDocumentCenter()
    {
        var origin = _documentHost.TransformToVisual(_overlayLayer).TransformPoint(new Windows.Foundation.Point(0, 0));
        return new Windows.Foundation.Point(origin.X + _documentHost.ActualWidth / 2, origin.Y + _documentHost.ActualHeight / 2);
    }

    /// <summary>The center of <paramref name="item"/>'s docked chrome in overlay coordinates.</summary>
    internal Windows.Foundation.Point SelfTestItemCenter(DockItem item)
    {
        var chrome = item.Parent as FrameworkElement ?? item;
        var origin = chrome.TransformToVisual(_overlayLayer).TransformPoint(new Windows.Foundation.Point(0, 0));
        return new Windows.Foundation.Point(origin.X + chrome.ActualWidth / 2, origin.Y + chrome.ActualHeight / 2);
    }
}
