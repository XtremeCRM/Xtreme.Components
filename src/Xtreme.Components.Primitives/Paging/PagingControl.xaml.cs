using System.Globalization;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Xtreme.Components.Primitives;

public sealed partial class PagingControl : UserControl
{
    private static readonly IReadOnlyList<uint> DefaultPageSizeOptions = new uint[] { 10, 25, 50, 100 };

    // Single suppression flag shared across all three combos (inline / overflow / compact).
    // Rebuilds are sequential and never run concurrently, so one flag is sufficient.
    private bool _suppressComboEvents;
    private bool _effectiveCompact;
    private bool _effectiveOverflow;

    // PageSize follows DefaultPageSize until anything else writes PageSize (the host, a binding, the
    // size selector). _applyingDefault marks the control's own write so it does not count as "else".
    private bool _pageSizeTouched;
    private bool _applyingDefault;

    // True while the "All" page-size option is selected. In that mode PageSize tracks
    // TotalItemCount so the whole set renders on a single page; we re-apply it whenever
    // the total changes (e.g. a new search loads a different row count).
    private bool _allSelected;

    public PagingControl()
    {
        this.InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Raised after <see cref="CurrentPage"/> changes, whatever changed it: a page button, Go to, the
    /// host, or the control itself (clamping to the page count, keeping the first row on screen after a
    /// page-size change). Read <see cref="CurrentPage"/> in the handler.
    /// </summary>
    public event EventHandler? CurrentPageChanged;

    /// <summary>
    /// Raised after <see cref="PageSize"/> changes (the size selector, "All", the host, or
    /// <see cref="DefaultPageSize"/>). A size change that also moves the current page raises
    /// <see cref="CurrentPageChanged"/> first.
    /// </summary>
    public event EventHandler? PageSizeChanged;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RebuildPageSizeCombo();
        EnsurePageSizeWithinOptions();
        Refresh();
    }

    #region Dependency properties

    public static readonly DependencyProperty TotalItemCountProperty = DependencyProperty.Register(
        nameof(TotalItemCount), typeof(uint), typeof(PagingControl),
        new PropertyMetadata(0u, OnAnyChanged));

    public uint TotalItemCount
    {
        get => (uint)GetValue(TotalItemCountProperty);
        set => SetValue(TotalItemCountProperty, value);
    }

    public static readonly DependencyProperty CurrentPageProperty = DependencyProperty.Register(
        nameof(CurrentPage), typeof(uint), typeof(PagingControl),
        new PropertyMetadata(1u, OnCurrentPageChanged));

