using Xtreme.Components.Core;
using Xtreme.Components.Primitives;

namespace Xtreme.Components.Gallery.Pages;

public sealed partial class PrimitivesPage : UserControl
{
    private readonly ToastControl _toast = new();

    public PrimitivesPage()
    {
        InitializeComponent();

        // Vector icon keys resolve through the host hook; the gallery knows one.
        ComponentHost.ResolveGeometry = key => key == "star"
            ? (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
                "M12,2 L15,9 L22,9.5 L16.5,14.5 L18,22 L12,18 L6,22 L7.5,14.5 L2,9.5 L9,9 Z")
            : null;

        // Chip picker: items are plain records; SaveAsync receives the picked set when the dialog closes.
        Chips.Title = "Tags";
        Chips.Items =
        [
            new ChipItem { ChipId = 1, Name = "Residential", GroupName = "Customer" },
            new ChipItem { ChipId = 2, Name = "Commercial", GroupName = "Customer" },
            new ChipItem { ChipId = 3, Name = "Battery plan", GroupName = "Service", IsSelected = true },
            new ChipItem { ChipId = 4, Name = "Monitoring", GroupName = "Service" },
            new ChipItem { ChipId = 5, Name = "Past due", GroupName = "Billing", BackgroundColor = Color.FromArgb(0xFF, 0xF4, 0xC7, 0xC3) },
        ];
        Chips.SaveAsync = picked =>
        {
            ChipEcho.Text = picked.Count == 0 ? "Nothing selected." : "Selected: " + string.Join(", ", picked.Select(p => p.Name));
            return ValueTask.CompletedTask;
        };

        // Criteria panel: fields are ordinary elements; the label is an attached property.
        Criteria.Fields.Add(Labelled(new TextBox { PlaceholderText = "Starts with" }, "Name"));
        Criteria.Fields.Add(Labelled(new TextBox { PlaceholderText = "Any part" }, "Email"));
        Criteria.Fields.Add(Labelled(new ComboBox { ItemsSource = new[] { "Any", "Active", "Inactive" }, SelectedIndex = 0 }, "Status"));
        Criteria.Fields.Add(Labelled(new CheckBox { Content = "Has open orders" }, ""));

        // Paging: CurrentPage and PageSize are dependency properties; watch them for a read-out.
        Pager.TotalItemCount = 137;
        Pager.PageSize = 25;
        void EchoPager() => PagerEcho.Text = $"Page {Pager.CurrentPage}, {Pager.PageSize} per page of {Pager.TotalItemCount}";
        Pager.RegisterPropertyChangedCallback(PagingControl.CurrentPageProperty, (_, _) => EchoPager());
        Pager.RegisterPropertyChangedCallback(PagingControl.PageSizeProperty, (_, _) => EchoPager());
        EchoPager();

        DateTime.ValueChanged += (_, _) => { };

        foreach (var word in new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa" })
            Wrap.Children.Add(new Border
            {
                Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
                Child = new TextBlock { Text = word },
            });

        Wrap.ColumnSpacing = 6;
        Wrap.RowSpacing = 6;
    }

    private static FrameworkElement Labelled(FrameworkElement field, string label)
    {
        CriteriaPanel.SetLabel(field, label);
        return field;
    }

    private void TimeBox_TimeCommitted(object sender, object e) => TimeEcho.Text = $"Committed: {TimeBox.Time}";

    private void DirtyBox_TextChanged(object sender, TextChangedEventArgs e) => Actions.IsDirty = DirtyBox.Text.Length > 0;

    private void Actions_SaveRequested(object sender, object e) { ActionEcho.Text = "Saved."; Actions.IsDirty = false; }
    private void Actions_CancelRequested(object sender, object e) { ActionEcho.Text = "Cancelled."; DirtyBox.Text = ""; Actions.IsDirty = false; }
    private void Actions_DeleteRequested(object sender, object e) => ActionEcho.Text = "Delete requested (confirmed in the bar's own dialog).";

    private void Toast_Click(object sender, RoutedEventArgs e)
    {
        if (_toast.Parent is null && Content is ScrollViewer sv && sv.Content is Panel p)
            p.Children.Add(_toast);
        _toast.Show("Saved 3 changes");
    }
}
