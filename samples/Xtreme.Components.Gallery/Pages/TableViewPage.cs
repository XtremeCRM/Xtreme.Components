using System.Collections.ObjectModel;
using WinUI.TableView;
using Xtreme.Components.TableView;

namespace Xtreme.Components.Gallery.Pages;

/// <summary>
/// A <see cref="WinUI.TableView.TableView"/> of sample rows with <see cref="RowFlagDecorator"/> attached.
/// Toggle a row's state with the buttons and the frame and tag follow on <see cref="RowFlagDecorator.Refresh"/>;
/// the switches detach the decorator and swap in a custom <see cref="RowFlagOptions"/> look.
/// </summary>
public sealed partial class TableViewPage : Grid
{
    public sealed class Row
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int Orders { get; set; }
        public bool IsNew { get; set; }
        public bool IsChanged { get; set; }
    }

    private readonly ObservableCollection<Row> _rows =
    [
        new() { Name = "Alex Rivera",   Type = "Customer", Orders = 12 },
        new() { Name = "Bailey Chen",   Type = "Customer", Orders = 3, IsChanged = true },
        new() { Name = "Casey Morgan",  Type = "Vendor",   Orders = 0, IsNew = true },
        new() { Name = "Dana Whitfield", Type = "Customer", Orders = 27 },
        new() { Name = "Eli Novak",     Type = "Employee", Orders = 0 },
        new() { Name = "Farah Osei",    Type = "Customer", Orders = 8 },
    ];

    private readonly WinUI.TableView.TableView _table = new();
    private readonly ToggleSwitch _decorated = new() { Header = "Decorator", IsOn = true };
    private readonly ToggleSwitch _customLook = new() { Header = "Custom look" };

    // A heavier frame, rounded corners and a larger tag; brush keys still resolve per theme.
    private static readonly RowFlagOptions CustomLook = new()
    {
        FrameThickness = 3,
        FrameCornerRadius = new CornerRadius(4),
        TagFontSize = 10,
        TagPadding = new Thickness(8, 1, 8, 2),
        TagCornerRadius = new CornerRadius(0, 6, 0, 4),
    };

    public TableViewPage()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        var markNew = new Button { Content = "Toggle NEW on selected", VerticalAlignment = VerticalAlignment.Bottom };
        var markChanged = new Button { Content = "Toggle CHANGED on selected", VerticalAlignment = VerticalAlignment.Bottom };
        markNew.Click += (_, _) => Toggle(r => r.IsNew = !r.IsNew);
        markChanged.Click += (_, _) => Toggle(r => r.IsChanged = !r.IsChanged);
        _decorated.Toggled += (_, _) => ApplyDecorator();
        _customLook.Toggled += (_, _) => ApplyDecorator();
        toolbar.Children.Add(markNew);
        toolbar.Children.Add(markChanged);
        toolbar.Children.Add(_decorated);
        toolbar.Children.Add(_customLook);
        Children.Add(toolbar);

        _table.AutoGenerateColumns = true;
        _table.ItemsSource = _rows;
        _table.SelectionMode = ListViewSelectionMode.Single;
        SetRow(_table, 1);
        Children.Add(_table);

        ApplyDecorator();
    }

    // Brush keys are resolved for the row's own theme; the gallery defines them in App.xaml.
    private static RowFlag? FlagOf(object? item) => item switch
    {
        Row { IsNew: true } => new RowFlag("NEW", "GalleryFlagNewBrush", "Added in this session"),
        Row { IsChanged: true } => new RowFlag("CHANGED", "GalleryFlagChangedBrush", "Has unsaved edits"),
        _ => null,
    };

    // Attaching again replaces the previous registration, so switching looks needs no Detach first.
    private void ApplyDecorator()
    {
        if (!_decorated.IsOn)
            RowFlagDecorator.Detach(_table);
        else if (_customLook.IsOn)
            RowFlagDecorator.Attach(_table, FlagOf, CustomLook);
        else
            RowFlagDecorator.Attach(_table, FlagOf);
    }

    private void Toggle(Action<Row> change)
    {
        if (_table.SelectedItem is not Row row) return;
        change(row);
        RowFlagDecorator.Refresh(_table);
    }

    /// <summary>
    /// <c>--selftest</c> checks for the decorator on the live table: flagged rows show their tag and
    /// leave the row border alone, re-attaching replaces the handler, a tag's hit-testing follows its
    /// flag, and detaching clears every realized row. Returns the failures (empty = all good).
    /// Leaves the page as it found it.
    /// </summary>
    public async Task<IReadOnlyList<string>> SelfCheckAsync()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }

        var bailey = _rows[1];
        var alex = _rows[0];

        // 1. Flagged row: tag shown; flagged and unflagged rows keep the same (untouched) border.
        var flagged = _table.ContainerFromItem(bailey) as Control;
        var plain = _table.ContainerFromItem(alex) as Control;
        Check(flagged is not null && plain is not null, "rows are realized");
        if (flagged is null || plain is null)
            return failures;
        Check(VisibleTag(flagged, "CHANGED") is not null, "flagged row shows its CHANGED tag");
        Check(VisibleTag(plain, "CHANGED") is null && VisibleTag(plain, "NEW") is null, "unflagged row shows no tag");
        Check(flagged.BorderThickness == plain.BorderThickness, "flag does not change the row's BorderThickness");
        Check(RowFlagDecorator.IsAttached(_table), "IsAttached after Attach");

        // 2. A second Attach replaces the first: after re-binding, only the newest flagOf runs.
        var firstCalls = 0;
        var secondCalls = 0;
        RowFlagDecorator.Attach(_table, item => { firstCalls++; return FlagOf(item); });
        RowFlagDecorator.Attach(_table, item => { secondCalls++; return FlagOf(item) is { } f ? f with { Tip = null } : null; });
        firstCalls = 0;
        _table.ItemsSource = null;
        _table.ItemsSource = _rows;
        await Task.Delay(400);
        _table.UpdateLayout();
        Check(firstCalls == 0, $"replaced handler stays quiet after re-attach (ran {firstCalls}x)");
        Check(secondCalls > 0, "replacing handler paints rows");

        // 3. Tip-less flag: the (recycled) tag no longer intercepts pointer input.
        flagged = _table.ContainerFromItem(bailey) as Control;
        var tipless = flagged is null ? null : VisibleTag(flagged, "CHANGED");
        Check(tipless is not null && !tipless.IsHitTestVisible, "a tag without a tooltip is not hit-testable");
        ApplyDecorator();   // back to the page's own registration (tips on)
        tipless = flagged is null ? null : VisibleTag(flagged, "CHANGED");
        Check(tipless is not null && tipless.IsHitTestVisible, "a tag with a tooltip is hit-testable again");

        // 4. Detach clears realized rows; re-attach paints them again.
        Check(RowFlagDecorator.Detach(_table), "Detach reports it removed the decorator");
        Check(!RowFlagDecorator.Detach(_table), "a second Detach reports nothing to remove");
        Check(flagged is null || VisibleTag(flagged, "CHANGED") is null, "Detach clears the tag");
        ApplyDecorator();
        Check(flagged is null || VisibleTag(flagged, "CHANGED") is not null, "re-Attach repaints the tag");

        return failures;
    }

    // The tag border whose text is label, when it is visible in the container's tree.
    private static Border? VisibleTag(DependencyObject root, string label)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Border { Child: TextBlock tb, Visibility: Visibility.Visible } b && tb.Text == label)
                return b;
            if (child is UIElement { Visibility: Visibility.Collapsed })
                continue;
            if (VisibleTag(child, label) is { } found)
                return found;
        }
        return null;
    }
}
