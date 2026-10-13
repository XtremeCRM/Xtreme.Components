using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Xtreme.Components.Docking.Controls;

/// <summary>
/// The title bar above a docked pane: its name, and the actions available on it.
/// </summary>
/// <remarks>
/// The float affordance is decided by the HOST, not the item. <see cref="DockItem.AllowFloat"/> only
/// says the pane is permitted to float; whether the platform can put it in an OS window at all is a
/// runtime fact (false on WASM and mobile). Both must hold before the button exists, otherwise the
/// non-desktop heads would show a control that cannot work.
/// </remarks>
internal sealed partial class DockItemHeader : Grid
{
    internal event EventHandler? CloseRequested;
    internal event EventHandler? PinRequested;

    /// <summary>Raised by the header's context menu — tear this pane out into its own OS window.</summary>
    internal event EventHandler? FloatRequested;

    /// <summary>
    /// Raised when a header drag begins, carrying the pointer's start position RELATIVE TO THE HEADER.
    /// The host needs it to track the actual pointer: cumulative translation alone only locates the
    /// header's corner, and against 44px guide targets the grab-offset error (wherever in the header
    /// the user grabbed) would make hover detection feel broken.
    /// </summary>
    internal event EventHandler<Windows.Foundation.Point>? DragStarted;

    /// <summary>Cumulative translation since drag start, in DIPs — same contract as DockSplitter.</summary>
    internal event Action<Windows.Foundation.Point>? DragMoved;

    internal event EventHandler? DragCompleted;

