using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;

namespace Xtreme.Components.Primitives;

public sealed partial class ChipPickerDialog : ContentDialog
{
    // Save stays disabled until the selection actually differs from what the dialog
    // opened with — the Shell-wide save-disabled-until-dirty standard. A set compare
    // (not a click counter) so toggling a chip off and back on correctly reads clean.
    // XU-517: the compared value is (id, excluded), so flipping a selected chip to NOT and
    // back also reads clean, and flipping it to NOT alone reads dirty.
    private HashSet<(int Id, bool Excluded)> _baselineSelection = [];

    public ChipPickerDialog()
    {
        this.InitializeComponent();
        // XU-516: RequestedTheme set on the window's root visual (Dark/High-Contrast) does not
        // reliably reach this dialog's own popup — force it explicitly.
        Xtreme.Components.Core.ComponentHost.PrepareDialog(this);

        IsPrimaryButtonEnabled = false;
        Opened += OnOpened;
    }

    private ChipPickerControl? Host => DataContext as ChipPickerControl;

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // Template is chosen here rather than bound: Uno does not reliably resolve an
        // ElementName reference out of a DataTemplate, and AllowExclude lives on the host
        // control, not on the row's ChipItem.
        var rowTemplate = (DataTemplate)Resources[
            Host?.AllowExclude == true ? "ChipRowWithExcludeTemplate" : "ChipRowTemplate"];
        // XU-586: header rows and chip rows share one list, so the template is chosen per item.
        ChipRows.ItemTemplateSelector = new ChipRowTemplateSelector(
            (DataTemplate)Resources["ChipGroupHeaderTemplate"], rowTemplate);
        Render();

        _baselineSelection = CurrentSelection();
        IsPrimaryButtonEnabled = false;
    }

    private HashSet<(int Id, bool Excluded)> CurrentSelection()
        => Host?.WorkingItems?
               .Where(i => i.IsSelected)
               .Select(i => (i.ChipId, i.IsExcluded))
               .ToHashSet()
           ?? [];

    private void OnChipToggled(object sender, RoutedEventArgs e)
    {
        // Deselecting drops any NOT with it — an unselected exclusion is not a state the
        // search can express, and leaving it set would resurrect on re-check.
        if (sender is CheckBox { IsChecked: false } cb && cb.DataContext is ChipItem vm && vm.IsExcluded)
        {
            vm.IsExcluded = false;
            RebuildRows();
        }
        // A CanRemove = false item locks as soon as it is selected; re-render so its row disables.
        else if (sender is CheckBox { IsChecked: true, DataContext: ChipItem { CanRemove: false } })
        {
            RebuildRows();
        }

        UpdateDirty();
    }

    private void OnExcludeToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb || tb.DataContext is not ChipItem vm) return;

        vm.IsExcluded = tb.IsChecked == true;
        // NOT implies the item is part of the criteria — one click to say "exclude this"
        // instead of check-then-exclude.
        if (vm.IsExcluded) vm.IsSelected = true;

        RebuildRows();
        UpdateDirty();
    }

    // ChipItem carries no change notification (it is a plain record shared by ~18 call sites, and
    // adding INotifyPropertyChanged would leak subscribers through the `with { }` clones the
    // host makes). Re-seating the same instances in a fresh list is enough to re-render every
    // row against current model state.
    private void RebuildRows()
    {
        if (Host?.WorkingItems is not { } items) return;
        Host.WorkingItems = items.ToList();
        Render();
    }

    private void UpdateDirty()
        => IsPrimaryButtonEnabled = !CurrentSelection().SetEquals(_baselineSelection);

    /// <summary>
    /// XU-586: show the working items, under a header per <see cref="ChipItem.GroupName"/> when the host
    /// grouped them (entity tags by EntityType), or as the plain list every other picker gets.
    /// </summary>
    private void Render()
    {
        var items = Host?.WorkingItems ?? [];
        ChipRows.ItemsSource = items.Any(i => !string.IsNullOrEmpty(i.GroupName))
            ? Grouped(items)
            : items.Cast<object>().ToList();
    }

    // Groups keep the order the host gave them (GroupBy preserves first appearance).
    private static List<object> Grouped(IReadOnlyList<ChipItem> items)
    {
        var rows = new List<object>(items.Count + 8);
        foreach (var group in items.GroupBy(i => i.GroupName ?? string.Empty))
        {
            rows.Add(new ChipGroupHeader(string.IsNullOrEmpty(group.Key) ? "Other" : group.Key));
            rows.AddRange(group);
        }
        return rows;
    }

    private sealed class ChipRowTemplateSelector(DataTemplate header, DataTemplate row) : DataTemplateSelector
    {
        protected override DataTemplate SelectTemplateCore(object item)
            => item is ChipGroupHeader ? header : row;

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
            => SelectTemplateCore(item);
    }
}

/// <summary>
/// Enables a dialog row unless its <see cref="ChipItem"/> is selected and cannot be removed
/// (<see cref="ChipItem.CanRemove"/> = false).
/// </summary>
internal sealed class ChipRemovableConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not ChipItem { IsSelected: true, CanRemove: false };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// XU-586: a section-header row in a grouped chip picker. Reached only through binding, hence the
/// trim annotation (XU-437: WASM drops binding-only members otherwise).
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public sealed record ChipGroupHeader(string Name);
