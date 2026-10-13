using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Xtreme.Components.Core;

namespace Xtreme.Components.Cards;

/// <summary>
/// Arranges cards into panels across a column grid, by dragging (XU-465).
/// <para>
/// A panel holds one or more cards side by side in order. Every column keeps an empty panel at its
/// foot: dropping a card there creates a panel, and the next rebuild draws a fresh empty one below
/// it. Cards dragged to the tray are set aside without being destroyed. A panel's top strip is its
/// handle: drag it to move the whole panel.
/// </para>
/// <para>
/// Drag runs on RAW POINTER handlers rather than manipulation events or XAML drag-and-drop. That is
/// the Dock Manager's hard-won approach (XU-395): manipulation never engages reliably once a
/// container captures the pointer, and this way the landing wash can be driven continuously from
/// hit-tested zones — the same feedback model, so the two controls feel identical.
/// </para>
/// <para>
/// App-agnostic by construction: cards are opaque ids with a title and an icon factory, so this
/// control never learns what a card actually is. What each edit does to the layout lives in
/// <see cref="CardDockerLayoutRules"/>; this class only works out which edit the user asked for.
/// </para>
/// </summary>
public partial class CardDockerControl : ContentControl
{
    /// <summary>Pointer travel before a press becomes a drag — matches the dock strip's threshold.</summary>
    private const double DragThreshold = 6;

    /// <summary>Floor for a panel's height — below this the cards inside stop being readable.</summary>
    public const int MinPanelHeight = CardDockerLayoutRules.MinPanelHeight;

    /// <summary>Ceiling, so a stray drag can't make one panel taller than any screen.</summary>
    public const int MaxPanelHeight = CardDockerLayoutRules.MaxPanelHeight;

    private const double PanelSpacing = 6;
    private const double ColumnSpacing = 8;

    private readonly Grid _root = new();
    private readonly Grid _columnsGrid = new();
    private readonly TextBlock _trayHeader = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly StackPanel _trayItems = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Border _trayBox;
    private readonly Grid _overlayLayer = new();

    // Rebuilt on every redraw. Looked up by id rather than found by walking the tree, so adding
    // chrome to a panel can't silently break drop zones or keyboard focus.
    private readonly Dictionary<string, Border> _panelBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Grid> _cardRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _chipFocus = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _panelFocus = new(StringComparer.Ordinal);
    private readonly List<Border> _emptySlots = new();

    private Border? _dragPreview;
    private Border? _insertionBar;

    // ---- Drag state (a card OR a panel, never both) ----
    private string? _pressedCardId;
    private string? _pressedPanelId;
    private Point _pressStart;
    private bool _isDragging;
    private UIElement? _capturedElement;
    private Pointer? _capturedPointer;

    private readonly List<DropZone> _zones = new();
    private DropZone? _hoverZone;

    // ---- Panel resize state ----
    private string? _resizePanelId;
    private double _resizeStartY;
    private int _resizeStartHeight;
    private Border? _resizeBox;

    public CardDockerControl()
    {
        _columnsGrid.ColumnSpacing = ColumnSpacing;
        _trayHeader.Text = TrayHeader;

        _trayBox = new Border
        {
            Padding = new Thickness(8),
            MinHeight = 52,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children = { _trayHeader, _trayItems },
            },
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12 };
        stack.Children.Add(_columnsGrid);
        stack.Children.Add(_trayBox);

        _overlayLayer.IsHitTestVisible = false;

        _root.Children.Add(stack);
        _root.Children.Add(_overlayLayer);