    public uint CurrentPage
    {
        get => (uint)GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public static readonly DependencyProperty PageSizeProperty = DependencyProperty.Register(
        nameof(PageSize), typeof(uint), typeof(PagingControl),
        new PropertyMetadata(10u, OnPageSizeChanged));

    /// <summary>
    /// Rows per page. Starts at <see cref="DefaultPageSize"/> and follows it until something else sets
    /// PageSize; from then on PageSize wins. Setting 0 snaps to <see cref="DefaultPageSize"/> (or 10).
    /// </summary>
    public uint PageSize
    {
        get => (uint)GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public static readonly DependencyProperty PageSizeOptionsProperty = DependencyProperty.Register(
        nameof(PageSizeOptions), typeof(IList<uint>), typeof(PagingControl),
        new PropertyMetadata(null, OnPageSizeOptionsChanged));

    public IList<uint>? PageSizeOptions
    {
        get => (IList<uint>?)GetValue(PageSizeOptionsProperty);
        set => SetValue(PageSizeOptionsProperty, value);
    }

    public static readonly DependencyProperty DefaultPageSizeProperty = DependencyProperty.Register(
        nameof(DefaultPageSize), typeof(uint), typeof(PagingControl),
        new PropertyMetadata(10u, OnDefaultPageSizeChanged));

    /// <summary>
    /// The starting page size. While <see cref="PageSize"/> has not been set by anything else (host
    /// code, a binding, the user's pick), changing DefaultPageSize also moves PageSize; once PageSize
    /// has been set, it wins and DefaultPageSize is only the value a PageSize of 0 snaps to.
    /// </summary>
    public uint DefaultPageSize
    {
        get => (uint)GetValue(DefaultPageSizeProperty);
        set => SetValue(DefaultPageSizeProperty, value);
    }

    public static readonly DependencyProperty ShowAllOptionProperty = DependencyProperty.Register(
        nameof(ShowAllOption), typeof(bool), typeof(PagingControl),
        new PropertyMetadata(false, OnShowAllOptionChanged));

    /// <summary>
    /// When true, an "All" entry is appended to the page-size selector. Choosing it sizes
    /// the page to the full result set (one page). Opt-in (default false) so existing
    /// consumers are unaffected; set true on grids that hold the whole set client-side.
    /// </summary>
    public bool ShowAllOption
    {
        get => (bool)GetValue(ShowAllOptionProperty);
        set => SetValue(ShowAllOptionProperty, value);
    }

    public static readonly DependencyProperty ShowPageSizeSelectorProperty = DependencyProperty.Register(
        nameof(ShowPageSizeSelector), typeof(bool), typeof(PagingControl),
        new PropertyMetadata(true, OnAnyChanged));

    public bool ShowPageSizeSelector
    {
        get => (bool)GetValue(ShowPageSizeSelectorProperty);
        set => SetValue(ShowPageSizeSelectorProperty, value);
    }

    public static readonly DependencyProperty ShowFirstButtonProperty = DependencyProperty.Register(
        nameof(ShowFirstButton), typeof(bool), typeof(PagingControl),
        new PropertyMetadata(true, OnAnyChanged));

    public bool ShowFirstButton
    {
        get => (bool)GetValue(ShowFirstButtonProperty);
        set => SetValue(ShowFirstButtonProperty, value);
    }

    public static readonly DependencyProperty ShowLastButtonProperty = DependencyProperty.Register(
        nameof(ShowLastButton), typeof(bool), typeof(PagingControl),
        new PropertyMetadata(true, OnAnyChanged));

    public bool ShowLastButton
    {
        get => (bool)GetValue(ShowLastButtonProperty);
        set => SetValue(ShowLastButtonProperty, value);
    }

    public static readonly DependencyProperty ShowNumberedPagesProperty = DependencyProperty.Register(
        nameof(ShowNumberedPages), typeof(bool), typeof(PagingControl),
        new PropertyMetadata(true, OnAnyChanged));

    public bool ShowNumberedPages
    {
        get => (bool)GetValue(ShowNumberedPagesProperty);
        set => SetValue(ShowNumberedPagesProperty, value);
    }

    public static readonly DependencyProperty ShowJumpToPageProperty = DependencyProperty.Register(
        nameof(ShowJumpToPage), typeof(bool), typeof(PagingControl),
        new PropertyMetadata(true, OnAnyChanged));

    public bool ShowJumpToPage
    {
        get => (bool)GetValue(ShowJumpToPageProperty);
        set => SetValue(ShowJumpToPageProperty, value);
    }

    public static readonly DependencyProperty ShowSummaryProperty = DependencyProperty.Register(
        nameof(ShowSummary), typeof(bool), typeof(PagingControl),
        new PropertyMetadata(false, OnAnyChanged));

    public bool ShowSummary
    {
        get => (bool)GetValue(ShowSummaryProperty);
        set => SetValue(ShowSummaryProperty, value);
    }

    public static readonly DependencyProperty SummaryFormatProperty = DependencyProperty.Register(
        nameof(SummaryFormat), typeof(string), typeof(PagingControl),
        new PropertyMetadata("{0}-{1} of {2}", OnAnyChanged));

    public string SummaryFormat
    {
        get => (string)GetValue(SummaryFormatProperty);
        set => SetValue(SummaryFormatProperty, value);
    }

    public static readonly DependencyProperty MaxNumberedPagesProperty = DependencyProperty.Register(
        nameof(MaxNumberedPages), typeof(int), typeof(PagingControl),
        new PropertyMetadata(7, OnAnyChanged));

    public int MaxNumberedPages
    {
        get => (int)GetValue(MaxNumberedPagesProperty);
        set => SetValue(MaxNumberedPagesProperty, value);
    }

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(PagingControl),
        new PropertyMetadata(false, OnAnyChanged));

    /// <summary>
    /// True forces compact mode (shorthand for <see cref="LayoutMode"/> = Compact, and it wins over
    /// LayoutMode and <see cref="IsOverflow"/>). False forces nothing: <see cref="LayoutMode"/> decides.
    /// </summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public static readonly DependencyProperty CompactThresholdProperty = DependencyProperty.Register(
        nameof(CompactThreshold), typeof(double), typeof(PagingControl),
        new PropertyMetadata(360d, OnAnyChanged));

