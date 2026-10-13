using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Xtreme.Components.Core;

namespace Xtreme.Components.Cards;

/// <summary>
/// Arranges cards into panels across a column grid, by dragging (XU-465).
/// <para>
/// A panel holds one or more cards side by side in order. Every column keeps an empty panel at its
/// foot: dropping a card there creates a panel, and the next rebuild draws a fresh empty one below
/// it. Cards dragged to the tray are set aside without being destroyed.
/// </para>
/// <para>
/// Drag runs on RAW POINTER handlers rather than manipulation events or XAML drag-and-drop. That is
/// the Dock Manager's hard-won approach (XU-395): manipulation never engages reliably once a
/// container captures the pointer, and this way the landing wash can be driven continuously from
/// hit-tested zones — the same feedback model, so the two controls feel identical.
/// </para>
/// <para>
/// App-agnostic by construction: cards are opaque ids with a title and an icon factory, so this
/// control never learns what a card actually is.
/// </para>
/// </summary>
public partial class CardDockerControl : ContentControl
{
    /// <summary>Pointer travel before a press becomes a drag — matches the dock strip's threshold.</summary>
    private const double DragThreshold = 6;

    /// <summary>Floor for a panel's height — below this the cards inside stop being readable.</summary>
    public const int MinPanelHeight = 60;

    /// <summary>Ceiling, so a stray drag can't make one panel taller than any screen.</summary>
    public const int MaxPanelHeight = 1200;

    private const double PanelSpacing = 6;
    private const double ColumnSpacing = 8;

    private readonly Grid _root = new();
    private readonly Grid _columnsGrid = new();
    private readonly StackPanel _trayItems = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Border _trayBox;
    private readonly Grid _overlayLayer = new();

    private Border? _dragPreview;
    private Border? _insertionBar;

    // ---- Drag state ----
    private string? _pressedCardId;
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
                Children =
                {
                    new TextBlock { Text = "Set aside", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    _trayItems,
                },
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

    /// <summary>How many columns this arrangement uses.</summary>
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

    /// <summary>The current arrangement. Setting it replaces what is on screen.</summary>
    public CardDockerLayout Layout
    {
        get => _layout;
        set { _layout = value?.Clone() ?? new CardDockerLayout(); Refresh(); }
    }

    private CardDockerLayout _layout = new();

    /// <summary>Raised after any user change. Read <see cref="Layout"/> for the result.</summary>
    public event EventHandler? LayoutChanged;

    public static readonly DependencyProperty ShowPanelActionProperty = DependencyProperty.Register(
        nameof(ShowPanelAction), typeof(bool), typeof(CardDockerControl),
        new PropertyMetadata(false, (d, _) => ((CardDockerControl)d).Rebuild()));

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
        new PropertyMetadata("Hide", (d, _) => ((CardDockerControl)d).Rebuild()));

    /// <summary>Text on the per-panel action button.</summary>
    public string PanelActionLabel
    {
        get => (string)GetValue(PanelActionLabelProperty);
        set => SetValue(PanelActionLabelProperty, value);
    }

    /// <summary>Raised when a panel's action button is clicked; the argument is the panel id.</summary>
    public event EventHandler<string>? PanelActionInvoked;

    // ---- Layout maintenance ---------------------------------------------------------------------

    /// <summary>
    /// Brings the layout into a state the renderer can trust: cards it doesn't mention go to the
    /// tray, unknown ids are dropped, empty panels disappear, columns are clamped and Sort is dense.
    /// </summary>
    private void Normalize()
    {
        var known = _cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var panel in _layout.Panels)
            panel.CardIds.RemoveAll(id => !known.Contains(id));

        _layout.TrayCardIds.RemoveAll(id => !known.Contains(id));
        _layout.Panels.RemoveAll(p => p.CardIds.Count == 0);

        var placed = _layout.Panels.SelectMany(p => p.CardIds)
            .Concat(_layout.TrayCardIds)
            .ToHashSet(StringComparer.Ordinal);

