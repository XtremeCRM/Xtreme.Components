using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Xtreme.Components.Primitives;

/// <summary>
/// XU-791: works around Uno's Expander clipping its own content after the content grows.
///
/// <para>Uno's Expander (6.6) rebuilds the clip on its "ExpanderContentClip" border in that border's
/// SizeChanged handler from <c>border.ActualSize</c> — which, during SizeChanged on Uno, still reports
/// the PREVIOUS size. So the clip lags one resize behind: content that grows after the expander opened
/// (the order header's Order Entities rows arrive with the order) is cut off at the old height, and
/// only a close/reopen (another resize) catches it up.</para>
///
/// <para>Timing-proof by design: whenever the expander or its clip border resizes, the clip is rebuilt
/// from the border's size AFTER the layout pass settles (next dispatcher tick), when ActualSize is
/// current. There is no one-shot attach that can miss its moment — the template part is looked up on
/// every resize. The clip itself must stay: it hides the content while it slides open and shut.</para>
/// </summary>
public static class ExpanderClipFix
{
    /// <summary>
    /// XAML switch: <c>helpers:ExpanderClipFix.IsEnabled="True"</c> on an Expander applies the fix —
    /// the form every Shell Expander uses, so a new one copies it from its neighbours.
    /// </summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ExpanderClipFix),
        new PropertyMetadata(false, (d, e) =>
        {
            if (d is Expander expander && e.NewValue is true)
                Apply(expander);
        }));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static void Apply(Expander expander)
    {
        expander.SizeChanged -= OnExpanderSizeChanged;
        expander.SizeChanged += OnExpanderSizeChanged;
        expander.Loaded -= OnExpanderLoaded;
        expander.Loaded += OnExpanderLoaded;
        Refresh(expander);
    }

    private static void OnExpanderLoaded(object sender, RoutedEventArgs e) => Refresh((Expander)sender);

    private static void OnExpanderSizeChanged(object sender, SizeChangedEventArgs e) => Refresh((Expander)sender);

    private static void Refresh(Expander expander)
    {
        if (FindByName(expander, "ExpanderContentClip") is not Border clip)
            return;

        // Also catch resizes of the clip border alone (content growing inside a fixed-height host).
        clip.SizeChanged -= OnClipSizeChanged;
        clip.SizeChanged += OnClipSizeChanged;

        ResetClipLater(clip);
    }

    private static void OnClipSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is not Border border) return;
        SetClip(border, args.NewSize);
        ResetClipLater(border);
    }

    // After the layout pass ActualSize is current; the template's own handler may also have run
    // in between with the stale size, so this lands last.
    private static void ResetClipLater(Border border)
        => border.DispatcherQueue?.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => SetClip(border, new Size(border.ActualWidth, border.ActualHeight)));

    private static void SetClip(Border border, Size size)
    {
        if (size.Width <= 0 && size.Height <= 0) return;
        border.Clip = new RectangleGeometry { Rect = new Rect(new Point(0, 0), size) };
    }

    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name)
                return fe;
            if (FindByName(child, name) is { } found)
                return found;
        }
        return null;
    }
}
