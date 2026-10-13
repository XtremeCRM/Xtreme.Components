using Xtreme.Components.Cards;

namespace Xtreme.Components.Gallery.Pages;

/// <summary>
/// Two <see cref="SectionCard"/>s (tabbed and headerless) above a <see cref="CardDockerControl"/>
/// fed hand-written cards: eight cards across a few panels, a 1/2/3 column switch, and a read-out of
/// the arrangement that updates on every drag.
/// </summary>
public sealed partial class CardsPage : Grid
{
    private readonly CardDockerControl _docker = new();
    private readonly TextBlock _readout = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 8, 0, 0) };

    public CardsPage()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // SectionCard: a tabbed card and a headerless one side by side.
        var tabbed = new SectionCard();
        tabbed.Tabs.Add(new SectionCardTab { Header = "Details", Content = Lines("Name: Alex Rivera", "Type: Customer", "Since: 2019") });
        tabbed.Tabs.Add(new SectionCardTab { Header = "Notes", Content = Lines("Prefers morning visits.", "Gate code 4411.") });
        var headerless = new SectionCard { Body = Lines("Headerless card: Body only, no tab strip.", "Same chrome, same corner, same edge.") };
        var cards = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 12) };
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetColumn(headerless, 1);
        cards.Children.Add(tabbed);
        cards.Children.Add(headerless);
        Children.Add(cards);

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
        var scroller = new ScrollViewer { Content = _docker, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollMode = ScrollMode.Auto };
        SetRow(scroller, 2);
        Children.Add(scroller);

        SetRow(_readout, 3);
        Children.Add(_readout);
        UpdateReadout();
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
                .Select(p => $"{p.Id}({string.Join(",", p.CardIds)})")));
        var tray = layout.TrayCardIds.Count > 0 ? $"  |  tray: {string.Join(",", layout.TrayCardIds)}" : "";
        _readout.Text = string.Join("   ", columns) + tray;
    }
}
