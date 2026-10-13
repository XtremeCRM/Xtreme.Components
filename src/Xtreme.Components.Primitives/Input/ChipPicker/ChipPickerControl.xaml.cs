
namespace Xtreme.Components.Primitives;

public sealed partial class ChipPickerControl : UserControl
{
    public ChipPickerControl()
    {
        InitializeComponent();
        ChipMaxWidth = CalculatedMaxChipWidth;
        SizeChanged += (_, _) => UpdateChipMaxWidth();
        Loaded += (_, _) => UpdateTitleVisibility();
    }

    #region Dependency Properties (public surface)

    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(
            nameof(IsBusy),
            typeof(bool),
            typeof(ChipPickerControl),
            new PropertyMetadata(false));

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(ChipPickerControl),
            new PropertyMetadata(string.Empty));
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty ShowTitleProperty =
        DependencyProperty.Register(
            nameof(ShowTitle),
            typeof(bool),
            typeof(ChipPickerControl),
            new PropertyMetadata(true, OnShowTitleChanged));

    /// <summary>
    /// Whether the <see cref="Title"/> text shows on the button. Left unset, the title shows only
    /// while nothing is selected (it reads as a placeholder). Set it (or bind it) and the control
    /// honors the value: <c>true</c> keeps the title visible beside the chips, like a label;
    /// <c>false</c> never shows it.
    /// </summary>
    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    public static readonly DependencyProperty AllowExcludeProperty =
        DependencyProperty.Register(
            nameof(AllowExclude),
            typeof(bool),
            typeof(ChipPickerControl),
            new PropertyMetadata(false));

    /// <summary>
    /// XU-517: lets the user mark a selected item as a NOT (exclude) rather than an include —
    /// the picker dialog grows a per-row NOT toggle, and excluded chips render with a
    /// circle-slash badge. Off by default: an EntityFilter's rules carry an IsNot flag so the
    /// Entity Type/Tag/Group pickers need this, but no other consumer's search supports
    /// exclusions, and offering a toggle whose value is silently dropped would be worse than
    /// not offering it.
    /// </summary>
    public bool AllowExclude
    {
        get => (bool)GetValue(AllowExcludeProperty);
        set => SetValue(AllowExcludeProperty, value);
    }

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(
            nameof(Items),
            typeof(IReadOnlyList<ChipItem>),
            typeof(ChipPickerControl),
            new PropertyMetadata(null, OnItemsChanged));

    public IReadOnlyList<ChipItem>? Items
    {
        get => (IReadOnlyList<ChipItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly DependencyProperty SelectedItemsProperty =
    DependencyProperty.Register(
        nameof(SelectedItems),
        typeof(IReadOnlyList<ChipItem>),
        typeof(ChipPickerControl),
        new PropertyMetadata(null));

    public IReadOnlyList<ChipItem>? SelectedItems
    {
        get => (IReadOnlyList<ChipItem>?)GetValue(SelectedItemsProperty);
        private set => SetValue(SelectedItemsProperty, value);
    }

    /// <summary>
    /// The dialog's editable copy of <see cref="Items"/> (each item cloned when the dialog opens).
    /// Dialog state only, so not public: hosts read the result in <see cref="SaveAsync"/>.
    /// </summary>
    internal IReadOnlyList<ChipItem>? WorkingItems { get; set; }

    public static readonly DependencyProperty CalculatedMaxChipWidthProperty =
    DependencyProperty.Register(
        nameof(CalculatedMaxChipWidth),
        typeof(double),
        typeof(ChipPickerControl),
        new PropertyMetadata(200d, OnCalculatedMaxChipWidthChanged));

    /// <summary>
    /// The widest a chip may grow on the button; longer text is trimmed with an ellipsis. The width
    /// actually applied is <see cref="ChipMaxWidth"/>, which also respects the button's own width.
    /// </summary>
    public double CalculatedMaxChipWidth
    {
        get => (double)GetValue(CalculatedMaxChipWidthProperty);
        set => SetValue(CalculatedMaxChipWidthProperty, value);
    }




    /// <summary>
    /// Called when the user saves, before the new list becomes <see cref="Items"/>. Receives the
    /// <b>whole</b> new list, selected and unselected, from either path: filter on
    /// <see cref="ChipItem.IsSelected"/> for the picks. Built-in dialog: its working list.
    /// <see cref="CustomPickAsync"/>: the current items with IsSelected taken from the picker's result,
    /// plus any picked item that was not in <see cref="Items"/> (appended).
    /// </summary>
    public static readonly DependencyProperty SaveAsyncProperty =
        DependencyProperty.Register(
            nameof(SaveAsync),
            typeof(Func<IReadOnlyList<ChipItem>, ValueTask>),
            typeof(ChipPickerControl),
            new PropertyMetadata(null));

    public Func<IReadOnlyList<ChipItem>, ValueTask>? SaveAsync
    {
        get => (Func<IReadOnlyList<ChipItem>, ValueTask>?)
            GetValue(SaveAsyncProperty);
        set => SetValue(SaveAsyncProperty, value);
    }

    public static readonly DependencyProperty CustomPickAsyncProperty =
        DependencyProperty.Register(
            nameof(CustomPickAsync),
            typeof(Func<IReadOnlyList<ChipItem>, Task<IReadOnlyList<ChipItem>?>>),
            typeof(ChipPickerControl),
            new PropertyMetadata(null));

    /// <summary>
    /// XU-583: replaces the built-in checkbox dialog with the host's own picker — the Advanced Search
    /// Product criterion opens the reusable Product Picker, because a catalog of thousands needs its
    /// search and filters. Receives the current chips; returns the picked items with
    /// <see cref="ChipItem.IsSelected"/> true (returning the whole list with each item's IsSelected
    /// set also works), or null when the user cancels. The result is merged into <see cref="Items"/>
    /// (see <see cref="SaveAsync"/>), so the host gets the same full-list shape as from the built-in dialog.
    /// </summary>
    public Func<IReadOnlyList<ChipItem>, Task<IReadOnlyList<ChipItem>?>>? CustomPickAsync
    {
        get => (Func<IReadOnlyList<ChipItem>, Task<IReadOnlyList<ChipItem>?>>?)GetValue(CustomPickAsyncProperty);
        set => SetValue(CustomPickAsyncProperty, value);
    }

    #endregion

    #region Internal sizing state
    public static readonly DependencyProperty ChipMaxWidthProperty =
        DependencyProperty.Register(
            nameof(ChipMaxWidth),
            typeof(double),
            typeof(ChipPickerControl),
            new PropertyMetadata(0d));

    /// <summary>
    /// Read-only. The max width applied to each chip: <see cref="CalculatedMaxChipWidth"/>, reduced to
    /// the room the button has for chips so a single long chip trims instead of overflowing.
    /// </summary>
    public double ChipMaxWidth
    {
        get => (double)GetValue(ChipMaxWidthProperty);
        private set => SetValue(ChipMaxWidthProperty, value);
    }

    #endregion

    #region Change handlers (single direction flow)

    private static void OnShowTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChipPickerControl control)
            control.UpdateTitleVisibility();
    }

    private static void OnCalculatedMaxChipWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChipPickerControl control)
            control.UpdateChipMaxWidth();
    }

    private static void OnItemsChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is ChipPickerControl control)
        {
            control.UpdateSelectedItems();
        }
    }

    #endregion
    private void UpdateSelectedItems()
    {
        var selected = Items?
            .Where(i => i.IsSelected)
            .ToList();

        SelectedItems = selected;
        UpdateTitleVisibility();
    }

    // An explicit (local or bound) ShowTitle wins; unset, the title is a placeholder that shows
    // only while nothing is selected. Computed here rather than written back to ShowTitle, so a
    // host's value is never overwritten.
    private void UpdateTitleVisibility()
    {
        if (TitleText is null) return;
        var explicitlySet = ReadLocalValue(ShowTitleProperty) != DependencyProperty.UnsetValue;
        var show = explicitlySet ? ShowTitle : SelectedItems is null || SelectedItems.Count == 0;
        TitleText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UpdateChipMaxWidth();
    }

    private void UpdateChipMaxWidth()
    {
        var max = Math.Max(0, CalculatedMaxChipWidth);
        if (DropdownButton is { ActualWidth: > 0 } button)
        {
            // Room left for chips: the button minus its padding/border and a visible title.
            var room = button.ActualWidth
                       - button.Padding.Left - button.Padding.Right
                       - button.BorderThickness.Left - button.BorderThickness.Right
                       - (TitleText is { Visibility: Visibility.Visible } t ? t.ActualWidth + 4 : 0);
            if (room > 24) max = Math.Min(max, room);
        }
        if (Math.Abs(ChipMaxWidth - max) > 0.5)
            ChipMaxWidth = max;
    }

    #region Commands (existing behavior preserved)

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (CustomPickAsync is { } pick)
        {
            await OpenCustomPickerAsync(pick);
            return;
        }

        if (Items is null) // || SaveAsync is null)
            return;

        // Create working copy owned by the control
        WorkingItems = Items
            .Select(i => i with { })
            .ToList();

        var dlg = new ChipPickerDialog
        {
            XamlRoot = this.XamlRoot,
            // XU-517: match the field ("Type…" → "Select Type") instead of the generic
            // "Select items" — with several of these pickers on one panel (Type/Tag/Group),
            // the dialog otherwise gives no clue which field is open.
            Title = string.IsNullOrWhiteSpace(Title) ? "Select items" : $"Select {Title.TrimEnd('…', '.', ' ')}",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            DataContext = this
        };

        IsBusy = false;

        var result = await dlg.ShowAsync();

        if (result == ContentDialogResult.Primary && WorkingItems is not null)
        {
            IsBusy = true;
            try
            {
                if (SaveAsync is not null) await SaveAsync(WorkingItems);

                // XU-517: reflect the new selection immediately. A host bound to a live IFeed
                // (Advanced Search) gets this for free when its state updates, but a host that
                // pushes Items imperatively (EntitySearchConfigureControl, EntityPickerControl)
                // never sees anything change unless we push it here too — otherwise the button
                // silently keeps showing the placeholder/old chips after Save.
                Items = WorkingItems;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    // XU-583: the host's picker path. Items may still be null here (the host's feed has not emitted yet),
    // which the checkbox dialog treats as nothing-to-show; a picker brings its own list, so it opens anyway.
    private async Task OpenCustomPickerAsync(Func<IReadOnlyList<ChipItem>, Task<IReadOnlyList<ChipItem>?>> pick)
    {
        var current = Items ?? Array.Empty<ChipItem>();
        var picked = await pick(current);
        if (picked is null) return;

        var merged = MergePicked(current, picked);

        IsBusy = true;
        try
        {
            if (SaveAsync is not null) await SaveAsync(merged);

            // A host that BINDS Items gets the new chips from its feed; assigning here would replace that
            // binding with a local value, and a later Clear on the host would no longer reach the button.
            if (ReadLocalValue(ItemsProperty) is not Microsoft.UI.Xaml.Data.BindingExpression)
                Items = merged;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The full-list shape <see cref="SaveAsync"/> receives from the custom-picker path: every current
    /// item, with IsSelected / IsExcluded taken from the picker's version of it (matched by
    /// <see cref="ChipItem.ChipId"/>; absent means unselected), then any picked item that was not in the
    /// current list. A selected item with <see cref="ChipItem.CanRemove"/> false stays selected.
    /// </summary>
    internal static IReadOnlyList<ChipItem> MergePicked(IReadOnlyList<ChipItem> current, IReadOnlyList<ChipItem> picked)
    {
        var byId = new Dictionary<int, ChipItem>();
        foreach (var p in picked)
            byId.TryAdd(p.ChipId, p);

        var result = new List<ChipItem>(current.Count + picked.Count);
        var seen = new HashSet<int>();
        foreach (var c in current)
        {
            if (!seen.Add(c.ChipId)) continue;
            var locked = c.IsSelected && !c.CanRemove;
            if (byId.TryGetValue(c.ChipId, out var p))
            {
                var selected = p.IsSelected || locked;
                result.Add(p with { IsSelected = selected, IsExcluded = selected && p.IsExcluded });
            }
            else
            {
                result.Add(c with { IsSelected = locked, IsExcluded = locked && c.IsExcluded });
            }
        }
        foreach (var p in picked)
        {
            if (seen.Add(p.ChipId))
                result.Add(p with { });
        }
        return result;
    }

    #endregion
}
