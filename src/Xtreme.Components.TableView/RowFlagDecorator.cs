using System.Runtime.CompilerServices;
using WinUI.TableView;
using Xtreme.Components.Core;

namespace Xtreme.Components.TableView;

/// <summary>A row's flag: the tag text, the brush resource key for the frame and tag, and a tooltip.</summary>
public sealed record RowFlag(string Label, string BrushKey, string? Tip = null);

/// <summary>
/// Frames a flagged <see cref="WinUI.TableView.TableView"/> row in a 2px colored border and pins a
/// small caps tag (for example "CHANGED") to its bottom-left corner.
/// </summary>
/// <remarks>
/// Applied per realized row CONTAINER: containers recycle, so every pass sets or clears both the frame
/// and the tag. The frame is an overlay drawn inside the row's template panel, not the container's own
/// border, so a flag never changes a row's size (the list estimates its scroll extent from one realized
/// row, and taller framed rows once left the last rows unreachable) and the container's
/// <c>BorderBrush</c> / <c>BorderThickness</c> stay whatever the app or the row style set. A flag that
/// changes after the row is drawn needs <see cref="Refresh"/>.
/// <para>
/// Brush keys resolve through <see cref="RowFlagOptions.ResolveBrush"/> (or the <c>resolveBrush</c>
/// delegate given to <see cref="Attach(WinUI.TableView.TableView, Func{object?, RowFlag?}, BrushResolver?)"/>);
/// the default looks the key up for the row's own <c>ActualTheme</c> through
/// <see cref="ComponentHost.TryGetBrush(string, ElementTheme)"/>, so a theme switch repaints
/// whatever row the recycled container holds at that moment.
/// </para>
/// </remarks>
public static class RowFlagDecorator
{
    /// <summary>Resolves a brush resource key for the element that will paint it.</summary>
    public delegate Brush? BrushResolver(FrameworkElement element, string key);

    private sealed class Registration(Func<object?, RowFlag?> flagOf, RowFlagOptions options)
    {
        public Func<object?, RowFlag?> FlagOf { get; } = flagOf;
        public RowFlagOptions Options { get; } = options;
        public TypedEventHandler<ListViewBase, ContainerContentChangingEventArgs>? Handler { get; set; }
        public BrushResolver Resolve => Options.ResolveBrush ?? DefaultResolveBrush;
    }

    // The overlay pair one row container carries once it has shown a flag; reused across recycles.
    private sealed class Decoration(Border frame, Border tag, TextBlock text)
    {
        public Border Frame { get; } = frame;
        public Border Tag { get; } = tag;
        public TextBlock Text { get; } = text;
    }

    private static readonly ConditionalWeakTable<WinUI.TableView.TableView, Registration> Tables = new();
    private static readonly ConditionalWeakTable<Control, Decoration> Decorations = new();
    private static readonly ConditionalWeakTable<Control, object> ThemeTracked = new();
    private static readonly ConditionalWeakTable<Control, object> AwaitingLoad = new();

    /// <summary>The default resolver: the key for the element's actual theme, else null.</summary>
    public static Brush? DefaultResolveBrush(FrameworkElement element, string key)
        => ComponentHost.TryGetBrush(key, element.ActualTheme);

    /// <summary>
    /// Decorate <paramref name="table"/>'s rows with whatever <paramref name="flagOf"/> returns (null =
    /// unflagged). <paramref name="resolveBrush"/> turns a <see cref="RowFlag.BrushKey"/> into a brush;
    /// null uses <see cref="DefaultResolveBrush"/>. Attaching again replaces the earlier registration.
    /// </summary>
    // RS0027 wants the optional-parameter overload to have the most parameters, so that adding an
    // overload cannot make an existing call ambiguous. Both have three here and the priority keeps
    // every existing call (including Attach(table, flagOf, null)) bound to this one.
#pragma warning disable RS0027
    [OverloadResolutionPriority(1)]
    public static void Attach(WinUI.TableView.TableView table, Func<object?, RowFlag?> flagOf, BrushResolver? resolveBrush = null)
        => Attach(table, flagOf, new RowFlagOptions { ResolveBrush = resolveBrush });
#pragma warning restore RS0027

    /// <summary>
    /// Decorate <paramref name="table"/>'s rows with whatever <paramref name="flagOf"/> returns (null =
    /// unflagged), resolving brushes and drawing the frame and tag as <paramref name="options"/> says.
    /// Attaching again to the same table replaces the earlier registration: its handler is
    /// unsubscribed and the realized rows are repainted from the new one.
    /// </summary>
    public static void Attach(WinUI.TableView.TableView table, Func<object?, RowFlag?> flagOf, RowFlagOptions options)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(flagOf);
        ArgumentNullException.ThrowIfNull(options);

        Unsubscribe(table);

        var registration = new Registration(flagOf, options);
        registration.Handler = (_, args) =>
        {
            if (args.ItemContainer is { } container)
                Apply(table, registration, container, flagOf(args.Item));
        };
        Tables.AddOrUpdate(table, registration);
        table.ContainerContentChanging += registration.Handler;

