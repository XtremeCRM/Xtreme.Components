using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Controls;

/// <summary>A view that draws the selection set (the "selected" outline) and can refresh it without a
/// re-render, so the scroll position and the rest of the view are kept.</summary>
internal interface ISelectionView
{
    void ApplySelection(IReadOnlyCollection<AppointmentModel>? selected);
}
