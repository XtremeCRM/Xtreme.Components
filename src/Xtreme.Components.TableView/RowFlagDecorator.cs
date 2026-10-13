using System.Runtime.CompilerServices;
using WinUI.TableView;
using Xtreme.Components.Core;

namespace Xtreme.Components.TableView;

/// <summary>A row's flag: the tag text, the brush resource key for the frame and tag, and a tooltip.</summary>
public sealed record RowFlag(string Label, string BrushKey, string? Tip = null);

/// <summary>
/// Frames a flagged <see cref="WinUI.TableView.TableView"/> row in a 2px coloured border and pins a
/// small caps tag (for example "CHANGED") to its bottom-left corner.
/// </summary>
/// <remarks>
/// Applied per realized row CONTAINER: containers recycle, so every pass sets or clears both the frame
/// and the tag. Every row carries the same 2px border (transparent when unflagged) because the list
/// estimates its scroll extent from one realized row, and taller framed rows left the last rows
/// unreachable. A flag that changes after the row is drawn needs <see cref="Refresh"/>.
/// <para>
/// Brush keys resolve through the <c>resolveBrush</c> delegate given to <see cref="Attach"/>; the
/// default looks the key up for the row's own <c>ActualTheme</c> through
/// <see cref="ComponentHost.TryGetBrush(string, ElementTheme)"/>, so a theme switch repaints
/// whatever row the recycled container holds at that moment.
/// </para>
/// </remarks>
public static class RowFlagDecorator
{
    /// <summary>Resolves a brush resource key for the element that will paint it.</summary>
    public delegate Brush? BrushResolver(FrameworkElement element, string key);

    private sealed class Registration(Func<object?, RowFlag?> flagOf, BrushResolver resolveBrush)
    {
        public Func<object?, RowFlag?> FlagOf { get; } = flagOf;
        public BrushResolver ResolveBrush { get; } = resolveBrush;
    }

    private static readonly ConditionalWeakTable<WinUI.TableView.TableView, Registration> Tables = new();
    private static readonly ConditionalWeakTable<Control, Border> Tags = new();
    private static readonly ConditionalWeakTable<Control, object> ThemeTracked = new();

    /// <summary>The default resolver: the key for the element's actual theme, else null.</summary>
    public static Brush? DefaultResolveBrush(FrameworkElement element, string key)
        => ComponentHost.TryGetBrush(key, element.ActualTheme);

    /// <summary>
    /// Decorate <paramref name="table"/>'s rows with whatever <paramref name="flagOf"/> returns (null =
    /// unflagged). <paramref name="resolveBrush"/> turns a <see cref="RowFlag.BrushKey"/> into a brush;
    /// null uses <see cref="DefaultResolveBrush"/>.
    /// </summary>
    public static void Attach(WinUI.TableView.TableView table, Func<object?, RowFlag?> flagOf, BrushResolver? resolveBrush = null)
    {
        var registration = new Registration(flagOf, resolveBrush ?? DefaultResolveBrush);
        Tables.AddOrUpdate(table, registration);
        table.ContainerContentChanging += (_, args) =>
        {
            if (args.ItemContainer is { } container)
                Apply(table, container, registration.FlagOf(args.Item));
        };
    }

    /// <summary>Repaint every realized row; call after a row's flag changes without the list re-binding.</summary>
    public static void Refresh(WinUI.TableView.TableView table)
    {
        if (!Tables.TryGetValue(table, out var registration) || table.ItemsSource is not System.Collections.IEnumerable items)
            return;

        foreach (var item in items)
        {
            if (table.ContainerFromItem(item) is Control container)
                Apply(table, container, registration.FlagOf(item));
        }
    }

    private static void Apply(WinUI.TableView.TableView table, Control container, RowFlag? flag)
    {
        if (!Tables.TryGetValue(table, out var registration)) return;

        if (!ThemeTracked.TryGetValue(container, out _))
        {
            ThemeTracked.Add(container, new object());
            container.ActualThemeChanged += (_, _) => Repaint(table, container);
        }

        ApplyFrame(container, flag, registration.ResolveBrush);
        ApplyTag(table, container, flag, registration.ResolveBrush);
    }

    private static void Repaint(WinUI.TableView.TableView table, Control container)
    {
        if (!Tables.TryGetValue(table, out var registration)) return;
        var item = (container as ContentControl)?.Content ?? container.DataContext;
        Apply(table, container, registration.FlagOf(item));
    }

    private static void ApplyFrame(Control container, RowFlag? flag, BrushResolver resolve)
    {
        container.BorderThickness = new Thickness(2);
        container.BorderBrush = (flag is not null ? resolve(container, flag.BrushKey) : null)
            ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    // The tag lives in the row's own template panel, not a cell (cells clip), anchored bottom-left
    // just inside the frame and drawn over everything else.
    private static void ApplyTag(WinUI.TableView.TableView table, Control container, RowFlag? flag, BrushResolver resolve)
    {
        if (!Tags.TryGetValue(container, out var tag))
        {
            if (flag is null) return;

            // The template may not be applied yet on the first pass; retry once the row has loaded,
            // re-reading the flag then because the container may hold a different (recycled) row.
            if (FindFirstPanel(container) is not Panel host)
            {
                void OnLoaded(object s, RoutedEventArgs e)
                {
                    container.Loaded -= OnLoaded;
                    Repaint(table, container);
                }
                container.Loaded += OnLoaded;
                return;
            }

            tag = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(6, 0, 6, 1),
                CornerRadius = new CornerRadius(0, 4, 0, 0),
                IsHitTestVisible = flag.Tip is not null,
                Child = new TextBlock
                {
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    FontSize = 8,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                },
            };
            Grid.SetColumnSpan(tag, 99);
            Grid.SetRowSpan(tag, 99);
            Canvas.SetZIndex(tag, 100);
            host.Children.Add(tag);
            Tags.Add(container, tag);
        }

        if (flag is null)
        {
            tag.Visibility = Visibility.Collapsed;
            return;
        }

        ((TextBlock)tag.Child).Text = flag.Label;
        tag.Background = resolve(container, flag.BrushKey) ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
        ToolTipService.SetToolTip(tag, flag.Tip);
        tag.Visibility = Visibility.Visible;
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