        // Rows already on screen were painted by the previous registration, or not at all.
        Refresh(table);
    }

    /// <summary>
    /// Remove the decorator from <paramref name="table"/>: stops painting new rows and clears the frame
    /// and tag from the realized ones. Returns false when the table was not decorated.
    /// </summary>
    public static bool Detach(WinUI.TableView.TableView table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!Unsubscribe(table))
            return false;

        foreach (var container in RealizedContainers(table))
        {
            if (Decorations.TryGetValue(container, out var decoration))
                Clear(decoration);
        }
        return true;
    }

    /// <summary>True when <paramref name="table"/> currently has the decorator attached.</summary>
    public static bool IsAttached(WinUI.TableView.TableView table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Tables.TryGetValue(table, out _);
    }

    /// <summary>
    /// Repaint the realized rows; call after a row's flag changes without the list re-binding. Only
    /// containers the list currently holds are visited, so the cost follows the visible row count,
    /// not the size of <c>ItemsSource</c>.
    /// </summary>
    public static void Refresh(WinUI.TableView.TableView table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!Tables.TryGetValue(table, out var registration))
            return;

        foreach (var container in RealizedContainers(table))
            Apply(table, registration, container, registration.FlagOf(ItemOf(table, container)));
    }

    // Drops the table's registration and its ContainerContentChanging handler. True if there was one.
    private static bool Unsubscribe(WinUI.TableView.TableView table)
    {
        if (!Tables.TryGetValue(table, out var old))
            return false;
        if (old.Handler is not null)
            table.ContainerContentChanging -= old.Handler;
        Tables.Remove(table);
        return true;
    }

    // The row containers the items panel holds right now (on-screen rows plus the panel's small
    // realization buffer).
    private static IEnumerable<Control> RealizedContainers(WinUI.TableView.TableView table)
    {
        if (table.ItemsPanelRoot is not Panel panel)
            yield break;
        // Snapshot: painting can realize a template and the panel may add children while we iterate.
        foreach (var child in panel.Children.ToArray())
        {
            if (child is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container)
                yield return container;
        }
    }

    private static object? ItemOf(WinUI.TableView.TableView table, Control container)
        => table.ItemFromContainer(container) ?? (container as ContentControl)?.Content ?? container.DataContext;

    private static void Apply(WinUI.TableView.TableView table, Registration registration, Control container, RowFlag? flag)
    {
        if (!ThemeTracked.TryGetValue(container, out _))
        {
            ThemeTracked.Add(container, new object());
            container.ActualThemeChanged += (_, _) => Repaint(table, container);
        }

        if (!Decorations.TryGetValue(container, out var decoration))
        {
            if (flag is null) return;   // nothing to clear and nothing to draw
            decoration = Create(table, container);
            if (decoration is null) return;   // template not applied yet; retried on Loaded
        }

        if (flag is null)
        {
            Clear(decoration);
            return;
        }

        var options = registration.Options;
        var brush = registration.Resolve(container, flag.BrushKey);

        var frame = decoration.Frame;
        frame.BorderThickness = new Thickness(Math.Max(0, options.FrameThickness));
        frame.CornerRadius = options.FrameCornerRadius;
        frame.BorderBrush = brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        frame.Visibility = Visibility.Visible;

        var tag = decoration.Tag;
        tag.Padding = options.TagPadding;
        tag.CornerRadius = options.TagCornerRadius;
        tag.Background = brush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
        // Per flag, because the container (and so the tag) is recycled across rows: a tag without a
        // tooltip must not swallow pointer input meant for the row underneath.
        tag.IsHitTestVisible = flag.Tip is not null;
        ToolTipService.SetToolTip(tag, flag.Tip);
        decoration.Text.Text = flag.Label;
        decoration.Text.FontSize = options.TagFontSize;
        decoration.Text.Foreground = options.TagForeground ?? new SolidColorBrush(Microsoft.UI.Colors.White);
        tag.Visibility = Visibility.Visible;
    }

    private static void Clear(Decoration decoration)
    {
        decoration.Frame.Visibility = Visibility.Collapsed;
        decoration.Tag.Visibility = Visibility.Collapsed;
        decoration.Tag.IsHitTestVisible = false;
        ToolTipService.SetToolTip(decoration.Tag, null);
    }

    private static void Repaint(WinUI.TableView.TableView table, Control container)
    {
        if (!Tables.TryGetValue(table, out var registration)) return;
        Apply(table, registration, container, registration.FlagOf(ItemOf(table, container)));
    }

    // The frame and tag live in the row's own template panel, not a cell (cells clip), drawn over
    // everything else: the frame stretched over the panel, the tag anchored bottom-left. Null when
    // the template is not applied yet; the row is then repainted once it has loaded, re-reading the
    // flag because the container may hold a different (recycled) row by then.
    private static Decoration? Create(WinUI.TableView.TableView table, Control container)
    {
        if (FindFirstPanel(container) is not Panel host)
        {
            if (!AwaitingLoad.TryGetValue(container, out _))
            {
                AwaitingLoad.Add(container, new object());
                void OnLoaded(object s, RoutedEventArgs e)
                {
                    container.Loaded -= OnLoaded;
                    AwaitingLoad.Remove(container);
                    Repaint(table, container);
                }
                container.Loaded += OnLoaded;
            }
            return null;
        }

        var frame = new Border { IsHitTestVisible = false };
        var text = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var tag = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = text,
        };
        foreach (var part in new FrameworkElement[] { frame, tag })
        {
            Grid.SetColumnSpan(part, 99);
            Grid.SetRowSpan(part, 99);
            host.Children.Add(part);
        }
        Canvas.SetZIndex(frame, 99);
        Canvas.SetZIndex(tag, 100);

        var decoration = new Decoration(frame, tag, text);
        Decorations.Add(container, decoration);
        return decoration;
    }

    // The row template's root layout panel (a few levels down at most).
    private static Panel? FindFirstPanel(DependencyObject root)
    {
        var queue = new Queue<(DependencyObject Node, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (node is Panel p && !ReferenceEquals(node, root)) return p;
            if (depth >= 4) continue;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                queue.Enqueue((VisualTreeHelper.GetChild(node, i), depth + 1));
        }
        return null;
    }
}
