using System.Collections.ObjectModel;
using WinUI.TableView;
using Xtreme.Components.TableView;

namespace Xtreme.Components.Gallery.Pages;

/// <summary>
/// A <see cref="WinUI.TableView.TableView"/> of sample rows with <see cref="RowFlagDecorator"/> attached.
/// Toggle a row's state with the buttons and the frame and tag follow on <see cref="RowFlagDecorator.Refresh"/>.
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

    public TableViewPage()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        var markNew = new Button { Content = "Toggle NEW on selected" };
        var markChanged = new Button { Content = "Toggle CHANGED on selected" };
        markNew.Click += (_, _) => Toggle(r => r.IsNew = !r.IsNew);
        markChanged.Click += (_, _) => Toggle(r => r.IsChanged = !r.IsChanged);
        toolbar.Children.Add(markNew);
        toolbar.Children.Add(markChanged);
        Children.Add(toolbar);

        _table.AutoGenerateColumns = true;
        _table.ItemsSource = _rows;
        _table.SelectionMode = ListViewSelectionMode.Single;
        SetRow(_table, 1);
        Children.Add(_table);

        // Brush keys are resolved for the row's own theme; the gallery defines them in App.xaml.
        RowFlagDecorator.Attach(_table, item => item switch
        {
            Row { IsNew: true } => new RowFlag("NEW", "GalleryFlagNewBrush", "Added in this session"),
            Row { IsChanged: true } => new RowFlag("CHANGED", "GalleryFlagChangedBrush", "Has unsaved edits"),
            _ => null,
        });
    }

    private void Toggle(Action<Row> change)
    {
        if (_table.SelectedItem is not Row row) return;
        change(row);
        RowFlagDecorator.Refresh(_table);
    }
}