    public double CompactThreshold
    {
        get => (double)GetValue(CompactThresholdProperty);
        set => SetValue(CompactThresholdProperty, value);
    }

    public static readonly DependencyProperty IsOverflowProperty = DependencyProperty.Register(
        nameof(IsOverflow), typeof(bool), typeof(PagingControl),
        new PropertyMetadata(false, OnAnyChanged));

    /// <summary>
    /// True forces the intermediate "overflow" mode, where the page-size selector and Go-to-page
    /// field collapse into a flyout button while the navigation buttons remain inline (shorthand for
    /// <see cref="LayoutMode"/> = Overflow; <see cref="IsCompact"/> wins over it). False forces nothing.
    /// </summary>
    public bool IsOverflow
    {
        get => (bool)GetValue(IsOverflowProperty);
        set => SetValue(IsOverflowProperty, value);
    }

    public static readonly DependencyProperty OverflowThresholdProperty = DependencyProperty.Register(
        nameof(OverflowThreshold), typeof(double), typeof(PagingControl),
        new PropertyMetadata(560d, OnAnyChanged));

    /// <summary>
    /// Width below which the control auto-flips to overflow mode (secondary
    /// controls behind a flyout). Must be greater than CompactThreshold.
    /// </summary>
    public double OverflowThreshold
    {
        get => (double)GetValue(OverflowThresholdProperty);
        set => SetValue(OverflowThresholdProperty, value);
    }

    public static readonly DependencyProperty LayoutModeProperty = DependencyProperty.Register(
        nameof(LayoutMode), typeof(PagingLayoutMode), typeof(PagingControl),
        new PropertyMetadata(PagingLayoutMode.Auto, OnAnyChanged));

