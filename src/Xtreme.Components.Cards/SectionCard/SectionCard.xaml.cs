using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
/// <para>The mode follows <see cref="Tabs"/> live: the card is built on first load and then
/// reacts to tabs being added or removed, to <see cref="Title"/>, <see cref="Body"/> and to every
/// tab property.</para>
///
/// <para>Per-list actions (Add) belong at the bottom of each tab's own content, not in this header.</para>
/// </summary>
[ContentProperty(Name = nameof(Body))]
public sealed partial class SectionCard : UserControl
{
    private readonly ObservableCollection<SectionCardTab> _tabs = new();
    private readonly List<SectionCardTab> _watched = new();

    private bool _isBuilt;
    private bool _updatingStrip;
    private bool _syncingIndex;
    private SectionCardTab? _selectedTab;

    public SectionCard()
    {
        InitializeComponent();
        _tabs.CollectionChanged += OnTabsChanged;
        Loaded += OnLoaded;
    }

    /// <summary>
    /// The card's tabs. Populated by XAML child elements in tabbed mode; adding or removing tabs
    /// later rebuilds the strip, keeping the selected tab when it is still present.
    /// </summary>
    public IList<SectionCardTab> Tabs => _tabs;

    // ---- Body: headerless content, used only when there are no tabs ----
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(object), typeof(SectionCard),
        new PropertyMetadata(null, OnBodyChanged));

    /// <summary>
    /// Content shown while the card has no tabs. Kept while tabs exist (not shown), and shown again
    /// if the last tab is removed.
    /// </summary>
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
        nameof(Title), typeof(string), typeof(SectionCard),
        new PropertyMetadata(null, (d, _) => ((SectionCard)d).OnTitleChanged()));

    /// <summary>Plain header label shown while the card has no tabs.</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private void OnTitleChanged()
    {
        if (_isBuilt && _tabs.Count == 0) UpdateTitle();
    }

    // ---- Selection ----
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(SectionCard),
        new PropertyMetadata(0, (d, _) => ((SectionCard)d).OnSelectedIndexChanged()));

    /// <summary>
    /// Index of the selected tab in <see cref="Tabs"/>. Defaults to 0 (the first tab); a value set
    /// before load is applied when the card builds. Reads -1 once built with no tabs. Setting an
    /// index outside <see cref="Tabs"/> is ignored (the value snaps back to the current tab).
    /// </summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>The selected tab, or null before the card has loaded or while it has no tabs.</summary>
    public SectionCardTab? SelectedTab => _selectedTab;

    /// <summary>
    /// Raised when <see cref="SelectedTab"/> changes: the user picks a tab, <see cref="SelectedIndex"/>
    /// is set, the card first selects a tab on load, or removing the selected tab moves the
    /// selection. Not raised when only the index shifts because a tab was inserted or removed
    /// before the selected one.
    /// </summary>
    public event EventHandler? SelectionChanged;

    private void OnSelectedIndexChanged()
    {
        if (_syncingIndex || !_isBuilt) return;

        var index = SelectedIndex;
        if (index < 0 || index >= _tabs.Count)
        {
            SyncIndex();
            return;
        }

        SelectInStrip(_tabs[index]);
    }

    // ---- Building ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Built once; after that the card tracks changes itself, so a later re-load (the card moved
        // in the tree) must not rebuild and reset the selection.
        if (_isBuilt) return;
        _isBuilt = true;
        BuildTabs();
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Resubscribe wholesale: tab lists are short, and it covers Reset (which carries no
        // OldItems) the same way as Add/Remove/Replace.
        foreach (var tab in _watched) tab.Changed -= OnTabPropertyChanged;
        _watched.Clear();
        foreach (var tab in _tabs)
        {
            tab.Changed += OnTabPropertyChanged;
            _watched.Add(tab);
        }

        if (_isBuilt) BuildTabs();
    }

    private void OnTabPropertyChanged(object? sender, DependencyProperty property)
    {
        if (!_isBuilt || sender is not SectionCardTab tab) return;

        if (property == SectionCardTab.ContentProperty)
        {
            if (ReferenceEquals(tab, _selectedTab)) BodyHost.Content = tab.Content;
            return;
        }

        // Header / Icon: patch the one item in place rather than rebuilding the strip.
        if (ItemFor(tab) is { } item)
        {
            item.Content = tab.Header ?? string.Empty;
            item.Icon = tab.Icon;
        }
    }

    private void BuildTabs()
    {
        _updatingStrip = true;
        try
        {
            // Let go of every tab icon before the items are dropped: an IconElement has one parent,
            // so re-adding it to a new item while the old one still holds it would throw.
            foreach (var old in TabNav.MenuItems.OfType<NavigationViewItem>()) old.Icon = null;
            TabNav.MenuItems.Clear();

            if (_tabs.Count == 0)
            {
                // No tabs: collapse the tab strip, show the plain body. If a Title was
                // given, show it as a plain header label (no NavigationView tab stops).
                TabNav.Visibility = Visibility.Collapsed;
                BodyHost.Content = Body;
                UpdateTitle();
                Select(null);
                return;
            }

            TitleText.Visibility = Visibility.Collapsed;
            TabNav.Visibility = Visibility.Visible;

            var titleFont = ResolveFontToken("XcFontSizeTitle", 14);
            foreach (var tab in _tabs)
            {
                TabNav.MenuItems.Add(new NavigationViewItem
                {
                    Content = tab.Header ?? string.Empty,
                    Icon = tab.Icon,
                    Tag = tab,
                    FontSize = titleFont,
                });
            }

            // Keep the selected tab if it survived; otherwise take the one now at the selected
            // index (clamped), which is the neighbor of a removed tab or SelectedIndex on first build.
            var target = _selectedTab is not null && _tabs.Contains(_selectedTab)
                ? _selectedTab
                : _tabs[Math.Clamp(SelectedIndex, 0, _tabs.Count - 1)];

            TabNav.SelectedItem = ItemFor(target);
            Select(target);
        }
        finally
        {
            _updatingStrip = false;
        }
    }

    private void UpdateTitle()
    {
        if (!string.IsNullOrEmpty(Title))
        {
            TitleText.Text = Title;
            TitleText.FontSize = ResolveFontToken("XcFontSizeTitle", 14);
            TitleText.Visibility = Visibility.Visible;
        }
        else
        {
            TitleText.Visibility = Visibility.Collapsed;
        }
    }

    private void TabNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_updatingStrip) return;
        if ((args.SelectedItem as NavigationViewItem)?.Tag is SectionCardTab tab) Select(tab);
    }

    private void SelectInStrip(SectionCardTab tab)
    {
        _updatingStrip = true;
        try { TabNav.SelectedItem = ItemFor(tab); }
        finally { _updatingStrip = false; }

        Select(tab);
    }

    /// <summary>Makes <paramref name="tab"/> the selected one: body, index, event.</summary>
    private void Select(SectionCardTab? tab)
    {
        var changed = !ReferenceEquals(tab, _selectedTab);
        _selectedTab = tab;

        if (tab is not null) BodyHost.Content = tab.Content;
        SyncIndex();

        if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncIndex()
    {
        _syncingIndex = true;
        try { SelectedIndex = _selectedTab is null ? -1 : _tabs.IndexOf(_selectedTab); }
        finally { _syncingIndex = false; }
    }

    private NavigationViewItem? ItemFor(SectionCardTab tab)
        => TabNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, tab));

    // Reads a font-size token from application scope (where the UI text-size setting overrides
    // it), falling back when unset (e.g. the harness).
    private static double ResolveFontToken(string key, double fallback)
        => Application.Current.Resources.TryGetValue(key, out var v) && v is double d ? d : fallback;
}
