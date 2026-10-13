
namespace Xtreme.Components.Primitives;

public sealed partial class ChipPickerControl : UserControl
{
    public ChipPickerControl()
    {
        InitializeComponent();
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
            new PropertyMetadata(true));

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

    public static readonly DependencyProperty WorkingItemsProperty =
    DependencyProperty.Register(
        nameof(WorkingItems),
        typeof(IReadOnlyList<ChipItem>),
        typeof(ChipPickerControl),
        new PropertyMetadata(null));

    public IReadOnlyList<ChipItem>? WorkingItems
    {
        get => (IReadOnlyList<ChipItem>?)GetValue(WorkingItemsProperty);
        set => SetValue(WorkingItemsProperty, value);
    }




    public static readonly DependencyProperty CalculatedMaxChipWidthProperty =
    DependencyProperty.Register(
        nameof(CalculatedMaxChipWidth),
        typeof(double),
        typeof(ChipPickerControl),
        new PropertyMetadata(200d));

    public double CalculatedMaxChipWidth
    {
        get => (double)GetValue(CalculatedMaxChipWidthProperty);
        set => SetValue(CalculatedMaxChipWidthProperty, value);
    }




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
    /// search and filters. Receives the current chips; returns the new selection (every chip selected),
    /// or null when the user cancels. The result still goes through <see cref="SaveAsync"/>.
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

    public double ChipMaxWidth
    {
        get => (double)GetValue(ChipMaxWidthProperty);
        private set => SetValue(ChipMaxWidthProperty, value);
    }

    #endregion

    #region Change handlers (single direction flow)

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

        // Title only shows when nothing is selected
        ShowTitle = selected is null || selected.Count == 0;
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
        var picked = await pick(Items ?? Array.Empty<ChipItem>());
        if (picked is null) return;

        IsBusy = true;
        try
        {
            if (SaveAsync is not null) await SaveAsync(picked);

            // A host that BINDS Items gets the new chips from its feed; assigning here would replace that
            // binding with a local value, and a later Clear on the host would no longer reach the button.
            if (ReadLocalValue(ItemsProperty) is not Microsoft.UI.Xaml.Data.BindingExpression)
                Items = picked;
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion
}
