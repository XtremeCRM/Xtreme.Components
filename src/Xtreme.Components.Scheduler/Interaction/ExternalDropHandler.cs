using Windows.ApplicationModel.DataTransfer;
using Xtreme.Components.Scheduler.Contracts;

namespace Xtreme.Components.Scheduler.Interaction;

/// <summary>
/// Host-side bridge for dragging an external item (e.g. an unscheduled-queue row) into the
/// scheduler. The source stashes an <see cref="ISchedulerDroppable"/> in the drag
/// <see cref="DataPackage"/>; the scheduler reads it back on drop and raises its
/// external-drop event. In-process only — the payload is the live object reference.
/// </summary>
public static class ExternalDropHandler
{
    /// <summary>The <see cref="DataPackage"/> property key the scheduler looks for.</summary>
    public const string DataKey = "Xtreme.Components.Scheduler.ExternalDroppable";

    /// <summary>Call from a source element's <c>DragStarting</c> to advertise a droppable item.</summary>
    public static void SetDroppable(DataPackage data, ISchedulerDroppable item)
    {
        data.Properties[DataKey] = item;
        data.RequestedOperation = DataPackageOperation.Copy;
    }

    internal static ISchedulerDroppable? GetDroppable(DataPackageView view) =>
        view.Properties.TryGetValue(DataKey, out var v) ? v as ISchedulerDroppable : null;
}
