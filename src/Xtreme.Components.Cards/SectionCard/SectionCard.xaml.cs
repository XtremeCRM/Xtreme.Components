using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Xtreme.Components.Cards;

/// <summary>
/// A boxed section "card" for grouping related fields inside an editor/detail pane. Owns the
/// consistent card chrome (card fill, themed stroke, soft corner, and a subtly darker
/// right/bottom edge) so every card matches.
///
/// <para><b>Tabbed mode</b> — add one or more <see cref="SectionCardTab"/> children. The header
/// renders them as a top-pane <c>NavigationView</c> strip (always shown, even for a single tab —
/// per the "every card header is a tab strip" convention) and the body swaps to the selected
/// tab's content. NavigationView (over SelectorBar) gives automatic top-overflow, hierarchy, and
/// per-tab icons, which the planned user-configurable card grouping needs, and matches
/// GeneralInfo's cards.</para>
///
/// <para><b>Headerless mode</b> — set <see cref="Body"/> and add no tabs. The tab strip collapses;
/// only the card chrome + body show (e.g. the EventEditor Details / Notes cards).</para>
///
/// <para>Per-list actions (Add) belong at the bottom of each tab's own content, not in this header.</para>
/// </summary>
[ContentProperty(Name = nameof(Body))]
public sealed partial class SectionCard : UserControl
{
    private readonly List<SectionCardTab> _tabs = new();

    public SectionCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>The card's tabs. Populated by XAML child elements in tabbed mode.</summary>
    public IList<SectionCardTab> Tabs => _tabs;

    // ---- Body: headerless content, used only when there are no tabs ----
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(object), typeof(SectionCard),
        new PropertyMetadata(null, OnBodyChanged));

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (SectionCard)d;
        if (card._tabs.Count == 0)
            card.BodyHost.Content = e.NewValue;
    }

    // ---- Title: a plain header label (no tab strip / no tab stops) ----
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SectionCard), new PropertyMetadata(null));

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        BuildTabs();
    }

    private void BuildTabs()
    {
        if (_tabs.Count == 0)
        {
            // No tabs: collapse the tab strip, show the plain body. If a Title was
            // given, show it as a plain header label (no NavigationView tab stops).
            TabNav.Visibility = Visibility.Collapsed;
            BodyHost.Content = Body;

            if (!string.IsNullOrEmpty(Title))
            {
                TitleText.Text = Title;
                TitleText.FontSize = ResolveFontToken("XcFontSizeTitle", 14);
                TitleText.Visibility = Visibility.Visible;
            }
            return;
        }

        TitleText.Visibility = Visibility.Collapsed;

        TabNav.Visibility = Visibility.Visible;
        TabNav.MenuItems.Clear();

        var titleFont = ResolveFontToken("XcFontSizeTitle", 14);
        foreach (var tab in _tabs)
        {
            TabNav.MenuItems.Add(new NavigationViewItem
            {
                Content = tab.Header ?? string.Empty,
                Tag = tab,
                FontSize = titleFont,
            });
        }

        if (TabNav.MenuItems.Count > 0)
            TabNav.SelectedItem = TabNav.MenuItems[0];
        ApplySelectedTab();
    }

    private void TabNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        => ApplySelectedTab();

    private SectionCardTab? SelectedTab
        => (TabNav.SelectedItem as NavigationViewItem)?.Tag as SectionCardTab;

    private void ApplySelectedTab()
    {
        if (SelectedTab is { } tab)
            BodyHost.Content = tab.Content;
    }

    // Reads a font-size token from application scope (where the UI text-size setting overrides
    // it), falling back when unset (e.g. the harness).
    private static double ResolveFontToken(string key, double fallback)
        => Application.Current.Resources.TryGetValue(key, out var v) && v is double d ? d : fallback;
}
