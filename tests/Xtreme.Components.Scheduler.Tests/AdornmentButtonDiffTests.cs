using Xtreme.Components.Scheduler.Controls;
using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Tests;

/// <summary>
/// <see cref="SchedulerToolbar.DiffersOnlyInEnabled"/>: when a re-supplied adornment group can be updated in
/// place (only IsEnabled changed) instead of rebuilt (XU-1264: the rebuild cost hundreds of ms per selection
/// change with a few dozen image templates).
/// </summary>
public sealed class AdornmentButtonDiffTests
{
    private static readonly byte[] Icon = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private static SchedulerToolbarButton Button(string id, bool enabled = true, byte[]? image = null, string? label = null) => new()
    {
        Id = id,
        Label = label ?? id,
        Tooltip = id,
        Image = image ?? Icon,
        IsEnabled = enabled,
    };

    [Fact]
    public void SameEntriesWithEnabledFlippedUpdateInPlace()
    {
        SchedulerToolbarButton[] current = [Button("a", enabled: false), Button("b", enabled: false)];
        SchedulerToolbarButton[] next = [Button("a"), Button("b")];

        Assert.True(SchedulerToolbar.DiffersOnlyInEnabled(current, next));
    }

    [Fact]
    public void EqualImageBytesInANewArrayStillCountAsTheSameEntry()
    {
        SchedulerToolbarButton[] current = [Button("a", image: [1, 2, 3])];
        SchedulerToolbarButton[] next = [Button("a", image: [1, 2, 3])];

        Assert.True(SchedulerToolbar.DiffersOnlyInEnabled(current, next));
    }

    [Fact]
    public void BothEmptyIsNoChange()
    {
        Assert.True(SchedulerToolbar.DiffersOnlyInEnabled([], []));
    }

    [Fact]
    public void FirstSupplyAfterEmptyRebuilds()
    {
        Assert.False(SchedulerToolbar.DiffersOnlyInEnabled([], [Button("a")]));
    }

    [Fact]
    public void AddedOrRemovedEntryRebuilds()
    {
        Assert.False(SchedulerToolbar.DiffersOnlyInEnabled([Button("a")], [Button("a"), Button("b")]));
        Assert.False(SchedulerToolbar.DiffersOnlyInEnabled([Button("a"), Button("b")], [Button("a")]));
    }

    [Fact]
    public void ReorderedEntriesRebuild()
    {
        Assert.False(SchedulerToolbar.DiffersOnlyInEnabled([Button("a"), Button("b")], [Button("b"), Button("a")]));
    }

    [Fact]
    public void ChangedLabelOrImageRebuilds()
    {
        Assert.False(SchedulerToolbar.DiffersOnlyInEnabled([Button("a")], [Button("a", label: "renamed")]));
        Assert.False(SchedulerToolbar.DiffersOnlyInEnabled([Button("a")], [Button("a", image: [9, 9])]));
    }

    [Fact]
    public void ToggledCheckStateRebuilds()
    {
        SchedulerToolbarButton[] current = [new() { Id = "urgent", IsToggle = true, IsChecked = false }];
        SchedulerToolbarButton[] next = [new() { Id = "urgent", IsToggle = true, IsChecked = true }];

        Assert.False(SchedulerToolbar.DiffersOnlyInEnabled(current, next));
    }
}
