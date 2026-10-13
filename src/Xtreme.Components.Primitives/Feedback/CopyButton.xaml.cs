using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Xtreme.Components.Primitives;

/// <summary>
/// Reusable copy-to-clipboard affordance: a small copy glyph that copies <see cref="Text"/>
/// and shows a brief fade-in/out "Copied" badge. Centralizes what was hand-rolled per site
/// (ContactRow storyboard, TenantInfo Task.Delay, the order serial buttons with no feedback)
/// so every copy control looks and behaves the same.
///
/// Bind <see cref="Text"/> to whatever should be copied (usually the same source a nearby
/// label is already bound to). The control does nothing when the text is empty. Optionally
/// override <see cref="Glyph"/>, <see cref="IconSize"/>, or <see cref="ToolTipText"/>.
/// </summary>
public sealed partial class CopyButton : UserControl
{
    // Standard Segoe "Copy" glyph (E8C8), the default icon.
    private const string CopyGlyph = "";

    public CopyButton() => InitializeComponent();

    /// <summary>The text placed on the clipboard when the button is clicked.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(CopyButton), new PropertyMetadata(null));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Icon glyph (Segoe symbol). Defaults to the standard Copy glyph (E8C8).</summary>
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(CopyButton), new PropertyMetadata(CopyGlyph));

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Icon font size. Defaults to 12.</summary>
    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(CopyButton), new PropertyMetadata(12d));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>Tooltip text. Defaults to "Copy to clipboard".</summary>
    public static readonly DependencyProperty ToolTipTextProperty =
        DependencyProperty.Register(nameof(ToolTipText), typeof(string), typeof(CopyButton), new PropertyMetadata("Copy to clipboard"));

    public string ToolTipText
    {
        get => (string)GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Text)) return;

        ClipboardHelper.CopyText(Text);

        // Restart cleanly on a rapid re-click: stop any in-flight fade, keep the popup open,
        // and replay from the top so the badge doesn't get stuck mid-fade.
        CopiedStoryboard.Stop();
        CopiedPopup.IsOpen = true;
        CopiedStoryboard.Begin();
    }

    private void CopiedStoryboard_Completed(object? sender, object e)
        => CopiedPopup.IsOpen = false;
}
