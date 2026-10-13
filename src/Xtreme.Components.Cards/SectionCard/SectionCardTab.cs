using Microsoft.UI.Xaml.Markup;

namespace Xtreme.Components.Cards;

/// <summary>
/// One tab within a <see cref="SectionCard"/>: a header label, an optional icon and a body
/// (<see cref="Content"/>). A card always renders its tabs as a top-pane NavigationView strip —
/// even a single-tab card shows one tab — so every card header looks the same.
///
/// <para>This is a plain <see cref="DependencyObject"/> (not a control), so its
/// <see cref="Content"/> is held as data and only parented when the tab becomes selected — the
/// card hosts it then. That keeps unselected tab bodies out of the visual tree and avoids
/// double-parenting.</para>
///
/// <para>Every property is live: the owning card listens for changes and updates its strip (or,
/// for the selected tab's <see cref="Content"/>, its body) in place.</para>
///
/// <para>Per-list actions (e.g. an Add button) live inside this tab's <see cref="Content"/>
/// (typically at the bottom of the list), not in the card header.</para>
/// </summary>
[ContentProperty(Name = nameof(Content))]
public sealed partial class SectionCardTab : DependencyObject
{
    /// <summary>Tab label shown in the NavigationView strip.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SectionCardTab), new PropertyMetadata(null, OnChanged));

    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Icon shown before the label in the strip, handed to the tab's <c>NavigationViewItem.Icon</c>
    /// as-is. An element has one parent, so give every tab its own instance.
    /// </summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconElement), typeof(SectionCardTab), new PropertyMetadata(null, OnChanged));

    public IconElement? Icon
    {
        get => (IconElement?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The tab's body content (the natural XAML child).</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(object), typeof(SectionCardTab), new PropertyMetadata(null, OnChanged));

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>Raised for every property change, with the property; the owning card listens.</summary>
    internal event EventHandler<DependencyProperty>? Changed;

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SectionCardTab)d).Changed?.Invoke(d, e.Property);
}
