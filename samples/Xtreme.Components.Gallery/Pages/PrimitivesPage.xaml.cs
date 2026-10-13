using Xtreme.Components.Core;
using Xtreme.Components.Primitives;

namespace Xtreme.Components.Gallery.Pages;

public sealed partial class PrimitivesPage : UserControl
{
    private readonly ToastControl _toast = new();

    public PrimitivesPage()
    {
        InitializeComponent();

        // The "star" icon key resolves through ComponentHost.ResolveGeometry, set once in App.OnLaunched.

        // Chip picker: items are plain records; SaveAsync runs when the user saves the dialog.
        Chips.Title = "Tags";
        Chips.Items =
        [
            new ChipItem { ChipId = 1, Name = "Residential", GroupName = "Customer" },
            new ChipItem { ChipId = 2, Name = "Commercial", GroupName = "Customer" },
            // CanRemove = false: selected and locked in, so its checkbox in the dialog is disabled.
            new ChipItem { ChipId = 3, Name = "Battery plan", GroupName = "Service", IsSelected = true, CanRemove = false },
            new ChipItem { ChipId = 4, Name = "Monitoring", GroupName = "Service" },
            new ChipItem { ChipId = 5, Name = "Past due", GroupName = "Billing", BackgroundColor = Color.FromArgb(0xFF, 0xF4, 0xC7, 0xC3) },
        ];
        // SaveAsync always receives the whole list (from the dialog or a CustomPickAsync picker); IsSelected marks the picks.
        Chips.SaveAsync = items =>
        {
            var picked = items.Where(i => i.IsSelected).ToList();
            ChipEcho.Text = picked.Count == 0 ? "Nothing selected." : "Selected: " + string.Join(", ", picked.Select(p => p.Name));
            return ValueTask.CompletedTask;
        };

        // Criteria panel: fields are ordinary elements; the label is an attached property.
        Criteria.Fields.Add(Labelled(new TextBox { PlaceholderText = "Starts with" }, "Name"));
        Criteria.Fields.Add(Labelled(new TextBox { PlaceholderText = "Any part" }, "Email"));
        Criteria.Fields.Add(Labelled(new ComboBox { ItemsSource = new[] { "Any", "Active", "Inactive" }, SelectedIndex = 0 }, "Status"));
        Criteria.Fields.Add(Labelled(new CheckBox { Content = "Has open orders" }, ""));
        Criteria.FieldValueChanged += (_, field) => CriteriaEcho.Text = $"Changed: {(CriteriaPanel.GetLabel(field) is { Length: > 0 } label ? label : field.GetType().Name)}";

        // Paging: CurrentPageChanged / PageSizeChanged fire for every change; ShownLayout says which arrangement is on screen.
        Pager.TotalItemCount = 137;
        Pager.DefaultPageSize = 25;
        void EchoPager() => PagerEcho.Text = $"Page {Pager.CurrentPage}, {Pager.PageSize} per page of {Pager.TotalItemCount} ({Pager.ShownLayout})";
        Pager.CurrentPageChanged += (_, _) => EchoPager();
        Pager.PageSizeChanged += (_, _) => EchoPager();
        Pager.RegisterPropertyChangedCallback(PagingControl.ShownLayoutProperty, (_, _) => EchoPager());
        EchoPager();

        DateTime.ValueChanged += (_, _) => DateTimeEcho.Text = $"Value: {DateTime.Date?.ToString() ?? "no date"} {DateTime.Time?.ToString() ?? ""}";
        Month.SelectedDateChanged += (_, month) => MonthEcho.Text = $"Selected: {month:yyyy-MM-dd}";

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
    private void Actions_CancelRequested(object sender, object e) { ActionEcho.Text = "Canceled."; DirtyBox.Text = ""; Actions.IsDirty = false; }
    private void Actions_DeleteRequested(object sender, object e) => ActionEcho.Text = "Delete requested (confirmed in the bar's own dialog).";

    private void Toast_Click(object sender, RoutedEventArgs e)
    {
        if (_toast.Parent is null && Content is ScrollViewer sv && sv.Content is Panel p)
            p.Children.Add(_toast);
        _toast.Show("Saved 3 changes");
    }
}