    internal DockItemHeader(DockItem item, bool platformSupportsFloating)
    {
        Height = 22; // the compact DX-style title bar
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0x80, 0x80, 0x80));

        // The header doubles as the drag handle for re-docking. RAW pointer events with an explicit
        // threshold and capture — NOT manipulation events, deliberately, after two observed failures
        // of the manipulation pipeline in this exact spot: (1) on SelectorBarItem tabs it never
        // engaged at all (ItemContainer's selection input wins), and (2) headers received PHANTOM
        // manipulations that auto-started with no user press right after a pool Window construction,
        // leaving a stuck drag that swallowed the next real one. A raw press can't phantom-start: the
        // gesture arms only on a genuine PointerPressed routed to this header. The buttons still
        // work: the drag engages only past the movement threshold, so a plain click never becomes a
        // drag — and the press listener uses handledEventsToo so a press that starts on a button can
        // still turn into a drag if the user pulls away instead of completing the click.
        const double dragThreshold = 6;
        uint pressedPointer = 0;
        var pressed = false;
        var dragging = false;
        var pressLocal = default(Windows.Foundation.Point);

        void FinishDrag(bool commit)
        {
            if (dragging && commit) DragCompleted?.Invoke(this, EventArgs.Empty);
            pressed = false;
            dragging = false;
        }

        AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (pressed) return;
            pressed = true;
            dragging = false;
            pressedPointer = e.Pointer.PointerId;
            pressLocal = e.GetCurrentPoint(this).Position;
        }), handledEventsToo: true);

        AddHandler(PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (!pressed || e.Pointer.PointerId != pressedPointer) return;

            var current = e.GetCurrentPoint(this).Position;
            var dx = current.X - pressLocal.X;
            var dy = current.Y - pressLocal.Y;

            if (!dragging)
            {
                if (dx * dx + dy * dy < dragThreshold * dragThreshold) return;
                dragging = true;
                CapturePointer(e.Pointer); // keep the move stream when the pointer leaves the header
                DragStarted?.Invoke(this, pressLocal);
            }

            // The header never moves mid-drag (the host defers all layout mutation to the commit),
            // so header-relative displacement IS the cumulative translation the host expects.
            DragMoved?.Invoke(new Windows.Foundation.Point(dx, dy));
        }), handledEventsToo: true);

        AddHandler(PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (pressed && e.Pointer.PointerId == pressedPointer) FinishDrag(commit: true);
        }), handledEventsToo: true);

        AddHandler(PointerCanceledEvent, new PointerEventHandler((_, e) =>
        {
            if (pressed && e.Pointer.PointerId == pressedPointer) FinishDrag(commit: true);
        }), handledEventsToo: true);

        PointerCaptureLost += (_, e) =>
        {
            // Only this header's own capture ending matters; a child button losing ITS capture
            // bubbles through here and must not end a drag.
            if (pressed && dragging && e.Pointer.PointerId == pressedPointer && ReferenceEquals(e.OriginalSource, this))
                FinishDrag(commit: true);
        };

        // Right-click menu — where DevExpress and Visual Studio put "Float", and the reason it exists
        // here: drag-to-float only triggers 32 DIP PAST the host's bounds, which is unreachable for a
        // pane docked against the host's outer edge (the Scheduler Map was the case that surfaced it —
        // dragging right just leaves the app). The menu makes floating reachable from any pane
        // wherever it sits, without tuning the drag.
        //
        // Rebuilt on every open rather than cached, so enablement always reflects CURRENT state — an
        // item's AllowFloat and IsFloating both change at runtime.
        RightTapped += (_, e) =>
        {
            var menu = new MenuFlyout();

            if (platformSupportsFloating && item.AllowFloat && !item.IsFloating)
            {
                var floatItem = new MenuFlyoutItem { Text = "Float" };
                floatItem.Click += (_, _) => FloatRequested?.Invoke(this, EventArgs.Empty);
                menu.Items.Add(floatItem);
            }

            if (item.ShowPinToggle)
            {
                var pinItem = new MenuFlyoutItem { Text = item.IsPinned ? "Unpin (auto-hide)" : "Pin" };
                pinItem.Click += (_, _) => PinRequested?.Invoke(this, EventArgs.Empty);
                menu.Items.Add(pinItem);
            }

            if (item.CanClose)
            {
                var closeItem = new MenuFlyoutItem { Text = "Close" };
                closeItem.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
                menu.Items.Add(closeItem);
            }

            // Nothing applicable (a pinned, unfloatable, unclosable pane) — show no empty menu.
            if (menu.Items.Count == 0) return;

            menu.ShowAt(this, e.GetPosition(this));
            e.Handled = true;
        };

        // No float BUTTON: the header stays uncluttered; dragging out and the menu above are the two
        // float paths.
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // pin
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // close

        var title = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            // Never wrap: a wrapping TextBlock inside a width-constrained container is the shape that
            // drives Uno into a layout cycle, and a fixed-height header has nowhere to wrap to anyway.
            TextWrapping = TextWrapping.NoWrap,
        };
        title.SetBinding(TextBlock.TextProperty, new Binding
        {
            Source = item,
            Path = new PropertyPath(nameof(DockItem.Title)),
            Mode = BindingMode.OneWay,
        });
        SetColumn(title, 0);
        Children.Add(title);

        if (item.ShowPinToggle)
        {
            // The pushpin: FontIcon glyphs E718 (pin) / E77A (slashed unpin) swapped by state.
            // FontIcon's DEFAULT FontFamily is the theme's SymbolThemeFontFamily — the sanctioned,
            // per-platform-resolved symbol font, not a hardcoded "Segoe *" name (which is the actual
            // macOS missing-glyph trap). Glyphs spelled as \u escapes — the mojibake lesson.
            var pin = BuildIconButton(item.IsPinned ? "\uE718" : "\uE77A",
                item.IsPinned ? "Unpin (auto-hide)" : "Pin");
            pin.Click += (_, _) => PinRequested?.Invoke(this, EventArgs.Empty);
            SetColumn(pin, 1);
            Children.Add(pin);
        }

        if (item.CanClose)
        {
            var close = BuildButton("✕", "Close"); // ✕
            close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
            SetColumn(close, 2);
            Children.Add(close);
        }
    }

    // Plain unicode glyphs (spelled as \u escapes) in a TextBlock, NOT FontIcon + a symbol font. The
    // first version of this file used symbol-font PUA characters typed literally — invisible in most
    // editors, which is exactly how they shipped encoding-mangled into mojibake without anyone
    // noticing. A hardcoded "Segoe *" family is also a missing-glyph box once bundled for macOS.
    // Internal: the tab-group strip in DockHost renders the same affordances and must match.
    internal static Button BuildButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    // FontIcon variant for glyphs that exist in the THEME symbol font (SymbolThemeFontFamily — the
    // per-platform-resolved resource, which is FontIcon's default). Distinct from the hardcoded-"Segoe *" trap: the
    // theme resource maps correctly on every head, proven by the Shell's own pin rendering on Skia.
    internal static Button BuildIconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 10,
            },
        };

        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }
}