        Content = _root;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        // Rebuild, not Refresh: a theme change repaints, it does not alter the arrangement.
        ActualThemeChanged += (_, _) => Rebuild();
    }

    // ---- Public surface -------------------------------------------------------------------------

    public static readonly DependencyProperty ColumnCountProperty = DependencyProperty.Register(
        nameof(ColumnCount), typeof(int), typeof(CardDockerControl),
        new PropertyMetadata(3, (d, _) => ((CardDockerControl)d).Refresh()));

    /// <summary>
    /// How many columns this arrangement uses. Lowering it parks panels from the removed columns in
    /// the last column; raising it again sends them back (see <see cref="CardDockerPanel.PreferredColumn"/>).
    /// </summary>
    public int ColumnCount
    {
        get => (int)GetValue(ColumnCountProperty);
        set => SetValue(ColumnCountProperty, value);
    }

    /// <summary>Every card that can be arranged. Ids not present in the layout land in the tray.</summary>
    public IReadOnlyList<CardDockerCard> Cards
    {
        get => _cards;
        set { _cards = value ?? []; Refresh(); }
    }

    private IReadOnlyList<CardDockerCard> _cards = [];

    /// <summary>
    /// The current arrangement, as a value: the setter stores a normalized copy of what it is given
    /// and the getter returns a fresh copy of the control's state. Mutating either object has no
    /// effect on the control; to change the arrangement in code, get it, edit it, and set it back.
    /// </summary>
    public CardDockerLayout Layout
    {
        get => _layout.Clone();
        set { _layout = value?.Clone() ?? new CardDockerLayout(); Refresh(); }
    }

    private CardDockerLayout _layout = new();

    /// <summary>
    /// Raised after a user edit (drop, panel move, keyboard move, panel resize) that actually changed
    /// the arrangement. Never raised for an assignment to <see cref="Cards"/>, <see cref="Layout"/>
    /// or <see cref="ColumnCount"/>, even when normalization rewrote the layout: the host made that
    /// change itself and can read <see cref="Layout"/> right after assigning.
    /// <para>
    /// Deliberately so. A host that edits one arrangement per width assigns ColumnCount, then Cards,
    /// then Layout; firing on each step would hand it the PREVIOUS width's layout normalized against
    /// the next width's column count and cards, which it would then save as a user edit.
    /// </para>
    /// </summary>
    public event EventHandler? LayoutChanged;

    public static readonly DependencyProperty ShowPanelActionProperty = DependencyProperty.Register(
        nameof(ShowPanelAction), typeof(bool), typeof(CardDockerControl),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Shows a small host-defined button on every panel, which raises
    /// <see cref="PanelActionInvoked"/> with that panel's id.
    /// <para>
    /// Deliberately generic. The General Info blade uses it for "hide this panel at this width", but
    /// per-width visibility is a property of THAT host's layout document, not of arranging cards —
    /// teaching this control about hidden panels would mean teaching it that a panel can exist at one
    /// width and not another, which is a concept it otherwise does not need.
    /// </para>
    /// </summary>
    public bool ShowPanelAction
    {
        get => (bool)GetValue(ShowPanelActionProperty);
        set => SetValue(ShowPanelActionProperty, value);
    }

    public static readonly DependencyProperty PanelActionLabelProperty = DependencyProperty.Register(
        nameof(PanelActionLabel), typeof(string), typeof(CardDockerControl),
        new PropertyMetadata("Hide", OnVisualPropertyChanged));

    /// <summary>Text on the per-panel action button.</summary>
    public string PanelActionLabel
    {
        get => (string)GetValue(PanelActionLabelProperty);
        set => SetValue(PanelActionLabelProperty, value);
    }

    /// <summary>Raised when a panel's action button is clicked; the argument is the panel id.</summary>
    public event EventHandler<string>? PanelActionInvoked;

    public static readonly DependencyProperty TrayHeaderProperty = DependencyProperty.Register(
        nameof(TrayHeader), typeof(string), typeof(CardDockerControl),
        new PropertyMetadata("Set aside", OnVisualPropertyChanged));

    /// <summary>Heading of the tray of set-aside cards.</summary>
    public string TrayHeader
    {
        get => (string)GetValue(TrayHeaderProperty);
        set => SetValue(TrayHeaderProperty, value);
    }

    public static readonly DependencyProperty TrayPlaceholderTextProperty = DependencyProperty.Register(
        nameof(TrayPlaceholderText), typeof(string), typeof(CardDockerControl),
        new PropertyMetadata("Drag a card here to set it aside.", OnVisualPropertyChanged));

    /// <summary>Hint shown in the tray while it is empty.</summary>
    public string TrayPlaceholderText
    {
        get => (string)GetValue(TrayPlaceholderTextProperty);
        set => SetValue(TrayPlaceholderTextProperty, value);
    }

    public static readonly DependencyProperty NewPanelTextProperty = DependencyProperty.Register(
        nameof(NewPanelText), typeof(string), typeof(CardDockerControl),
        new PropertyMetadata("New panel", OnVisualPropertyChanged));

    /// <summary>Label of the empty drop slot at the foot of every column.</summary>
    public string NewPanelText
    {
        get => (string)GetValue(NewPanelTextProperty);
        set => SetValue(NewPanelTextProperty, value);
    }

    public static readonly DependencyProperty DropTargetBackgroundProperty = DependencyProperty.Register(
        nameof(DropTargetBackground), typeof(Brush), typeof(CardDockerControl), new PropertyMetadata(null));

    /// <summary>
    /// Fill of the wash over the drop target while dragging. Null (the default) uses the Dock
    /// Manager's translucent blue landing preview, so both controls read the same.
    /// </summary>
    public Brush? DropTargetBackground
    {
        get => (Brush?)GetValue(DropTargetBackgroundProperty);
        set => SetValue(DropTargetBackgroundProperty, value);
    }

    public static readonly DependencyProperty DropTargetBorderBrushProperty = DependencyProperty.Register(
        nameof(DropTargetBorderBrush), typeof(Brush), typeof(CardDockerControl), new PropertyMetadata(null));

    /// <summary>Outline of the drop-target wash. Null (the default) uses the built-in blue.</summary>
    public Brush? DropTargetBorderBrush
    {
        get => (Brush?)GetValue(DropTargetBorderBrushProperty);
        set => SetValue(DropTargetBorderBrushProperty, value);
    }

    public static readonly DependencyProperty InsertionMarkerBrushProperty = DependencyProperty.Register(
        nameof(InsertionMarkerBrush), typeof(Brush), typeof(CardDockerControl), new PropertyMetadata(null));

    /// <summary>
    /// The bar marking the exact seam a card or panel will land in. Null (the default) uses the
    /// built-in blue.
    /// </summary>
    public Brush? InsertionMarkerBrush
    {
        get => (Brush?)GetValue(InsertionMarkerBrushProperty);
        set => SetValue(InsertionMarkerBrushProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CardDockerControl)d).Rebuild();

    // ---- Layout maintenance ---------------------------------------------------------------------

    private void Normalize()
        => CardDockerLayoutRules.Normalize(_layout, _cards.Select(c => c.Id).ToList(), ColumnCount);

    /// <summary>
    /// Normalize-then-redraw. Every input (<see cref="Cards"/>, <see cref="Layout"/>,
    /// <see cref="ColumnCount"/>) goes through this, so the control does not depend on the order a
    /// host happens to assign them in.
    /// <para>
    /// Worth stating because the alternative bit: normalizing only in the <see cref="Layout"/> setter
    /// left the invariants holding purely because the one host assigned ColumnCount → Cards → Layout.
    /// Any other order, or a later <see cref="Cards"/> assignment on its own, silently rendered
    /// un-normalized state — unplaced cards never reaching the tray, panels sitting in a column index
    /// past <see cref="ColumnCount"/> and so vanishing.
    /// </para>
    /// </summary>
    private void Refresh()
    {
        Normalize();
        Rebuild();
    }

    /// <summary>
    /// Applies one user edit, then normalizes and redraws. <see cref="LayoutChanged"/> fires only if
    /// the normalized result differs from before — a drop on a card's own seam, or a panel dragged
    /// back to where it was, is not a change.
    /// </summary>
    private void Commit(Action<CardDockerLayout> edit)
    {
        var before = CardDockerLayoutRules.Signature(_layout);

        edit(_layout);
        Refresh();

        if (CardDockerLayoutRules.Signature(_layout) != before)
            LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Rendering ------------------------------------------------------------------------------

    /// <summary>
    /// Redraws everything from <see cref="_layout"/>. Always draws — an empty layout with no cards
    /// clears the surface to empty columns and an empty tray rather than keeping the last picture.
    /// </summary>
    private void Rebuild()
    {
        var theme = ActualTheme;

        _columnsGrid.Children.Clear();
        _columnsGrid.ColumnDefinitions.Clear();
        _panelBoxes.Clear();
        _cardRows.Clear();
        _chipFocus.Clear();
        _panelFocus.Clear();
        _emptySlots.Clear();

        for (var column = 0; column < Math.Max(1, ColumnCount); column++)
        {
            _columnsGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var host = new StackPanel { Orientation = Orientation.Vertical, Spacing = PanelSpacing };

            foreach (var panel in CardDockerLayoutRules.PanelsIn(_layout, column))
                host.Children.Add(BuildPanel(panel, theme));

            var empty = BuildEmptyPanel(theme);
            _emptySlots.Add(empty);
            host.Children.Add(empty);

            Grid.SetColumn(host, column);
            _columnsGrid.Children.Add(host);
        }

        _trayHeader.Text = TrayHeader;
        _trayBox.BorderBrush = ComponentTheme.SurfaceOutline(theme);
        _trayItems.Children.Clear();

        foreach (var id in _layout.TrayCardIds)
        {
            if (FindCard(id) is { } card) _trayItems.Children.Add(BuildChip(card, theme));
        }

        if (_layout.TrayCardIds.Count == 0)
        {
            _trayItems.Children.Add(new TextBlock
            {
                Text = TrayPlaceholderText,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    private Border BuildPanel(CardDockerPanel panel, ElementTheme theme)
    {
        var cardRow = new Grid { ColumnSpacing = 4 };

        for (var i = 0; i < panel.CardIds.Count; i++)
        {
            cardRow.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (FindCard(panel.CardIds[i]) is not { } card) continue;

            var chip = BuildChip(card, theme);
            Grid.SetColumn(chip, i);
            cardRow.Children.Add(chip);
        }

        // The header strip is the panel's handle: drag it to move the panel, or focus it and use
        // Ctrl+arrows. The action button (if any) shares the strip, right-aligned.
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var handle = new ContentControl
        {
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Tag = panel.Id,
            Content = new Border
            {
                Height = 14,
                // Transparent, not null: a null background is not hit-testable, so only the bar
                // itself would start a drag.
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Child = new Border
                {
                    Width = 28,
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = ComponentTheme.SurfaceOutline(theme),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };

        AutomationProperties.SetName(handle, string.Join(", ",
            panel.CardIds.Select(id => FindCard(id)?.Title).Where(t => !string.IsNullOrEmpty(t))));

        handle.PointerPressed += OnPanelHandlePressed;
        handle.PointerMoved += OnDragPointerMoved;
        handle.PointerReleased += OnDragPointerReleased;
        handle.PointerCaptureLost += OnDragPointerCaptureLost;
        handle.KeyDown += OnPanelHandleKeyDown;
        header.Children.Add(handle);

        if (ShowPanelAction)
        {
            var action = new Button
            {
                Content = new TextBlock { Text = PanelActionLabel, FontSize = 11 },
                Padding = new Thickness(6, 1, 6, 1),
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };

            var panelId = panel.Id;
            action.Click += (_, _) => PanelActionInvoked?.Invoke(this, panelId);

            Grid.SetColumn(action, 1);
            header.Children.Add(action);
        }

        // A grip on the bottom edge, always present: the panel's height is part of the arrangement,
        // so it is adjustable wherever the arrangement is.
        var grip = new Border
        {
            Height = 6,
            Margin = new Thickness(0, 4, 0, -4),
            CornerRadius = new CornerRadius(3),
            Background = ComponentTheme.SurfaceOutline(theme),
            Opacity = 0.5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        body.Children.Add(header);
        body.Children.Add(cardRow);
        body.Children.Add(grip);

        var box = new Border
        {
            Padding = new Thickness(8, 2, 8, 8),
            MinHeight = Math.Clamp(panel.MinHeight, MinPanelHeight, MaxPanelHeight),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = ComponentTheme.SurfaceOutline(theme),
            Background = ComponentTheme.Surface(theme),
            Child = body,
            Tag = panel.Id,
        };

        var gripPanelId = panel.Id;
        grip.PointerPressed += (s, e) => OnGripPressed(s, e, gripPanelId, box);
        grip.PointerMoved += OnGripMoved;
        grip.PointerReleased += OnGripReleased;
        grip.PointerCaptureLost += (_, _) => { _resizePanelId = null; _resizeBox = null; };

        _panelBoxes[panel.Id] = box;
        _cardRows[panel.Id] = cardRow;
        _panelFocus[panel.Id] = handle;

        return box;
    }

    private Border BuildEmptyPanel(ElementTheme theme)
        => new()
        {
            Padding = new Thickness(8),
            MinHeight = 44,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = ComponentTheme.SurfaceOutline(theme),
            Opacity = 0.6,
            Child = new TextBlock
            {
                Text = NewPanelText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    /// <summary>
    /// A card chip: the visual Border (which owns the pointer gesture) inside a focusable
    /// ContentControl (which owns keyboard focus — a Border cannot take focus).
    /// </summary>
    private ContentControl BuildChip(CardDockerCard card, ElementTheme theme)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        if (card.CreateIcon?.Invoke() is { } icon)
        {
            icon.Width = 16;
            icon.Height = 16;
            content.Children.Add(icon);
        }

        content.Children.Add(new TextBlock
        {
            Text = card.Title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var chip = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = ComponentTheme.SurfaceOutline(theme),
            Background = ComponentTheme.Surface(theme),
            Child = content,
            Tag = card.Id,
        };

        chip.PointerPressed += OnChipPointerPressed;
        chip.PointerMoved += OnDragPointerMoved;
        chip.PointerReleased += OnDragPointerReleased;
        chip.PointerCaptureLost += OnDragPointerCaptureLost;

        var focusable = new ContentControl
        {
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = chip,
            Tag = card.Id,
        };

        AutomationProperties.SetName(focusable, card.Title);
        focusable.KeyDown += OnChipKeyDown;

        _chipFocus[card.Id] = focusable;
        return focusable;
    }

    private CardDockerCard? FindCard(string id)
        => _cards.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    // ---- Drag -----------------------------------------------------------------------------------

    private void OnChipPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string cardId } chip) return;
        BeginPress(chip, e, cardId, panelId: null);
    }

    private void OnPanelHandlePressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string panelId } handle) return;
        BeginPress(handle, e, cardId: null, panelId);
    }

    private void BeginPress(UIElement element, PointerRoutedEventArgs e, string? cardId, string? panelId)
    {
        _pressedCardId = cardId;
        _pressedPanelId = panelId;
        _pressStart = e.GetCurrentPoint(this).Position;
        _isDragging = false;

        if (element.CapturePointer(e.Pointer))
        {
            _capturedElement = element;
            _capturedPointer = e.Pointer;
        }
    }

    private void OnDragPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedCardId is null && _pressedPanelId is null) return;

        var point = e.GetCurrentPoint(this).Position;

        if (!_isDragging)
        {
            // A press that never travels is a click, not a drag — the threshold is what keeps a
            // twitchy mouse from starting one.
            if (Math.Abs(point.X - _pressStart.X) < DragThreshold
                && Math.Abs(point.Y - _pressStart.Y) < DragThreshold)
            {
                return;
            }

            _isDragging = true;
            BuildZones();
        }

        UpdateHover(point);
        e.Handled = true;
    }

    private void OnDragPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var dropped = _isDragging ? _hoverZone : null;
        var cardId = _pressedCardId;
        var panelId = _pressedPanelId;

        ReleaseCapture();
        ResetDrag();

        if (dropped is null) return;

        if (cardId is not null) ApplyCardDrop(cardId, dropped);
        else if (panelId is not null && dropped.Kind == DropKind.PanelSlot)
            Commit(l => CardDockerLayoutRules.MovePanel(l, panelId, dropped.Column, dropped.Index));
    }

    private void OnDragPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Capture can be stolen mid-gesture. Abandon rather than guess where it would have landed —
        // a stuck drag id poisons every later gesture (XU-395).
        _capturedElement = null;
        _capturedPointer = null;
        ResetDrag();
    }

    private void ResetDrag()
    {
        ClearDragVisuals();
        _pressedCardId = null;
        _pressedPanelId = null;
        _isDragging = false;
        _hoverZone = null;
        _zones.Clear();
    }

    private void ReleaseCapture()
    {
        if (_capturedElement is not null && _capturedPointer is not null)
            _capturedElement.ReleasePointerCapture(_capturedPointer);

        _capturedElement = null;
        _capturedPointer = null;
    }

    /// <summary>
    /// Snapshots every legal drop target as a rectangle, once, at drag start. Hit-testing a fixed
    /// list is both cheaper and steadier than walking the tree per pointer move — the dock does the
    /// same, and it is why the landing wash doesn't flicker.
    /// </summary>
    private void BuildZones()
    {
        _zones.Clear();

        for (var column = 0; column < _emptySlots.Count; column++)
        {
            var panels = CardDockerLayoutRules.PanelsIn(_layout, column);

            for (var p = 0; p < panels.Count; p++)
            {
                if (!_panelBoxes.TryGetValue(panels[p].Id, out var box)) continue;

                var bounds = BoundsOf(box);
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;

                if (_pressedPanelId is not null)
                {
                    // Moving a panel: its top half lands above it, its bottom half below.
                    var half = bounds.Height / 2;
                    _zones.Add(new DropZone(new Rect(bounds.X, bounds.Y, bounds.Width, half),
                        DropKind.PanelSlot, null, column, p, Leading: true));
                    _zones.Add(new DropZone(new Rect(bounds.X, bounds.Y + half, bounds.Width, half),
                        DropKind.PanelSlot, null, column, p + 1, Leading: false));
                    continue;
                }

                // Moving a card: each card in the panel contributes two half-width zones, left half
                // to land before it and right half after. That is what makes a side-by-side row
                // reorderable without a separate gesture.
                var chips = _cardRows.TryGetValue(panels[p].Id, out var row)
                    ? row.Children.OfType<FrameworkElement>().ToList()
                    : [];

                if (chips.Count == 0)
                {
                    _zones.Add(new DropZone(bounds, DropKind.IntoPanel, panels[p].Id, column, 0, Leading: true));
                    continue;
                }

                for (var i = 0; i < chips.Count; i++)
                {
                    var chipBounds = BoundsOf(chips[i]);
                    var half = chipBounds.Width / 2;

                    _zones.Add(new DropZone(
                        new Rect(chipBounds.X, bounds.Y, half, bounds.Height),
                        DropKind.IntoPanel, panels[p].Id, column, i, Leading: true));

                    _zones.Add(new DropZone(
                        new Rect(chipBounds.X + half, bounds.Y, half, bounds.Height),
                        DropKind.IntoPanel, panels[p].Id, column, i + 1, Leading: false));
                }
            }

            var slotBounds = BoundsOf(_emptySlots[column]);
            if (slotBounds.Width > 0 && slotBounds.Height > 0)
            {
                _zones.Add(_pressedPanelId is not null
                    ? new DropZone(slotBounds, DropKind.PanelSlot, null, column, panels.Count, Leading: true)
                    : new DropZone(slotBounds, DropKind.NewPanel, null, column, 0, Leading: true));
            }
        }

        // Panels can't be set aside — only cards go to the tray.
        if (_pressedCardId is not null)
        {
            var trayBounds = BoundsOf(_trayBox);
            if (trayBounds.Width > 0)
                _zones.Add(new DropZone(trayBounds, DropKind.Tray, null, 0, 0, Leading: true));
        }
    }

    private Rect BoundsOf(FrameworkElement element)
    {
        try
        {
            var origin = element.TransformToVisual(this).TransformPoint(new Point(0, 0));
            return new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
        }
        catch
        {
            // An element not yet in the visual tree can't be transformed; it simply isn't a target.
            return default;
        }
    }

    private void UpdateHover(Point point)
    {
        // Smallest matching zone wins, so a card's half-width slot beats the panel it sits in.
        var zone = _zones
            .Where(z => z.Bounds.Contains(point))
            .OrderBy(z => z.Bounds.Width * z.Bounds.Height)
            .FirstOrDefault();

        _hoverZone = zone;

        if (zone is null)
        {
            ClearDragVisuals();
            return;
        }

        ShowPreview(zone);
    }

    /// <summary>
    /// The Dock Manager's landing wash, reused deliberately: the same shade and border mean a drop
    /// target reads identically whether the user is arranging panes or cards.
    /// </summary>
    private void ShowPreview(DropZone zone)
    {
        _dragPreview ??= CreateAndAdd(new Border
        {
            Background = DropTargetBackground
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0x2D, 0x5B, 0x8A)),
            BorderBrush = DropTargetBorderBrush
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xE0, 0x4A, 0x82, 0xC4)),
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TranslateTransform(),
        });

        Place(_dragPreview!, zone.Bounds);

        // Inside a panel (or between panels) the wash alone can't say WHERE it lands, so an
        // insertion bar marks the exact seam: vertical between cards, horizontal between panels.
        Rect? bar = zone.Kind switch
        {
            DropKind.IntoPanel => new Rect(
                (zone.Leading ? zone.Bounds.X : zone.Bounds.X + zone.Bounds.Width) - 1.5,
                zone.Bounds.Y, 3, zone.Bounds.Height),
            DropKind.PanelSlot => new Rect(
                zone.Bounds.X,
                (zone.Leading ? zone.Bounds.Y : zone.Bounds.Y + zone.Bounds.Height) - 1.5,
                zone.Bounds.Width, 3),
            _ => (Rect?)null,
        };

        if (bar is { } rect)
        {
            _insertionBar ??= CreateAndAdd(new Border
            {
                Background = InsertionMarkerBrush
                    ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0x82, 0xC4)),
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = new TranslateTransform(),
            });

            Place(_insertionBar!, rect);
        }
        else if (_insertionBar is not null)
        {
            _overlayLayer.Children.Remove(_insertionBar);
            _insertionBar = null;
        }
    }

    private T CreateAndAdd<T>(T element) where T : UIElement
    {
        _overlayLayer.Children.Add(element);
        return element;
    }

    private static void Place(Border element, Rect rect)
    {
        var transform = (TranslateTransform)element.RenderTransform;
        transform.X = rect.X;
        transform.Y = rect.Y;
        element.Width = Math.Max(0, rect.Width);
        element.Height = Math.Max(0, rect.Height);
    }

    private void ClearDragVisuals()
    {
        _overlayLayer.Children.Clear();
        _dragPreview = null;
        _insertionBar = null;
    }

    private void ApplyCardDrop(string cardId, DropZone zone)
    {
        switch (zone.Kind)
        {
            case DropKind.Tray:
                Commit(l => CardDockerLayoutRules.MoveCardToTray(l, cardId));
                break;

            case DropKind.NewPanel:
                Commit(l => CardDockerLayoutRules.MoveCardToNewPanel(l, cardId, zone.Column));
                break;

            case DropKind.IntoPanel when zone.PanelId is { } panelId:
                Commit(l => CardDockerLayoutRules.MoveCardToPanel(l, cardId, panelId, zone.Index));
                break;
        }
    }

    // ---- Keyboard -------------------------------------------------------------------------------

    private static bool IsCtrlDown()
        => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down)
           == CoreVirtualKeyStates.Down;

    /// <summary>
    /// Keyboard path for a focused card: Ctrl+Left/Right reorders it within its panel, Ctrl+Up/Down
    /// moves it to the end of the panel above/below (Ctrl+Down from the bottom panel gives it a new
    /// panel at the column foot), Delete sets it aside, and Ctrl+Up on a set-aside card puts it in a
    /// new panel at the foot of the first column.
    /// </summary>
    private void OnChipKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string cardId }) return;

        var ctrl = IsCtrlDown();
        Action<CardDockerLayout>? edit = null;
        var panel = CardDockerLayoutRules.PanelOf(_layout, cardId);

        if (panel is null)
        {
            if (ctrl && e.Key == VirtualKey.Up)
                edit = l => CardDockerLayoutRules.MoveCardToNewPanel(l, cardId, 0);
        }
        else if (e.Key == VirtualKey.Delete && !ctrl)
        {
            edit = l => CardDockerLayoutRules.MoveCardToTray(l, cardId);
        }
        else if (ctrl)
        {
            var index = panel.CardIds.IndexOf(cardId);
            var column = CardDockerLayoutRules.PanelsIn(_layout, panel.Column);
            var position = column.IndexOf(panel);

            edit = e.Key switch
            {
                VirtualKey.Left when index > 0
                    => l => CardDockerLayoutRules.MoveCardToPanel(l, cardId, panel.Id, index - 1),
                VirtualKey.Right when index < panel.CardIds.Count - 1
                    => l => CardDockerLayoutRules.MoveCardToPanel(l, cardId, panel.Id, index + 2),
                VirtualKey.Up when position > 0
                    => l => CardDockerLayoutRules.MoveCardToPanel(l, cardId, column[position - 1].Id, int.MaxValue),
                VirtualKey.Down when position < column.Count - 1
                    => l => CardDockerLayoutRules.MoveCardToPanel(l, cardId, column[position + 1].Id, int.MaxValue),
                VirtualKey.Down
                    => l => CardDockerLayoutRules.MoveCardToNewPanel(l, cardId, panel.Column),
                _ => null,
            };
        }

        // Swallow every Ctrl+arrow, even a no-op at an edge, so a host ScrollViewer doesn't scroll.
        if (edit is null && !(ctrl && IsArrow(e.Key))) return;
        e.Handled = true;

        if (edit is null) return;
        Commit(edit);
        RestoreFocus(_chipFocus, cardId);
    }

    /// <summary>
    /// Keyboard path for a focused panel handle: Ctrl+Up/Down moves the panel within its column,
    /// Ctrl+Left/Right moves it to the neighboring column at the same height.
    /// </summary>
    private void OnPanelHandleKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string panelId } || !IsCtrlDown() || !IsArrow(e.Key)) return;
        e.Handled = true;

        var panel = _layout.Panels.FirstOrDefault(p => p.Id == panelId);
        if (panel is null) return;

        var column = CardDockerLayoutRules.PanelsIn(_layout, panel.Column);
        var position = column.IndexOf(panel);
        var lastColumn = Math.Max(0, ColumnCount - 1);

        (int Column, int Slot)? target = e.Key switch
        {
            VirtualKey.Up when position > 0 => (panel.Column, position - 1),
            VirtualKey.Down when position < column.Count - 1 => (panel.Column, position + 2),
            VirtualKey.Left when panel.Column > 0 => (panel.Column - 1, position),
            VirtualKey.Right when panel.Column < lastColumn => (panel.Column + 1, position),
            _ => null,
        };

        if (target is not { } t) return;

        Commit(l => CardDockerLayoutRules.MovePanel(l, panelId, t.Column, t.Slot));
        RestoreFocus(_panelFocus, panelId);
    }

    private static bool IsArrow(VirtualKey key)
        => key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down;

    /// <summary>
    /// Puts focus back on the moved card or panel. The rebuild replaced every element, so the old
    /// focused one is gone; deferred a tick because the new one is not in the tree yet.
    /// </summary>
    private void RestoreFocus(Dictionary<string, Control> registry, string id)
        => DispatcherQueue?.TryEnqueue(() =>
        {
            if (registry.TryGetValue(id, out var target)) target.Focus(FocusState.Keyboard);
        });

    // ---- Panel resize ---------------------------------------------------------------------------

    private void OnGripPressed(object sender, PointerRoutedEventArgs e, string panelId, Border box)
    {
        if (sender is not Border grip) return;

        var target = _layout.Panels.FirstOrDefault(p => p.Id == panelId);
        if (target is null) return;

        _resizePanelId = panelId;
        _resizeStartY = e.GetCurrentPoint(this).Position.Y;
        _resizeStartHeight = Math.Clamp(target.MinHeight, MinPanelHeight, MaxPanelHeight);

        // Resize the panel box live rather than rebuilding on every pointer move, which would drop
        // the capture mid-drag.
        _resizeBox = box;

        grip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnGripMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_resizePanelId is null || _resizeBox is null) return;

        var delta = e.GetCurrentPoint(this).Position.Y - _resizeStartY;
        _resizeBox.MinHeight = Math.Clamp(_resizeStartHeight + delta, MinPanelHeight, MaxPanelHeight);

        e.Handled = true;
    }

    private void OnGripReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_resizePanelId is null)
        {
            _resizeBox = null;
            return;
        }

        if (sender is Border grip) grip.ReleasePointerCapture(e.Pointer);

        var panelId = _resizePanelId;
        var applied = (int)Math.Round(_resizeBox?.MinHeight ?? _resizeStartHeight);

        _resizePanelId = null;
        _resizeBox = null;

        // Only now — a Commit per pointer move would rebuild the tree under the captured pointer.
        // Commit skips the event when the height came back to where it started.
        Commit(l =>
        {
            if (l.Panels.FirstOrDefault(p => p.Id == panelId) is { } target)
                target.MinHeight = Math.Clamp(applied, MinPanelHeight, MaxPanelHeight);
        });
    }

    private enum DropKind { IntoPanel, NewPanel, Tray, PanelSlot }

    /// <summary>
    /// One drop target. <see cref="Index"/> is the slot (cards within a panel, or panels within a
    /// column); <see cref="Leading"/> says which edge of <see cref="Bounds"/> the seam is on.
    /// </summary>
    private sealed record DropZone(Rect Bounds, DropKind Kind, string? PanelId, int Column, int Index, bool Leading);
}
