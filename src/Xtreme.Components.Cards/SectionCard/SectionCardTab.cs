using Microsoft.UI.Xaml.Markup;

namespace Xtreme.Components.Cards;

/// <summary>
/// One tab within a <see cref="SectionCard"/>: a header label and a body (<see cref="Content"/>).
/// A card always renders its tabs as a top-pane NavigationView strip — even a single-tab card
/// shows one tab — so every card header looks the same.
///
/// <para>This is a plain <see cref="DependencyObject"/> (not a control), so its
/// <see cref="Content"/> is held as data and only parented when the tab becomes selected — the
/// card hosts it then. That keeps unselected tab bodies out of the visual tree and avoids
/// double-parenting.</para>
///
/// <para>Per-list actions (e.g. an Add button) live inside this tab's <see cref="Content"/>
/// (typically at the bottom of the list), not in the card header.</para>
/// </summary>
[ContentProperty(Name = nameof(Content))]
public sealed partial class SectionCardTab : DependencyObject
{
    /// <summary>Tab label shown in the NavigationView strip.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SectionCardTab), new PropertyMetadata(null));

    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>The tab's body content (the natural XAML child).</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(object), typeof(SectionCardTab), new PropertyMetadata(null));

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }
}
