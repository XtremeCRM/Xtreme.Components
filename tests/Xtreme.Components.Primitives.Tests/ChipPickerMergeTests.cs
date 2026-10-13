namespace Xtreme.Components.Primitives.Tests;

// The custom-picker path must hand SaveAsync the same whole-list shape the built-in dialog does.
public class ChipPickerMergeTests
{
    private static ChipItem Chip(int id, bool selected = false, bool canRemove = true, bool excluded = false)
        => new() { ChipId = id, Name = $"Chip {id}", IsSelected = selected, CanRemove = canRemove, IsExcluded = excluded };

    [Fact]
    public void Picks_only_result_becomes_the_whole_list()
    {
        var current = new[] { Chip(1, selected: true), Chip(2) };
        var picked = new[] { Chip(2, selected: true), Chip(4, selected: true) };

        var merged = ChipPickerControl.MergePicked(current, picked);

        Assert.Equal(new[] { 1, 2, 4 }, merged.Select(c => c.ChipId));
        Assert.Equal(new[] { false, true, true }, merged.Select(c => c.IsSelected));
    }

    [Fact]
    public void Whole_list_result_keeps_its_flags()
    {
        var current = new[] { Chip(1, selected: true), Chip(2) };
        var picked = new[] { Chip(1), Chip(2, selected: true) };

        var merged = ChipPickerControl.MergePicked(current, picked);

        Assert.Equal(new[] { false, true }, merged.Select(c => c.IsSelected));
    }

    [Fact]
    public void A_selected_chip_that_cannot_be_removed_stays_selected()
    {
        var current = new[] { Chip(1, selected: true, canRemove: false), Chip(2, selected: true) };

        var merged = ChipPickerControl.MergePicked(current, Array.Empty<ChipItem>());

        Assert.True(merged[0].IsSelected);
        Assert.False(merged[1].IsSelected);
    }

    [Fact]
    public void Unselected_items_drop_their_exclusion()
    {
        var current = new[] { Chip(1, selected: true, excluded: true) };
        var picked = new[] { Chip(1, selected: false, excluded: true) };

        var merged = ChipPickerControl.MergePicked(current, picked);

        Assert.False(merged[0].IsExcluded);
    }

    [Fact]
    public void The_inputs_are_not_mutated()
    {
        var current = new[] { Chip(1, selected: true) };
        ChipPickerControl.MergePicked(current, Array.Empty<ChipItem>());
        Assert.True(current[0].IsSelected);
    }
}
