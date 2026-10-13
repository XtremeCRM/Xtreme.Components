using Microsoft.UI.Xaml.Media.Animation;

namespace Xtreme.Components.Primitives;

/// <summary>
/// Transient confirmation toast (fade in → hold → fade out). Overlay an instance as the
/// last child of a host's root Grid and call <see cref="Show"/>; a new Show while one is
/// visible restarts the message and the clock. Click-through (never intercepts input).
/// </summary>
public sealed partial class ToastControl : UserControl
{
    private const double FadeInMs = 150;
    private const double HoldMs = 1800;
    private const double FadeOutMs = 400;

    // The single in-flight fade. A composition Storyboard rather than a per-frame
    // dispatcher loop: no UI-thread timer churn, and its Completed handler is the
    // one place that hides the toast — guarded so a superseded animation can't
    // collapse the toast a newer Show() just made visible.
    private Storyboard? _storyboard;

    public ToastControl()
    {
        InitializeComponent();
    }

    public void Show(string message)
    {
        // Retire any in-flight animation: detach its Completed handler FIRST so a
        // stale finish can't run OnStoryboardCompleted against the new toast, then Stop.
        if (_storyboard is not null)
        {
            _storyboard.Completed -= OnStoryboardCompleted;
            _storyboard.Stop();
            _storyboard = null;
        }

        ToastText.Text = message;
        ToastBorder.Opacity = 0d;
        ToastBorder.Visibility = Visibility.Visible;

        var fade = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(fade, ToastBorder);
        Storyboard.SetTargetProperty(fade, "Opacity");
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0d });
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(FadeInMs)), Value = 1d });
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(FadeInMs + HoldMs)), Value = 1d });
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(FadeInMs + HoldMs + FadeOutMs)), Value = 0d });

        var sb = new Storyboard();
        sb.Children.Add(fade);
        sb.Completed += OnStoryboardCompleted;
        _storyboard = sb;
        sb.Begin();
    }

    private void OnStoryboardCompleted(object? sender, object e)
    {
        // Only the current storyboard may hide the toast — a superseded one was
        // detached in Show() before this could fire.
        if (!ReferenceEquals(sender, _storyboard)) return;
        ToastBorder.Visibility = Visibility.Collapsed;
        _storyboard = null;
    }
}
