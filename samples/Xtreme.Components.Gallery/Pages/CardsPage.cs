using Xtreme.Components.Cards;

namespace Xtreme.Components.Gallery.Pages;

/// <summary>
/// Two <see cref="SectionCard"/>s (tabbed and headerless), a header-fitting demo (a width slider
/// over an Abbreviate card and an Overflow card), then a <see cref="CardDockerControl"/>
/// fed hand-written cards: eight cards across a few panels, a 1/2/3 column switch (lowering it parks
/// panels in the last column, raising it sends them home), and a read-out of the arrangement that
/// updates on every real change. Panels move by their top handle; cards and handles also move with
/// Ctrl+arrow keys when focused.
/// </summary>
public sealed partial class CardsPage : Grid
{
    private readonly CardDockerControl _docker = new();
    private readonly TextBlock _readout = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 8, 0, 0) };
    private SectionCard? _fitCard;
    private Slider? _fitWidth;

    public CardsPage()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // SectionCard: a tabbed card (with tab icons) and a headerless one side by side. The
        // headerless card's buttons edit the tabbed card live: tabs come and go, the selection
        // follows, and SelectionChanged reports it.
        var tabbed = new SectionCard();
        tabbed.Tabs.Add(new SectionCardTab { Header = "Details", Icon = new SymbolIcon(Symbol.Contact), Content = Lines("Name: Alex Rivera", "Type: Customer", "Since: 2019") });
        tabbed.Tabs.Add(new SectionCardTab { Header = "Notes", Icon = new SymbolIcon(Symbol.Comment), Content = Lines("Prefers morning visits.", "Gate code 4411.") });

        var selection = new TextBlock { Opacity = 0.75 };
        tabbed.SelectionChanged += (_, _) => selection.Text = $"Selected: {tabbed.SelectedTab?.Header ?? "(none)"} (index {tabbed.SelectedIndex})";

        var extra = 0;
        var addTab = new Button { Content = "Add tab" };
        addTab.Click += (_, _) =>
        {
            extra++;
            tabbed.Tabs.Add(new SectionCardTab { Header = $"Extra {extra}", Icon = new SymbolIcon(Symbol.Add), Content = Lines($"Added at runtime ({extra}).") });
            tabbed.SelectedIndex = tabbed.Tabs.Count - 1;
        };
        var removeTab = new Button { Content = "Remove selected" };
        removeTab.Click += (_, _) =>
        {
            if (tabbed.SelectedTab is { } tab) tabbed.Tabs.Remove(tab);
        };

        var body = Lines("Headerless card: Body only, no tab strip.", "Same chrome, same corner, same edge.");
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { addTab, removeTab } });
        body.Children.Add(selection);
        var headerless = new SectionCard { Body = body };
        var cards = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 12) };
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetColumn(headerless, 1);
        cards.Children.Add(tabbed);
        cards.Children.Add(headerless);
        Children.Add(new StackPanel { Children = { cards, BuildHeaderFitDemo() } });

        // CardDocker toolbar.
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        toolbar.Children.Add(new TextBlock { Text = "Columns:", VerticalAlignment = VerticalAlignment.Center });
        foreach (var count in new[] { 1, 2, 3 })
        {
            var button = new Button { Content = count.ToString(), MinWidth = 36 };
            button.Click += (_, _) => { _docker.ColumnCount = count; UpdateReadout(); };
            toolbar.Children.Add(button);
        }
        SetRow(toolbar, 1);
        Children.Add(toolbar);

        _docker.Cards = BuildCards();
        _docker.ColumnCount = 3;
        _docker.Layout = BuildLayout();
        _docker.LayoutChanged += (_, _) => UpdateReadout();
        _docker.TrayHeader = "Set aside (Delete on a focused card; Ctrl+Up brings it back)";
        var scroller = new ScrollViewer { Content = _docker, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollMode = ScrollMode.Auto };
        SetRow(scroller, 2);
        Children.Add(scroller);

        SetRow(_readout, 3);
        Children.Add(_readout);
        UpdateReadout();
    }

    /// <summary>
    /// HeaderFit = Abbreviate: a slider sets the card's width and the tab headers shorten to fit,
    /// measured at the strip's own font (hover a shortened tab for its full name), down to icons only.
    /// A plain card beside it shows the default (Overflow) at the same width for comparison.
    /// </summary>
    private UIElement BuildHeaderFitDemo()
    {
        _fitCard = FitDemoCard(SectionCardHeaderFit.Abbreviate);
        var overflowCard = FitDemoCard(SectionCardHeaderFit.Overflow);

        _fitWidth = new Slider { Minimum = 120, Maximum = 900, Value = 420, StepFrequency = 1, Width = 260, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
        void Apply()
        {
            _fitCard.Width = overflowCard.Width = _fitWidth.Value;
            label.Text = $"Card width {_fitWidth.Value:0} px";
        }
        _fitWidth.ValueChanged += (_, _) => Apply();
        Apply();

        return new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new TextBlock { Text = "Header fitting: drag the slider. Top: HeaderFit = Abbreviate. Bottom: the default, Overflow.", Opacity = 0.75 },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _fitWidth, label } },
                _fitCard,
                overflowCard,
            },
        };
    }

    private static SectionCard FitDemoCard(SectionCardHeaderFit fit)
    {
        var card = new SectionCard { HeaderFit = fit, HorizontalAlignment = HorizontalAlignment.Left };
        card.Tabs.Add(new SectionCardTab { Header = "Contact Details", Icon = new SymbolIcon(Symbol.Contact), Content = Lines("Alex Rivera, Customer since 2019.") });
        card.Tabs.Add(new SectionCardTab { Header = "Addresses", Icon = new SymbolIcon(Symbol.Map), Content = Lines("12 Harbor Rd, Duluth MN") });
        card.Tabs.Add(new SectionCardTab { Header = "Payment Accounts", Icon = new SymbolIcon(Symbol.Tag), Content = Lines("Visa ending 4242") });
        card.Tabs.Add(new SectionCardTab { Header = "Notes", Icon = new SymbolIcon(Symbol.Comment), Content = Lines("Prefers morning visits.") });
        return card;
    }

    /// <summary>
    /// <c>--selftest</c>: sweeps the header-fit demo down from wide to the narrowest width at which
    /// icons alone fit, then back up, and checks at every step that all four tabs are in the strip
    /// (none pushed into the overflow menu) and inside its bounds; that headers are whole at the widest
    /// width and shortened in the middle; and that going back up restores them.
    /// </summary>
    internal async Task<List<string>> HeaderFitSelfTestAsync()
    {
        var problems = new List<string>();
        var log = new List<string>();
        var strip = Descendants(_fitCard!).OfType<NavigationView>().First();

        foreach (var width in new double[] { 900, 640, 520, 420, 360, 320, 420, 900 })
        {
            _fitWidth!.Value = width;
            await Task.Delay(400);
            var items = Descendants(strip).OfType<NavigationViewItem>().ToList();
            var texts = items.Select(i => i.Content as string ?? "").ToList();
            log.Add($"{width} [{string.Join(" | ", texts)}]");

            var inside = items.Count(i => i.ActualWidth > 0
                && i.TransformToVisual(strip).TransformPoint(new Point(i.ActualWidth, 0)).X <= strip.ActualWidth + 0.5);
            if (items.Count != 4 || inside != 4)
                problems.Add($"{width}px: {inside} of 4 tabs inside the strip ({string.Join(" | ", texts)})");
            if (width == 900 && texts.Any(t => t.EndsWith(LabelAbbreviation.Suffix)))
                problems.Add($"900px: shortened with room to spare ({string.Join(" | ", texts)})");
            if (width == 420 && !texts.Any(t => t.EndsWith(LabelAbbreviation.Suffix) || t.Length == 0))
                problems.Add($"420px: nothing shortened ({string.Join(" | ", texts)})");
        }

        Console.WriteLine("selftest: cards header fit " + string.Join("  ", log));
        _fitWidth!.Value = 420;
        return problems;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private static StackPanel Lines(params string[] lines)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(12) };
        foreach (var line in lines) panel.Children.Add(new TextBlock { Text = line });
        return panel;
    }

    private static List<CardDockerCard> BuildCards() =>
    [
        Card("name", "Name", Symbol.Contact), Card("addresses", "Addresses", Symbol.Map),
        Card("contacts", "Contacts", Symbol.Mail), Card("groups", "Groups", Symbol.People),
        Card("notes", "Notes", Symbol.Comment), Card("directions", "Directions", Symbol.Go),
        Card("payments", "Payment Accounts", Symbol.Tag), Card("custom", "Custom Fields", Symbol.Setting),
    ];

    private static CardDockerCard Card(string id, string title, Symbol symbol)
        => new() { Id = id, Title = title, CreateIcon = () => new SymbolIcon(symbol) };

    private static CardDockerLayout BuildLayout() => new()
    {
        Panels =
        [
            new CardDockerPanel { Id = "p1", Column = 0, Sort = 0, CardIds = { "name" } },
            new CardDockerPanel { Id = "p2", Column = 0, Sort = 1, CardIds = { "addresses" } },
            new CardDockerPanel { Id = "p3", Column = 1, Sort = 0, CardIds = { "notes", "directions" } },
            new CardDockerPanel { Id = "p4", Column = 2, Sort = 0, CardIds = { "contacts", "groups" } },
            new CardDockerPanel { Id = "p5", Column = 2, Sort = 1, CardIds = { "payments" } },
        ],
        TrayCardIds = { "custom" },
    };

    private void UpdateReadout()
    {
        var layout = _docker.Layout;
        var columns = Enumerable.Range(0, _docker.ColumnCount).Select(column =>
            $"col{column}: " + string.Join(" ", layout.Panels.Where(p => p.Column == column).OrderBy(p => p.Sort)
                .Select(p => $"{p.Id}({string.Join(",", p.CardIds)})" + (p.PreferredColumn is { } home ? $"->col{home}" : ""))));
        var tray = layout.TrayCardIds.Count > 0 ? $"  |  tray: {string.Join(",", layout.TrayCardIds)}" : "";
        _readout.Text = string.Join("   ", columns) + tray;
    }
}