        // A card nobody placed is set aside rather than invented into a panel — the host decides
        // where new cards belong, and silently materialising them here would fight that.
        foreach (var card in _cards.Where(c => !placed.Contains(c.Id)))
            _layout.TrayCardIds.Add(card.Id);

        foreach (var panel in _layout.Panels)
        {
            panel.Column = Math.Clamp(panel.Column, 0, Math.Max(0, ColumnCount - 1));
            panel.MinHeight = Math.Clamp(panel.MinHeight, MinPanelHeight, MaxPanelHeight);
        }

        for (var column = 0; column < ColumnCount; column++)
        {
            var inColumn = _layout.Panels.Where(p => p.Column == column).OrderBy(p => p.Sort).ToList();
            for (var i = 0; i < inColumn.Count; i++) inColumn[i].Sort = i;
        }
    }

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

    private void Commit()
    {
        Refresh();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Rendering ------------------------------------------------------------------------------

    private void Rebuild()
    {
        if (_cards.Count == 0 && _layout.Panels.Count == 0) return;

        var theme = ActualTheme;

        _columnsGrid.Children.Clear();
        _columnsGrid.ColumnDefinitions.Clear();

        for (var column = 0; column < Math.Max(1, ColumnCount); column++)
        {
            _columnsGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var host = new StackPanel { Orientation = Orientation.Vertical, Spacing = PanelSpacing };

            foreach (var panel in _layout.Panels.Where(p => p.Column == column).OrderBy(p => p.Sort))
                host.Children.Add(BuildPanel(panel, theme));

            host.Children.Add(BuildEmptyPanel(column, theme));

            Grid.SetColumn(host, column);
            _columnsGrid.Children.Add(host);
        }

        _trayBox.BorderBrush = ComponentTheme.SurfaceOutline(theme);
        _trayItems.Children.Clear();

        foreach (var id in _layout.TrayCardIds)
        {
            if (FindCard(id) is { } card) _trayItems.Children.Add(BuildChip(card, theme, inTray: true));
        }

        if (_layout.TrayCardIds.Count == 0)
        {
            _trayItems.Children.Add(new TextBlock
            {
                Text = "Drag a card here to set it aside.",
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

            var chip = BuildChip(card, theme, inTray: false);
            Grid.SetColumn(chip, i);
            cardRow.Children.Add(chip);
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

        var gripPanelId = panel.Id;
        grip.PointerPressed += (s, e) => OnGripPressed(s, e, gripPanelId);
        grip.PointerMoved += OnGripMoved;
        grip.PointerReleased += OnGripReleased;
        grip.PointerCaptureLost += (_, _) => _resizePanelId = null;

        UIElement content = cardRow;

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

            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
            stack.Children.Add(action);
            stack.Children.Add(cardRow);
            content = stack;
        }

        // The grip goes in a wrapper so CardRowOf still finds the card row as the first Grid child.
        var body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        body.Children.Add(content);
        body.Children.Add(grip);

        var box = new Border
        {
            Padding = new Thickness(8),
            MinHeight = Math.Clamp(panel.MinHeight, MinPanelHeight, MaxPanelHeight),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = ComponentTheme.SurfaceOutline(theme),
            Background = ComponentTheme.Surface(theme),
            Child = body,
            Tag = panel.Id,
        };

        return box;
    }

    private Border BuildEmptyPanel(int column, ElementTheme theme)
        => new()
        {
            Padding = new Thickness(8),
            MinHeight = 44,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = ComponentTheme.SurfaceOutline(theme),
            Opacity = 0.6,
            Tag = EmptyPanelTag(column),
            Child = new TextBlock
            {
                Text = "New panel",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    private Border BuildChip(CardDockerCard card, ElementTheme theme, bool inTray)
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
        chip.PointerMoved += OnChipPointerMoved;
        chip.PointerReleased += OnChipPointerReleased;
        chip.PointerCaptureLost += OnChipPointerCaptureLost;

        return chip;
    }

    private CardDockerCard? FindCard(string id)
        => _cards.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    private static string EmptyPanelTag(int column) => $" empty:{column}";

    // ---- Drag -----------------------------------------------------------------------------------

    private void OnChipPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border chip || chip.Tag is not string cardId) return;

        _pressedCardId = cardId;
        _pressStart = e.GetCurrentPoint(this).Position;
        _isDragging = false;

        if (chip.CapturePointer(e.Pointer))
        {
            _capturedElement = chip;
            _capturedPointer = e.Pointer;
        }
    }

    private void OnChipPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedCardId is null) return;

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

    private void OnChipPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var dropped = _isDragging ? _hoverZone : null;
        var cardId = _pressedCardId;

        ReleaseCapture();
        ClearDragVisuals();

        _pressedCardId = null;
        _isDragging = false;
        _hoverZone = null;
        _zones.Clear();

        if (cardId is null || dropped is null) return;

        ApplyDrop(cardId, dropped);
    }

    private void OnChipPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Capture can be stolen mid-gesture. Abandon rather than guess where it would have landed —
        // a stuck drag id poisons every later gesture (XU-395).
        ClearDragVisuals();
        _pressedCardId = null;
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

        foreach (var host in _columnsGrid.Children.OfType<StackPanel>())
        {
            foreach (var box in host.Children.OfType<Border>())
            {
                if (box.Tag is not string tag) continue;

                var bounds = BoundsOf(box);
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;

                if (tag.StartsWith(' '))
                {
                    var column = int.Parse(tag[(tag.IndexOf(':') + 1)..]);
                    _zones.Add(new DropZone(bounds, DropKind.NewPanel, PanelId: null, column, Index: 0));
                    continue;
                }

                var panel = _layout.Panels.FirstOrDefault(p => p.Id == tag);
                if (panel is null) continue;

                // Each card in the panel contributes two half-width zones: drop on the left half to
                // land before it, the right half to land after. That is what makes a side-by-side
                // row reorderable without a separate gesture.
                var chips = CardRowOf(box)?.Children.OfType<Border>().ToList() ?? [];

                if (chips.Count == 0)
                {
                    _zones.Add(new DropZone(bounds, DropKind.IntoPanel, panel.Id, panel.Column, 0));
                    continue;
                }

                for (var i = 0; i < chips.Count; i++)
                {
                    var chipBounds = BoundsOf(chips[i]);
                    var half = chipBounds.Width / 2;

                    _zones.Add(new DropZone(
                        new Rect(chipBounds.X, bounds.Y, half, bounds.Height),
                        DropKind.IntoPanel, panel.Id, panel.Column, i));

                    _zones.Add(new DropZone(
                        new Rect(chipBounds.X + half, bounds.Y, half, bounds.Height),
                        DropKind.IntoPanel, panel.Id, panel.Column, i + 1));
                }
            }
        }

        var trayBounds = BoundsOf(_trayBox);
        if (trayBounds.Width > 0)
            _zones.Add(new DropZone(trayBounds, DropKind.Tray, null, 0, 0));
    }

    /// <summary>
    /// The card row inside a panel box. Looked up rather than assumed, because
    /// <see cref="ShowPanelAction"/> wraps the row in a StackPanel alongside the action button — and
    /// a hard-coded <c>box.Child as Grid</c> would silently return null there, collapsing every
    /// card-level insertion zone down to one whole-panel zone and quietly killing reordering.
    /// </summary>
    private static Grid? CardRowOf(Border box) => FirstGrid(box.Child);

    private static Grid? FirstGrid(UIElement? element) => element switch
    {
        Grid grid => grid,
        Panel panel => panel.Children.Select(FirstGrid).FirstOrDefault(g => g is not null),
        _ => null,
    };

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
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0x2D, 0x5B, 0x8A)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xE0, 0x4A, 0x82, 0xC4)),
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TranslateTransform(),
        });

        Place(_dragPreview!, zone.Bounds);

        // Inside a panel the wash alone can't say WHERE in the row it lands, so an insertion bar
        // marks the exact seam.
        if (zone.Kind == DropKind.IntoPanel)
        {
            _insertionBar ??= CreateAndAdd(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0x82, 0xC4)),
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = new TranslateTransform(),
            });

            var x = zone.Index == 0 ? zone.Bounds.X : zone.Bounds.X + zone.Bounds.Width;
            Place(_insertionBar!, new Rect(x - 1.5, zone.Bounds.Y, 3, zone.Bounds.Height));
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

    // ---- Panel resize ---------------------------------------------------------------------------

    private void OnGripPressed(object sender, PointerRoutedEventArgs e, string panelId)
    {
        if (sender is not Border grip) return;

        var target = _layout.Panels.FirstOrDefault(p => p.Id == panelId);
        if (target is null) return;

        _resizePanelId = panelId;
        _resizeStartY = e.GetCurrentPoint(this).Position.Y;
        _resizeStartHeight = Math.Clamp(target.MinHeight, MinPanelHeight, MaxPanelHeight);

        // The panel box is the grip's grandparent (box -> body -> grip); resize it live rather than
        // rebuilding on every pointer move, which would drop the capture mid-drag.
        _resizeBox = (grip.Parent as FrameworkElement)?.Parent as Border;

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

        var target = _layout.Panels.FirstOrDefault(p => p.Id == _resizePanelId);
        var applied = (int)Math.Round(_resizeBox?.MinHeight ?? _resizeStartHeight);

        _resizePanelId = null;
        _resizeBox = null;

        if (target is null || applied == _resizeStartHeight) return;

        target.MinHeight = Math.Clamp(applied, MinPanelHeight, MaxPanelHeight);

        // Only now — a Commit per pointer move would rebuild the tree under the captured pointer.
        Commit();
    }

    private void ApplyDrop(string cardId, DropZone zone)
    {
        var source = _layout.Panels.FirstOrDefault(p => p.CardIds.Contains(cardId));
        var sourceIndex = source?.CardIds.IndexOf(cardId) ?? -1;

        source?.CardIds.Remove(cardId);
        _layout.TrayCardIds.Remove(cardId);

        switch (zone.Kind)
        {
            case DropKind.Tray:
                _layout.TrayCardIds.Add(cardId);
                break;

            case DropKind.NewPanel:
                _layout.Panels.Add(new CardDockerPanel
                {
                    Id = NewPanelId(),
                    Column = zone.Column,
                    Sort = int.MaxValue,
                    CardIds = { cardId },
                });
                break;

            case DropKind.IntoPanel:
                var target = _layout.Panels.FirstOrDefault(p => p.Id == zone.PanelId);
                if (target is null)
                {
                    // The panel went away mid-drag (it held only this card). Put it back rather than
                    // dropping the card on the floor.
                    RestoreTo(source, sourceIndex, cardId);
                    break;
                }

                // Removing the card first can shift the target index left by one.
                var index = zone.Index;
                if (ReferenceEquals(source, target) && sourceIndex >= 0 && sourceIndex < index) index--;

                target.CardIds.Insert(Math.Clamp(index, 0, target.CardIds.Count), cardId);
                break;
        }

        Commit();
    }

    private static void RestoreTo(CardDockerPanel? panel, int index, string cardId)
    {
        if (panel is null || index < 0) return;
        panel.CardIds.Insert(Math.Clamp(index, 0, panel.CardIds.Count), cardId);
    }

    private string NewPanelId()
    {
        var existing = _layout.Panels.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = 1; ; i++)
        {
            var candidate = $"p{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    private enum DropKind { IntoPanel, NewPanel, Tray }

    private sealed record DropZone(Rect Bounds, DropKind Kind, string? PanelId, int Column, int Index);
}