    /// <summary>
    /// The requested arrangement. <see cref="PagingLayoutMode.Auto"/> (the default) picks by width at
    /// <see cref="CompactThreshold"/> / <see cref="OverflowThreshold"/> and keeps re-picking as the
    /// control resizes; any other value pins that mode. <see cref="IsCompact"/> or
    /// <see cref="IsOverflow"/> set true override it. What is on screen is <see cref="ShownLayout"/>.
    /// </summary>
    public PagingLayoutMode LayoutMode
    {
        get => (PagingLayoutMode)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    public static readonly DependencyProperty ShownLayoutProperty = DependencyProperty.Register(
        nameof(ShownLayout), typeof(PagingLayoutMode), typeof(PagingControl),
        new PropertyMetadata(PagingLayoutMode.Inline));

    /// <summary>
    /// Read-only. The arrangement currently on screen (Inline, Overflow or Compact, never Auto),
    /// whether requested or picked by width. Watch it with <c>RegisterPropertyChangedCallback</c>.
    /// </summary>
    public PagingLayoutMode ShownLayout
    {
        get => (PagingLayoutMode)GetValue(ShownLayoutProperty);
        private set => SetValue(ShownLayoutProperty, value);
    }

    public static readonly DependencyProperty CompactIndicatorFormatProperty = DependencyProperty.Register(
        nameof(CompactIndicatorFormat), typeof(string), typeof(PagingControl),
        new PropertyMetadata("Page {0} of {1}", OnAnyChanged));

    public string CompactIndicatorFormat
    {
        get => (string)GetValue(CompactIndicatorFormatProperty);
        set => SetValue(CompactIndicatorFormatProperty, value);
    }

    #endregion

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PagingControl ctl)
        {
            ctl.Refresh();
        }
    }

    private static void OnCurrentPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PagingControl ctl) return;
        ctl.Refresh();
        ctl.CurrentPageChanged?.Invoke(ctl, EventArgs.Empty);
    }

    private static void OnDefaultPageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PagingControl ctl || ctl._pageSizeTouched) return;
        // A bound PageSize belongs to the host, even before the binding first changes it.
        if (ctl.ReadLocalValue(PageSizeProperty) is Microsoft.UI.Xaml.Data.BindingExpression) return;
        var size = (uint)e.NewValue;
        if (size == 0 || size == ctl.PageSize) return;

        ctl._applyingDefault = true;
        try { ctl.PageSize = size; }
        finally { ctl._applyingDefault = false; }
    }

    private static void OnPageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PagingControl ctl) return;
        if (!ctl._applyingDefault) ctl._pageSizeTouched = true;

        var oldSize = (uint)e.OldValue;
        var newSize = (uint)e.NewValue;
        if (newSize == 0)
        {
            // Snap back to default; avoids divide-by-zero. The recursive set will re-enter
            // this handler with the corrected newSize and run the rest of the path.
            ctl.PageSize = ctl.DefaultPageSize == 0 ? 10u : ctl.DefaultPageSize;
            return;
        }

        // Keep user near their current starting row when page size changes.
        // Setting CurrentPage routes through OnCurrentPageChanged → Refresh, so we only Refresh
        // here when the page didn't change.
        var pageMoved = false;
        if (oldSize > 0 && ctl.CurrentPage > 0)
        {
            var startRow = (ctl.CurrentPage - 1) * oldSize; // 0-based
            var newPage = (startRow / newSize) + 1;
            if (newPage != ctl.CurrentPage)
            {
                ctl.SyncComboToPageSize();
                ctl.CurrentPage = newPage;
                pageMoved = true;
            }
        }

        if (!pageMoved)
        {
            ctl.SyncComboToPageSize();
            ctl.Refresh();
        }
        ctl.PageSizeChanged?.Invoke(ctl, EventArgs.Empty);
    }

    private static void OnPageSizeOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PagingControl ctl)
        {
            // Rebuild all three combos so a dynamic PageSizeOptions change is picked up
            // immediately regardless of which mode (Inline / Overflow / Compact) is active.
            ctl.RebuildPageSizeCombo();
            ctl.RebuildOverflowPageSizeCombo();
            ctl.RebuildCompactPageSizeCombo();
            ctl.Refresh();
        }
    }

    private static void OnShowAllOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PagingControl ctl) return;
        // Dropping the option mid-flight can't leave us stuck in "All" mode.
        if (!(bool)e.NewValue) ctl._allSelected = false;
        ctl.RebuildPageSizeCombo();
        ctl.RebuildOverflowPageSizeCombo();
        ctl.RebuildCompactPageSizeCombo();
        ctl.Refresh();
    }

    private void SyncAllCombos()
    {
        SyncComboToPageSizeFor(PART_PageSizeCombo);
        SyncComboToPageSizeFor(PART_OverflowPageSizeCombo);
        SyncComboToPageSizeFor(PART_CompactPageSizeCombo);
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var layout = ResolveLayout(e.NewSize.Width);
        if ((layout == PagingLayoutMode.Compact) != _effectiveCompact
            || (layout == PagingLayoutMode.Overflow) != _effectiveOverflow)
        {
            Refresh();
        }
    }

    // The requested arrangement, with Auto resolved against the given width.
    private PagingLayoutMode ResolveLayout(double width)
    {
        var requested = IsCompact ? PagingLayoutMode.Compact
                      : IsOverflow ? PagingLayoutMode.Overflow
                      : LayoutMode;
        if (requested != PagingLayoutMode.Auto) return requested;

        if (width > 0 && width < CompactThreshold) return PagingLayoutMode.Compact;
        if (width > 0 && width < OverflowThreshold) return PagingLayoutMode.Overflow;
        return PagingLayoutMode.Inline;
    }

    private uint EffectivePageSize => PageSize == 0 ? (DefaultPageSize == 0 ? 10u : DefaultPageSize) : PageSize;

    private uint PageCount
    {
        get
        {
            var size = EffectivePageSize;
            if (TotalItemCount == 0 || size == 0) return 0;
            return (TotalItemCount + size - 1) / size;
        }
    }

    // Smallest numeric page-size the user could pick. Used for the auto-hide rule:
    // if the whole set already fits in the smallest size there is nothing to page
    // and no smaller size to choose, so the pager hides.
    private uint SmallestPageSizeOption()
    {
        var options = PageSizeOptions ?? (IList<uint>)DefaultPageSizeOptions;
        uint min = 0;
        foreach (var o in options)
        {
            if (o == 0) continue;
            if (min == 0 || o < min) min = o;
        }
        return min == 0 ? 10u : min;
    }

    private void EnsurePageSizeWithinOptions()
    {
        var options = PageSizeOptions ?? (IList<uint>)DefaultPageSizeOptions;
        if (options.Count == 0) return;
        if (!options.Contains(EffectivePageSize))
        {
            // Don't override an explicitly set value that's just not in options;
            // instead, leave PageSize alone but seed it from DefaultPageSize if zero.
            if (PageSize == 0)
            {
                PageSize = options.Contains(DefaultPageSize) ? DefaultPageSize : options[0];
            }
        }
    }

    // Thin wrappers — all three combos go through the same fill/sync helpers.
    private void RebuildPageSizeCombo() => RebuildPageSizeComboFor(PART_PageSizeCombo);

    private void SyncComboToPageSize() => SyncComboToPageSizeFor(PART_PageSizeCombo);

    private void RebuildPageSizeComboFor(ComboBox? combo)
    {
        if (combo == null) return;
        var options = PageSizeOptions ?? (IList<uint>)DefaultPageSizeOptions;
        _suppressComboEvents = true;
        try
        {
            combo.Items.Clear();
            foreach (var opt in options)
            {
                combo.Items.Add(new PageSizeItem(opt, isAll: false));
            }
            if (ShowAllOption)
            {
                combo.Items.Add(new PageSizeItem(0u, isAll: true));
            }
            SelectComboItem(combo);
        }
        finally
        {
            _suppressComboEvents = false;
        }
    }

    private void SyncComboToPageSizeFor(ComboBox? combo)
    {
        if (combo == null) return;
        _suppressComboEvents = true;
        try
        {
            SelectComboItem(combo);
        }
        finally
        {
            _suppressComboEvents = false;
        }
    }

    private void SelectComboItem(ComboBox combo)
    {
        var size = EffectivePageSize;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is not PageSizeItem p) continue;
            var match = _allSelected ? p.IsAll : (!p.IsAll && p.Value == size);
            if (match)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = -1;
    }

    private void Refresh()
    {
        if (PART_NumberedPages == null) return;

        // In "All" mode keep the single page sized to the whole set as the total changes.
        if (_allSelected)
        {
            var allSize = TotalItemCount == 0 ? 1u : TotalItemCount;
            if (PageSize != allSize)
            {
                PageSize = allSize; // re-drives Refresh via OnPageSizeChanged
                return;
            }
        }

        var pageCount = PageCount;

        // Auto-hide only when the whole set already fits in the smallest selectable page
        // size — nothing to page through and no smaller size to pick. (Previously hid on
        // pageCount <= 1, which wrongly hid the size selector when a large page size left
        // a single page, locking users out of choosing a smaller size.)
        if (TotalItemCount <= SmallestPageSizeOption())
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;

        // Clamp current page.
        var current = CurrentPage == 0 ? 1u : CurrentPage;
        if (current > pageCount) current = pageCount;
        if (current != CurrentPage)
        {
            CurrentPage = current;
            return; // OnAnyChanged will re-enter Refresh.
        }

        // Decide layout mode: IsCompact / IsOverflow / LayoutMode request one, Auto picks by width.
        var layout = ResolveLayout(ActualWidth);
        _effectiveCompact = layout == PagingLayoutMode.Compact;
        _effectiveOverflow = layout == PagingLayoutMode.Overflow;
        ShownLayout = layout;

        PART_InlineRoot.Visibility = _effectiveCompact ? Visibility.Collapsed : Visibility.Visible;
        PART_CompactRoot.Visibility = _effectiveCompact ? Visibility.Visible : Visibility.Collapsed;

        if (_effectiveCompact)
        {
            RefreshCompact(current, pageCount);
        }
        else
        {
            RefreshInline(current, pageCount);
        }
    }

    private void RefreshInline(uint current, uint pageCount)
    {
        // In overflow mode, the page-size selector and jump-to-page collapse inline
        // and re-emerge in the overflow flyout (still gated by the same Show* flags).
        var pageSizeInline = ShowPageSizeSelector && !_effectiveOverflow;
        var jumpInline = ShowJumpToPage && !_effectiveOverflow;
        var anyOverflowItem = _effectiveOverflow && (ShowPageSizeSelector || ShowJumpToPage);

        // Section visibility.
        PART_PageSizePanel.Visibility = pageSizeInline ? Visibility.Visible : Visibility.Collapsed;
        PART_First.Visibility = ShowFirstButton ? Visibility.Visible : Visibility.Collapsed;
        PART_Last.Visibility = ShowLastButton ? Visibility.Visible : Visibility.Collapsed;
        PART_NumberedPages.Visibility = ShowNumberedPages ? Visibility.Visible : Visibility.Collapsed;
        PART_JumpPanel.Visibility = jumpInline ? Visibility.Visible : Visibility.Collapsed;
        PART_Summary.Visibility = ShowSummary ? Visibility.Visible : Visibility.Collapsed;
        PART_OverflowButton.Visibility = anyOverflowItem ? Visibility.Visible : Visibility.Collapsed;

        if (anyOverflowItem)
        {
            PART_OverflowPageSizePanel.Visibility = ShowPageSizeSelector ? Visibility.Visible : Visibility.Collapsed;
            PART_OverflowJumpPanel.Visibility = ShowJumpToPage ? Visibility.Visible : Visibility.Collapsed;
            RebuildOverflowPageSizeCombo();
        }

        // Prev/Next enabled state.
        PART_First.IsEnabled = current > 1;
        PART_Prev.IsEnabled = current > 1;
        PART_Next.IsEnabled = current < pageCount;
        PART_Last.IsEnabled = current < pageCount;

        if (ShowNumberedPages)
        {
            BuildNumberedPages(current, pageCount, MaxNumberedPages <= 0 ? 7 : MaxNumberedPages);
        }
        else
        {
            PART_NumberedPages.Children.Clear();
        }

        if (ShowSummary)
        {
            var size = EffectivePageSize;
            var startRow = ((current - 1) * size) + 1;
            var endRow = Math.Min(current * size, TotalItemCount);
            PART_Summary.Text = string.Format(CultureInfo.CurrentCulture,
                SummaryFormat,
                startRow, endRow, TotalItemCount);
        }
    }

    private void RefreshCompact(uint current, uint pageCount)
    {
        PART_CompactPrev.IsEnabled = current > 1;
        PART_CompactNext.IsEnabled = current < pageCount;

        PART_CompactIndicatorText.Text = string.Format(CultureInfo.CurrentCulture,
            CompactIndicatorFormat,
            current, pageCount);

        // Flyout content visibility — toggled by the same Show* flags. First/Last omitted in compact.
        PART_CompactJumpPanel.Visibility = ShowJumpToPage ? Visibility.Visible : Visibility.Collapsed;
        PART_CompactPageSizePanel.Visibility = ShowPageSizeSelector ? Visibility.Visible : Visibility.Collapsed;
        PART_CompactSummary.Visibility = ShowSummary ? Visibility.Visible : Visibility.Collapsed;

        BuildCompactPages(current, pageCount, MaxNumberedPages <= 0 ? 7 : MaxNumberedPages);
        RebuildCompactPageSizeCombo();

        if (ShowSummary)
        {
            var size = EffectivePageSize;
            var startRow = ((current - 1) * size) + 1;
            var endRow = Math.Min(current * size, TotalItemCount);
            PART_CompactSummary.Text = string.Format(CultureInfo.CurrentCulture,
                SummaryFormat,
                startRow, endRow, TotalItemCount);
        }
    }

    private void BuildCompactPages(uint current, uint pageCount, int maxNumbered)
    {
        PART_CompactPages.Children.Clear();
        var model = BuildPageButtonModel(current, pageCount, maxNumbered);
        foreach (var entry in model)
        {
            if (entry == 0)
            {
                PART_CompactPages.Children.Add(new TextBlock
                {
                    Text = "…",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 2),
                    FontSize = 12,
                });
            }
            else
            {
                var isCurrent = entry == current;
                var btn = new Button
                {
                    Content = entry.ToString(CultureInfo.CurrentCulture),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(6, 2, 6, 2),
                    Tag = entry,
                };
                if (isCurrent)
                {
                    if (Application.Current.Resources["AccentButtonStyle"] is Style accent)
                    {
                        btn.Style = accent;
                    }
                    btn.IsEnabled = false;
                }
                btn.Click += OnCompactNumberedPageClick;
                PART_CompactPages.Children.Add(btn);
            }
        }
    }

    private void RebuildCompactPageSizeCombo() => RebuildPageSizeComboFor(PART_CompactPageSizeCombo);

    private void OnCompactNumberedPageClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is uint page)
        {
            CurrentPage = page;
            PART_CompactFlyout?.Hide();
        }
    }

    private void OnCompactPageSizeSelectionChanged(object sender, SelectionChangedEventArgs e)
        => HandlePageSizeSelectionChanged(PART_CompactPageSizeCombo);

    private void OnCompactJumpKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            TryCompactJump();
            e.Handled = true;
        }
    }

    private void TryCompactJump() => TryJumpFrom(PART_CompactJumpInput, PART_CompactFlyout);

    private void RebuildOverflowPageSizeCombo() => RebuildPageSizeComboFor(PART_OverflowPageSizeCombo);

    private void OnOverflowPageSizeSelectionChanged(object sender, SelectionChangedEventArgs e)
        => HandlePageSizeSelectionChanged(PART_OverflowPageSizeCombo);

    private void OnOverflowJumpKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            TryOverflowJump();
            e.Handled = true;
        }
    }

    private void TryOverflowJump() => TryJumpFrom(PART_OverflowJumpInput, PART_OverflowFlyout);

    private void TryJumpFrom(TextBox? input, Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase? flyout)
    {
        if (input == null) return;
        if (uint.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var page))
        {
            var pc = PageCount;
            if (page >= 1 && page <= pc)
            {
                CurrentPage = page;
                input.Text = string.Empty;
                flyout?.Hide();
            }
        }
    }

    /// <summary>
    /// Pure helper for the page-button window. Returns a list where each entry is either
    /// a 1-based page number, or 0 to mean ellipsis.
    /// </summary>
    internal static IList<uint> BuildPageButtonModel(uint currentPage, uint pageCount, int maxNumbered)
    {
        var result = new List<uint>();
        if (pageCount == 0) return result;
        if (maxNumbered < 5) maxNumbered = 5; // need room for: first, ellipsis, current, ellipsis, last

        if (pageCount <= (uint)maxNumbered)
        {
            for (uint i = 1; i <= pageCount; i++) result.Add(i);
            return result;
        }

        // Always include first and last; window around current.
        var sideCount = (maxNumbered - 3) / 2; // pages on each side of current (excluding first/last/current itself)
        var windowStart = currentPage > (uint)(sideCount + 1) ? currentPage - (uint)sideCount : 2u;
        var windowEnd = currentPage + (uint)sideCount < pageCount ? currentPage + (uint)sideCount : pageCount - 1;

        // Shift window if we are near either edge so total visible stays ~maxNumbered.
        var visibleMiddle = (int)(windowEnd - windowStart + 1);
        var slots = maxNumbered - 2; // first + last consume two
        if (visibleMiddle < slots)
        {
            if (windowStart == 2)
            {
                windowEnd = Math.Min(pageCount - 1, windowStart + (uint)slots - 1);
            }
            else if (windowEnd == pageCount - 1)
            {
                windowStart = Math.Max(2u, windowEnd - (uint)slots + 1);
            }
        }

        result.Add(1);
        if (windowStart > 2) result.Add(0); // ellipsis
        for (var i = windowStart; i <= windowEnd; i++) result.Add(i);
        if (windowEnd < pageCount - 1) result.Add(0); // ellipsis
        result.Add(pageCount);
        return result;
    }

    private void BuildNumberedPages(uint current, uint pageCount, int maxNumbered)
    {
        PART_NumberedPages.Children.Clear();
        var model = BuildPageButtonModel(current, pageCount, maxNumbered);
        foreach (var entry in model)
        {
            if (entry == 0)
            {
                PART_NumberedPages.Children.Add(new TextBlock
                {
                    Text = "…",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0),
                    FontSize = 12,
                });
            }
            else
            {
                var isCurrent = entry == current;
                var btn = new Button
                {
                    Content = entry.ToString(CultureInfo.CurrentCulture),
                    MinWidth = 32,
                    Padding = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = entry,
                };
                if (isCurrent)
                {
                    if (Application.Current.Resources["AccentButtonStyle"] is Style accent)
                    {
                        btn.Style = accent;
                    }
                    btn.IsEnabled = false;
                }
                else
                {
                    btn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    btn.BorderThickness = new Thickness(0);
                }
                btn.Click += OnNumberedPageClick;
                PART_NumberedPages.Children.Add(btn);
            }
        }
    }

    private void OnNumberedPageClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is uint page)
        {
            CurrentPage = page;
        }
    }

    private void OnFirstClick(object sender, RoutedEventArgs e) => CurrentPage = 1;

    private void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (CurrentPage > 1) CurrentPage -= 1;
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (CurrentPage < PageCount) CurrentPage += 1;
    }

    private void OnLastClick(object sender, RoutedEventArgs e)
    {
        var pc = PageCount;
        if (pc > 0) CurrentPage = pc;
    }

    private void OnPageSizeSelectionChanged(object sender, SelectionChangedEventArgs e)
        => HandlePageSizeSelectionChanged(PART_PageSizeCombo);

    private void HandlePageSizeSelectionChanged(ComboBox? combo)
    {
        if (_suppressComboEvents) return;
        if (combo?.SelectedItem is not PageSizeItem p) return;

        _pageSizeTouched = true; // the user's pick wins over DefaultPageSize from here on

        if (p.IsAll)
        {
            _allSelected = true;
            ApplyAllPageSize();
        }
        else
        {
            var wasAll = _allSelected;
            _allSelected = false;
            if (p.Value != PageSize)
            {
                PageSize = p.Value; // OnPageSizeChanged syncs combos + refreshes
            }
            else if (wasAll)
            {
                // Same numeric value as before "All"; the DP won't fire, so sync manually.
                SyncAllCombos();
                Refresh();
            }
        }
    }

    private void ApplyAllPageSize()
    {
        var allSize = TotalItemCount == 0 ? 1u : TotalItemCount;
        if (PageSize != allSize)
        {
            PageSize = allSize; // OnPageSizeChanged syncs combos + refreshes
        }
        else
        {
            SyncAllCombos();
            Refresh();
        }
    }

    private void OnJumpKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            TryJump();
            e.Handled = true;
        }
    }

    private void TryJump() => TryJumpFrom(PART_JumpInput, flyout: null);

    // Page-size combo entry. ToString() drives the default ComboBox display, so a numeric
    // option shows its count and the sentinel shows "All".
    private sealed class PageSizeItem
    {
        public PageSizeItem(uint value, bool isAll)
        {
            Value = value;
            IsAll = isAll;
        }

        public uint Value { get; }

        public bool IsAll { get; }

        public override string ToString()
            => IsAll ? "All" : Value.ToString(CultureInfo.CurrentCulture);
    }
}
