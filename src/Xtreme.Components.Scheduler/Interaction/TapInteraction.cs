using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;
using Xtreme.Components.Scheduler.Contracts;
using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Interaction;

/// <summary>
/// Gesture wiring for the views that have no pointer overlay: Month cells and chips, Agenda rows and the
/// all-day row of Day/Week. Uses the platform tap gestures (Tapped / DoubleTapped / RightTapped), which
/// cooperate with a scrolling parent and with touch. A view sets the callbacks it supports and attaches
/// its elements; <c>SchedulerControl</c> turns the callbacks into its public events.
/// </summary>
/// <remarks>
/// A double-tap raises Tapped for the first tap and DoubleTapped for the second. Both the click and the
/// double-click callbacks therefore fire for one double-click; the control de-duplicates (see
/// <c>SchedulerControl.RaiseEdit</c> / <c>RaiseSlotCreate</c>).
/// </remarks>
internal sealed class TapInteraction
{
    /// <summary>Single tap on an appointment: (appointment, Ctrl held).</summary>
    public Action<AppointmentModel, bool>? Click { get; init; }

    /// <summary>Double tap on an appointment.</summary>
    public Action<AppointmentModel>? DoubleClick { get; init; }

    /// <summary>Right tap (or press-and-hold) on an appointment, with the root-relative position.</summary>
    public Action<AppointmentModel, Point?>? Context { get; init; }

    /// <summary>Single tap on an empty cell.</summary>
    public Action<EmptySlotSelectedArgs>? EmptyClick { get; init; }

    /// <summary>Double tap on an empty cell.</summary>
    public Action<EmptySlotSelectedArgs>? EmptyDoubleClick { get; init; }

    /// <summary>Wire an appointment element (chip, row). The gesture is marked handled so an enclosing
    /// cell never sees it as an empty-cell tap.</summary>
    public void AttachAppointment(UIElement element, AppointmentModel appointment)
    {
        element.Tapped += (_, e) =>
        {
            e.Handled = true;
            Click?.Invoke(appointment, IsCtrlDown());
        };
        element.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            DoubleClick?.Invoke(appointment);
        };
        element.RightTapped += (_, e) =>
        {
            e.Handled = true;
            Context?.Invoke(appointment, RootPosition(element, e.GetPosition));
        };
    }

    /// <summary>Wire an empty all-day cell (Month day, Day/Week all-day cell) for <paramref name="day"/>.
    /// The element needs a non-null background to be hit-testable over its empty area.</summary>
    public void AttachEmptyDay(UIElement element, DateTime day, object? resourceId)
    {
        element.Tapped += (_, e) =>
        {
            if (e.Handled)
            {
                return;
            }
            e.Handled = true;
            EmptyClick?.Invoke(Slot(day, resourceId, RootPosition(element, e.GetPosition)));
        };
        element.DoubleTapped += (_, e) =>
        {
            if (e.Handled)
            {
                return;
            }
            e.Handled = true;
            EmptyDoubleClick?.Invoke(Slot(day, resourceId, RootPosition(element, e.GetPosition)));
        };
    }

    private static EmptySlotSelectedArgs Slot(DateTime day, object? resourceId, Point? position) => new()
    {
        Start = day.Date,
        ResourceId = resourceId,
        IsAllDay = true,
        Position = position,
    };

    // The gesture position relative to the XamlRoot content, matching the overlay views' Position contract.
    private static Point? RootPosition(UIElement element, Func<UIElement, Point> getPosition)
    {
        var root = element.XamlRoot?.Content;
        return root is null ? null : getPosition(root);
    }

    private static bool IsCtrlDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
}
