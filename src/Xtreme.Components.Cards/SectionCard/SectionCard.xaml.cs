using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
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
/// <para><b>Header fitting</b> — set <see cref="HeaderFit"/> to <see cref="SectionCardHeaderFit.Abbreviate"/>
/// and the tab headers shorten as the card narrows (measured, not by breakpoint), down to icon only,
/// before any tab is pushed into the overflow menu.</para>
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

    // Header fitting (HeaderFit = Abbreviate). Tab chrome (padding, icon, gap) and the strip's own
    // overhead are learned from the laid-out strip rather than assumed, so a theme or font change
    // needs no retuning.
    private readonly TextBlock _probe = new();
    private readonly Dictionary<bool, double> _textChrome = new();   // keyed by "tab has an icon"
    private double? _iconOnlyWidth;
    private double _stripOverhead;
    private FrameworkElement? _topNavGrid;   // NavigationView template part; read for the strip overhead
    private double _fittedWidth = double.NaN;
    private int _fitVersion;

    public SectionCard()
    {
        InitializeComponent();
        _tabs.CollectionChanged += OnTabsChanged;
        Loaded += OnLoaded;
        TabNav.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width != _fittedWidth) ScheduleHeaderFit();
        };
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

    // ---- Header fitting ----
    public static readonly DependencyProperty HeaderFitProperty = DependencyProperty.Register(
        nameof(HeaderFit), typeof(SectionCardHeaderFit), typeof(SectionCard),
        new PropertyMetadata(SectionCardHeaderFit.Overflow, (d, _) => ((SectionCard)d).OnHeaderFitChanged()));

    /// <summary>
    /// What the tab strip does when the headers do not fit. <see cref="SectionCardHeaderFit.Overflow"/>
    /// (the default) leaves them whole and lets NavigationView move tabs into its overflow menu;
    /// <see cref="SectionCardHeaderFit.Abbreviate"/> shortens them first, measured against the strip.
    /// </summary>
    public SectionCardHeaderFit HeaderFit
    {
        get => (SectionCardHeaderFit)GetValue(HeaderFitProperty);
        set => SetValue(HeaderFitProperty, value);
    }

    private void OnHeaderFitChanged()
    {
        if (!_isBuilt) return;
        if (HeaderFit == SectionCardHeaderFit.Abbreviate)
        {
            ScheduleHeaderFit();
        }
        else
        {
            _fitVersion++;   // drop any pass still queued
            foreach (var (item, tab) in StripItems()) ShowHeader(item, tab.Header ?? string.Empty, tab.Header ?? string.Empty);
        }
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
            ScheduleHeaderFit();
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

        ScheduleHeaderFit();
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
        ScheduleHeaderFit();   // the selected tab can draw wider (bolder) than the others
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

    // ---- Header fitting passes ----
    //
    // A pass runs after layout (low priority) so the strip's width and the tabs' widths are real:
    //   1. learn each kind of tab's chrome (tab width minus its measured text width) and the strip's
    //      overhead (NavigationView's TopNavGrid desired width minus the tabs);
    //   2. pick the largest shared letter count at which the measured strip fits (TabHeaderFitSolver);
    //   3. apply it and lay the strip out again on the spot. If NavigationView still has a tab in its
    //      overflow menu, re-add the items so it starts clean; if one is still out after that, the
    //      overhead was underestimated: learn it and go one letter shorter.
    // A newer pass (resize, tab change) supersedes a queued one through _fitVersion.

    private void ScheduleHeaderFit()
    {
        if (!_isBuilt || HeaderFit != SectionCardHeaderFit.Abbreviate || _tabs.Count == 0) return;
        var version = ++_fitVersion;
        DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () => FitHeaders(version));
    }

    private void FitHeaders(int version)
    {
        if (version != _fitVersion || HeaderFit != SectionCardHeaderFit.Abbreviate) return;

        TabNav.UpdateLayout();   // measure against the strip as it is now, not a frame ago
        var strip = TabNav.ActualWidth;
        var items = StripItems().ToList();
        if (strip <= 0 || items.Count == 0) return;
        _fittedWidth = strip;

        double Measure(string text) => MeasureText(items[0].Item, text);
        var cap = int.MaxValue;
        while (true)
        {
            LearnChrome(items);
            var entries = items.Select(p => Entry(p.Item, p.Tab)).ToList();
            var letters = Math.Min(cap, TabHeaderFitSolver.LargestFittingLetters(entries, Measure, strip - _stripOverhead - 1));
            for (var i = 0; i < items.Count; i++)
                ShowHeader(items[i].Item, TabHeaderFitSolver.HeaderAt(entries[i], letters), entries[i].Header);

            TabNav.UpdateLayout();
            if (AnyTabOverflowed(items))
            {
                // NavigationView only brings a tab back from its overflow menu once there is room for
                // its "More" button too, so a strip that overflowed earlier can stay overflowed after
                // the headers shrank. Re-adding the items makes it lay the strip out from scratch.
                ResetStrip();
                TabNav.UpdateLayout();
            }

            if (letters <= 0 || !AnyTabOverflowed(items)) return;

            // Still overflowing from a clean layout: the strip has overhead the summed tabs did not
            // include (a template without TopNavGrid, or one not yet laid out). Learn at least
            // enough to make this count fail.
            _stripOverhead = Math.Max(_stripOverhead, strip - TabHeaderFitSolver.TotalWidth(entries, Measure, letters) + 1);
            cap = letters - 1;
        }
    }

    /// <summary>Re-adds the strip's items (same instances, same selection) so NavigationView re-measures them.</summary>
    private void ResetStrip()
    {
        _updatingStrip = true;
        try
        {
            var items = TabNav.MenuItems.ToList();
            var selected = TabNav.SelectedItem;
            TabNav.MenuItems.Clear();
            foreach (var item in items) TabNav.MenuItems.Add(item);
            TabNav.SelectedItem = selected;
        }
        finally
        {
            _updatingStrip = false;
        }
    }

    private void LearnChrome(List<(NavigationViewItem Item, SectionCardTab Tab)> items)
    {
        var slots = 0.0;
        foreach (var (item, _) in items)
        {
            if (!IsInStrip(item)) continue;
            var slot = item.ActualWidth + item.Margin.Left + item.Margin.Right;
            slots += slot;
            if (item.Content is string { Length: > 0 } text)
                _textChrome[item.Icon is not null] = Math.Max(0, slot - MeasureText(item, text));
            else if (item.Icon is not null)
                _iconOnlyWidth = slot;
        }

        // NavigationView overflows when its top grid's desired width (the tabs plus its padding and
        // the other top-pane columns) exceeds the strip. With every tab in the strip that desired
        // width is exact, so the difference from the tabs is the strip's real overhead.
        _topNavGrid ??= FindNamed(TabNav, "TopNavGrid");
        if (_topNavGrid is { } grid && !AnyTabOverflowed(items))
            _stripOverhead = Math.Max(0, grid.DesiredSize.Width - slots);
    }

    private static FrameworkElement? FindNamed(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Name: var n } fe && n == name) return fe;
            if (FindNamed(child, name) is { } found) return found;
        }
        return null;
    }

    private TabHeaderFitEntry Entry(NavigationViewItem item, SectionCardTab tab)
    {
        var hasIcon = item.Icon is not null;
        // A kind of tab not laid out yet borrows the other kind's chrome; the overflow check corrects it.
        var chrome = _textChrome.TryGetValue(hasIcon, out var own) ? own
            : _textChrome.TryGetValue(!hasIcon, out var other) ? other : 0;
        return new TabHeaderFitEntry(tab.Header ?? string.Empty, hasIcon, chrome, _iconOnlyWidth ?? chrome);
    }

    private double MeasureText(NavigationViewItem item, string text)
    {
        _probe.FontSize = item.FontSize;
        _probe.FontFamily = item.FontFamily;
        _probe.FontWeight = item.FontWeight;
        _probe.Text = text;
        _probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _probe.DesiredSize.Width;
    }

    // A tab in the strip sits under TabNav in the visual tree, with a width; one moved into the
    // overflow menu lives in that menu's popup instead. (IsLoaded is no help: it lags a re-add.)
    private bool IsInStrip(NavigationViewItem item)
    {
        if (item.ActualWidth <= 0) return false;
        for (DependencyObject? node = item; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, TabNav)) return true;
        }
        return false;
    }

    private bool AnyTabOverflowed(List<(NavigationViewItem Item, SectionCardTab Tab)> items)
        => items.Any(p => !IsInStrip(p.Item));

    private static void ShowHeader(NavigationViewItem item, string text, string full)
    {
        if (!Equals(item.Content, text)) item.Content = text;
        ToolTipService.SetToolTip(item, text != full ? full : null);
        AutomationProperties.SetName(item, full);
    }

    private IEnumerable<(NavigationViewItem Item, SectionCardTab Tab)> StripItems()
        => TabNav.MenuItems.OfType<NavigationViewItem>()
            .Where(i => i.Tag is SectionCardTab)
            .Select(i => (i, (SectionCardTab)i.Tag));

    private NavigationViewItem? ItemFor(SectionCardTab tab)
        => TabNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, tab));

    // Reads a font-size token from application scope (where the UI text-size setting overrides
    // it), falling back when unset (e.g. the harness).
    private static double ResolveFontToken(string key, double fallback)
        => Application.Current.Resources.TryGetValue(key, out var v) && v is double d ? d : fallback;
}
