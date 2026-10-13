using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// The transparent, full-covering hit-test surface the time-grid interaction lives on. It's a
/// <see cref="Canvas"/> subclass purely so it can drive the pointer cursor: <c>UIElement.ProtectedCursor</c>
/// is <c>protected</c>, so a subclass is the only way to switch the cursor (move vs resize) on hover.
/// </summary>
internal sealed partial class PointerOverlay : Canvas
{
    private InputSystemCursorShape? _shape;

    /// <summary>Set the pointer cursor by shape, or null to fall back to the default arrow.
    /// No-ops when the shape is unchanged so we don't churn cursor objects on every pointer move.</summary>
    public void SetCursorShape(InputSystemCursorShape? shape)
    {
        if (_shape == shape)
        {
            return;
        }
        _shape = shape;
        ProtectedCursor = shape is { } s ? InputSystemCursor.Create(s) : null;
    }
}
