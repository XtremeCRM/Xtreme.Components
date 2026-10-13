using System.IO;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Xtreme.Components.Scheduler.Models;

namespace Xtreme.Components.Scheduler.Rendering;

/// <summary>
/// Builds the visual element for an appointment in one of three shapes: a full time-grid block
/// (Day/Week), a compact chip (all-day banner / Month cell) and a horizontal bar (Planner).
/// Each shape resolves in the same order: a host code renderer, then a host <see cref="DataTemplate"/>,
/// then the built-in default. Defaults show subject + time + an indicator strip (recurrence, locked,
/// complete, canceled and any custom <see cref="AppointmentIndicator"/>s); colors come from the
/// appointment with palette fallbacks and the text color auto-contrasts the fill.
/// </summary>
internal static class EventBlockFactory
{
    // Segoe Fluent Icons / MDL2 codepoints for the standard status glyphs (kept as ASCII codepoint
    // expressions so the source carries no non-ASCII bytes).
    private static readonly string RecurrenceGlyph = ((char)0xE8EE).ToString(); // RepeatAll
    private static readonly string LockedGlyph = ((char)0xE72E).ToString();     // Lock
    private static readonly string CompleteGlyph = ((char)0xE73E).ToString();   // CheckMark
    private static readonly string ConfirmedGlyph = ((char)0xEC61).ToString();  // CompletedSolid (circle-check)
    private static readonly string CanceledGlyph = ((char)0xE711).ToString();  // Cancel
    private static readonly string TentativeGlyph = ((char)0xE897).ToString();  // Help (question mark)

    // Status-glyph colors, all read against the gray chip: Complete = green, Confirmed = blue, and a
    // fixed near-black for the neutral statuses (never gray, so they don't blend into the chip).
    private static readonly SolidColorBrush CompleteGreen = new(Color.FromArgb(0xFF, 0x2E, 0x7D, 0x32));
    private static readonly SolidColorBrush ConfirmedBlue = new(Color.FromArgb(0xFF, 0x15, 0x65, 0xC0));
    private static readonly SolidColorBrush GlyphDark = new(Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B));

    /// <summary>Full block for the time grid: accent bar + subject + time range.</summary>
    public static FrameworkElement CreateTimed(AppointmentModel appt, SchedulerPalette palette, AppointmentTemplating templating)
    {
        var ctx = new AppointmentContext(appt, palette, AppointmentSurface.Timed, showTime: true, templating.CornerRadius, templating.ShowTiles);
        return Render(ctx, templating, templating.TimedTemplate, DefaultTimed);
    }

    /// <summary>Compact single-line chip for the all-day banner and Month cells.</summary>
    public static FrameworkElement CreateChip(AppointmentModel appt, SchedulerPalette palette, AppointmentTemplating templating, bool showTime = false)
    {
        var ctx = new AppointmentContext(appt, palette, AppointmentSurface.Chip, showTime, cornerRadius: 3, templating.ShowTiles);
        return Render(ctx, templating, templating.ChipTemplate, DefaultChip);
    }

    /// <summary>Horizontal bar for the continuous timeline: left accent stripe + subject (single line).</summary>
    public static FrameworkElement CreateBar(AppointmentModel appt, SchedulerPalette palette, AppointmentTemplating templating)
    {
        var ctx = new AppointmentContext(appt, palette, AppointmentSurface.Bar, showTime: false, cornerRadius: 3, templating.ShowTiles);
        return Render(ctx, templating, templating.BarTemplate, DefaultBar);
    }

    // Resolution order: code renderer -> DataTemplate -> built-in default. The first two are wrapped
    // in standard chrome (fill + corner + hit-test Tag) so geometry/interaction stay consistent.
    private static FrameworkElement Render(
        AppointmentContext ctx,
        AppointmentTemplating templating,
        DataTemplate? template,
        Func<AppointmentContext, FrameworkElement> fallback)
    {
        var block = BuildBlock(ctx, templating, template, fallback);
        AttachTooltip(block, ctx, templating);
        return block;
    }

    private static FrameworkElement BuildBlock(
        AppointmentContext ctx,
        AppointmentTemplating templating,
        DataTemplate? template,
        Func<AppointmentContext, FrameworkElement> fallback)
    {
        if (templating.Renderer?.Invoke(ctx) is { } content)
        {
            return Chrome(ctx, content);
        }
        if (template is not null)
        {
            return new ContentControl
            {
                Content = ctx,
                ContentTemplate = template,
                Foreground = ctx.Foreground,
                Background = ctx.BackBrush,
                BorderBrush = ctx.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(ctx.CornerRadius),
                Tag = ctx.Appointment,
                IsTabStop = false,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
        }
        return fallback(ctx);
    }

    // Hover popup, resolved the same way as the block: code renderer -> DataTemplate -> default.
    private static void AttachTooltip(FrameworkElement block, AppointmentContext ctx, AppointmentTemplating templating)
        => ToolTipService.SetToolTip(block, new ToolTip { Content = BuildTooltipContent(ctx, templating) });

    /// <summary>Builds the hover-popup content for an appointment (renderer -> template -> default).
    /// Used by views whose pointer overlay covers the blocks, so they drive the popup on hover.</summary>
    internal static FrameworkElement BuildTooltipContent(AppointmentModel appt, SchedulerPalette palette, AppointmentTemplating templating)
        => BuildTooltipContent(new AppointmentContext(appt, palette, AppointmentSurface.Timed, showTime: true, templating.CornerRadius), templating);

    private static FrameworkElement BuildTooltipContent(AppointmentContext ctx, AppointmentTemplating templating)
    {
        if (templating.TooltipRenderer?.Invoke(ctx) is { } custom)
        {
            return custom;
        }
        if (templating.TooltipTemplate is not null)
        {
            return new ContentControl { Content = ctx, ContentTemplate = templating.TooltipTemplate };
        }
        return DefaultTooltip(ctx);
    }

    // Wraps host-rendered content in the standard block chrome.
    private static FrameworkElement Chrome(AppointmentContext ctx, FrameworkElement content)
    {
        var border = new Border
        {
            Background = ctx.BackBrush,
            BorderBrush = ctx.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(ctx.CornerRadius),
            Child = WithTentativeStripe(ctx, content),
            DataContext = ctx,
            Tag = ctx.Appointment,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        return DecorationFactory.Apply(border, ctx);
    }

    // The time pill's background: a light, mostly-opaque gray so the time stays legible over an
    // overlapping note while reading as a neutral chip (not tied to the block's own color).
    // Internal so drag/resize previews can reuse the exact same pill look (see TimeGridInteraction).
    internal static readonly SolidColorBrush TimePillBackBrush =
        new(Color.FromArgb(0xE6, 0xDD, 0xDD, 0xDD));

    // Dark text for the time, since the pill is a fixed light gray (not the block's contrast color).
    internal static readonly SolidColorBrush TimePillForegroundBrush =
        new(Color.FromArgb(0xFF, 0x33, 0x33, 0x33));

    // ---- Built-in defaults ---------------------------------------------------------------------

    private static FrameworkElement DefaultTimed(AppointmentContext ctx)
    {
        var barRect = new Rectangle { Fill = ctx.BarBrush, Width = 3 };
        Grid.SetColumn(barRect, 0);

        var subject = new TextBlock
        {
            Text = ctx.Subject,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ctx.Foreground,
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        var time = new TextBlock
        {
            Text = ctx.CompactTimeRange,
            FontSize = 10,
            Foreground = TimePillForegroundBrush, // dark text — the pill is a fixed light gray, not the block color
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };

        // A translucent pill behind the time so it stays legible when a long note wraps down behind it.
        // The fill is the block's own color at 50% alpha: invisible against the clean block background,
        // but it mutes any overlapping note text enough for the time (full-contrast) to read on top.
        var timePill = new Border
        {
            Child = time,
            Background = TimePillBackBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5, 1, 5, 1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        // Name on top; when the event has a note, a divider then the note below it. Top / Bottom tiles
        // ride as rows above the subject / below the note.
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        if (BuildTiles(ctx, TileSide.Top, multiLine: true, 10) is { } topTiles)
        {
            topTiles.Margin = new Thickness(0, 0, 0, 2);
            info.Children.Add(topTiles);
        }
        info.Children.Add(subject);
        if (!string.IsNullOrWhiteSpace(ctx.Note))
        {
            info.Children.Add(new Border
            {
                Height = 1,
                Background = ctx.Foreground,
                Opacity = 0.25,
                Margin = new Thickness(0, 3, 0, 3),
            });
            info.Children.Add(new TextBlock
            {
                Text = ctx.Note,
                FontSize = 11,
                Opacity = 0.9,
                Foreground = ctx.Foreground,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        if (BuildTiles(ctx, TileSide.Bottom, multiLine: true, 10) is { } bottomTiles)
        {
            bottomTiles.Margin = new Thickness(0, 2, 0, 0);
            info.Children.Add(bottomTiles);
        }

        // Name/divider/note flow from the top; the compact time stays pinned bottom-right.
        var content = new Grid { Margin = new Thickness(5, 2, 4, 2) };
        content.Children.Add(info);
        content.Children.Add(timePill);
        Grid.SetColumn(content, 3);
        FitShortBlock(content, subject, timePill, ctx.Subject);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // accent bar
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // icon gutter (left)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // tile gutter (left)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // content
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // tile gutter (right)
        grid.Children.Add(barRect);
        grid.Children.Add(content);

        // Status icons live in a left gutter (vertical strip); the gutter column is Auto, so it
        // collapses to nothing when the appointment has no indicators. A locked block shows its lock
        // in the prominent red bottom strip instead, so suppress the (now redundant) gutter glyph.
        if (BuildIndicators(ctx, 12, Orientation.Vertical, includeLock: !ctx.IsLocked) is { } indicators)
        {
            indicators.VerticalAlignment = VerticalAlignment.Top;
            indicators.Margin = new Thickness(3, 3, 0, 3);
            Grid.SetColumn(indicators, 1);
            grid.Children.Add(indicators);
        }

        // Data tiles: vertical gutter strips at the left (after the status icons) and the right.
        if (BuildTiles(ctx, TileSide.Left, multiLine: true, 10) is { } leftTiles)
        {
            leftTiles.Margin = new Thickness(3, 3, 0, 3);
            Grid.SetColumn(leftTiles, 2);
            grid.Children.Add(leftTiles);
        }
        if (BuildTiles(ctx, TileSide.Right, multiLine: true, 10) is { } tiles)
        {
            tiles.Margin = new Thickness(2, 3, 3, 3);
            Grid.SetColumn(tiles, 4);
            grid.Children.Add(tiles);
        }

        return ctx.IsLocked ? LockedShell(ctx, grid) : Shell(ctx, grid);
    }

    // Approximate rendered heights of one 12pt subject line and of the 10pt time pill (text + padding).
    private const double SubjectLineHeight = 16;
    private const double TimePillHeight = 16;

    // Short blocks (XU-837): a multi-line subject (name + phone) ran down under the bottom-pinned time pill
    // and both became unreadable. Clamp the subject to the whole lines that fit above the pill; when there is
    // no room for even one line plus the pill, drop the pill (the time is in the tooltip and the block's position).
    private static void FitShortBlock(FrameworkElement content, TextBlock subject, FrameworkElement timePill, string? fullSubject)
    {
        var lines = (fullSubject ?? string.Empty).Replace("\r", string.Empty).Split('\n');
        content.SizeChanged += (_, e) =>
        {
            var h = e.NewSize.Height;
            var showPill = h >= SubjectLineHeight + TimePillHeight;
            timePill.Visibility = showPill ? Visibility.Visible : Visibility.Collapsed;

            var room = showPill ? h - TimePillHeight : h;
            var maxLines = Math.Max(1, (int)Math.Floor(room / SubjectLineHeight));
            subject.Text = lines.Length <= maxLines ? fullSubject : string.Join("\n", lines, 0, maxLines);
        };
    }

    private static FrameworkElement DefaultChip(AppointmentContext ctx)
    {
        var dot = new Rectangle
        {
            Fill = ctx.BarBrush,
            Width = 4,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, 5, 0),
        };
        Grid.SetColumn(dot, 0);

        var label = ctx.ShowTime && !ctx.IsAllDay ? $"{ctx.CompactStart}  {ctx.Subject}" : ctx.Subject;
        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = ctx.Foreground,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        Grid.SetColumn(text, 3);

        var grid = new Grid { Margin = new Thickness(3, 1, 5, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // tiles (left)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // tiles (right)
        grid.Children.Add(dot);
        grid.Children.Add(text);

        if (BuildIndicators(ctx, 11) is { } indicators)
        {
            indicators.VerticalAlignment = VerticalAlignment.Center;
            indicators.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(indicators, 1);
            grid.Children.Add(indicators);
        }

        AddLineTiles(grid, ctx, leftColumn: 2, rightColumn: 4);

        return Shell(ctx, grid);
    }

    // Tiles on a single-line surface (chip, bar): a left strip before the text and a right strip after it.
    private static void AddLineTiles(Grid grid, AppointmentContext ctx, int leftColumn, int rightColumn)
    {
        if (BuildTiles(ctx, TileSide.Left, multiLine: false, 9) is { } left)
        {
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(left, leftColumn);
            grid.Children.Add(left);
        }
        if (BuildTiles(ctx, TileSide.Right, multiLine: false, 9) is { } right)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(right, rightColumn);
            grid.Children.Add(right);
        }
    }

    private static FrameworkElement DefaultBar(AppointmentContext ctx)
    {
        var barRect = new Rectangle { Fill = ctx.BarBrush, Width = 3 };
        Grid.SetColumn(barRect, 0);

        var text = new TextBlock
        {
            Text = ctx.Subject,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ctx.Foreground,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        Grid.SetColumn(text, 3);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // tiles (left)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // tiles (right)
        grid.Children.Add(barRect);
        grid.Children.Add(text);

        if (BuildIndicators(ctx, 11) is { } indicators)
        {
            indicators.VerticalAlignment = VerticalAlignment.Center;
            indicators.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(indicators, 1);
            grid.Children.Add(indicators);
        }

        AddLineTiles(grid, ctx, leftColumn: 2, rightColumn: 4);

        return Shell(ctx, grid);
    }

    // Overlay a 45° candy-stripe on a Tentative block's content (Legacy parity for "undecided" events).
    // No-op otherwise, so a plain block keeps the same visual tree.
    private static FrameworkElement WithTentativeStripe(AppointmentContext ctx, FrameworkElement content)
    {
        if (!ctx.IsTentative)
            return content;

        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(new Border { Background = CandyStripeBrush(), IsHitTestVisible = false });
        return grid;
    }

    // Semi-transparent white diagonal stripes. Absolute mapping + Repeat spread = fixed-width 45° stripes
    // regardless of block size (the colored block shows through the gaps; text stays legible).
    private static Brush CandyStripeBrush()
    {
        var stripe = Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF); // softer hatch (~half the prior opacity)
        var clear = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(9, 9),
            SpreadMethod = GradientSpreadMethod.Repeat,
        };
        brush.GradientStops.Add(new GradientStop { Color = stripe, Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = stripe, Offset = 0.5 });
        brush.GradientStops.Add(new GradientStop { Color = clear, Offset = 0.5 });
        brush.GradientStops.Add(new GradientStop { Color = clear, Offset = 1.0 });
        return brush;
    }

    // The default block chrome shared by all three shapes.
    private static FrameworkElement Shell(AppointmentContext ctx, FrameworkElement content)
    {
        var border = new Border
        {
            Background = ctx.BackBrush,
            BorderBrush = ctx.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(ctx.CornerRadius),
            Child = WithTentativeStripe(ctx, content),
            DataContext = ctx,
            Tag = ctx.Appointment,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        return DecorationFactory.Apply(border, ctx);
    }

    // The red used for the locked treatment (matches the selection outline red so the palette stays tight).
    private static readonly SolidColorBrush LockedRed = new(Color.FromArgb(0xFF, 0xCC, 0x10, 0x10));
    private static readonly SolidColorBrush LockedGlyphForeground = new(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

    // Chrome for a LOCKED timed block: the normal block with a red strip docked along the bottom (a
    // centered white lock glyph) and a red bottom edge. The strip + red border flag — at a glance — that
    // the appointment can't be moved, resized or edited (the interaction layer enforces it).
    private static FrameworkElement LockedShell(AppointmentContext ctx, FrameworkElement content)
    {
        var lockStrip = new Border
        {
            Background = LockedRed,
            Height = 14,
            CornerRadius = new CornerRadius(0, 0, ctx.CornerRadius, ctx.CornerRadius),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new FontIcon
            {
                Glyph = LockedGlyph,
                FontSize = 10,
                Foreground = LockedGlyphForeground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTipService.SetToolTip(lockStrip, "Locked");

        // Content takes the remaining height; the lock strip is its own bottom row so it never hides
        // the time pill (which sits bottom-right of the content).
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(content, 0);
        layout.Children.Add(content);
        Grid.SetRow(lockStrip, 1);
        layout.Children.Add(lockStrip);

        var border = new Border
        {
            Background = ctx.BackBrush,
            BorderBrush = LockedRed,
            // Red bottom edge is the lock strip; the other three sides keep a normal-weight red outline.
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(ctx.CornerRadius),
            Child = WithTentativeStripe(ctx, layout),
            DataContext = ctx,
            Tag = ctx.Appointment,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        return DecorationFactory.Apply(border, ctx);
    }

    // ---- Default hover popup -------------------------------------------------------------------

    // The built-in popup, styled to echo the event tile: a color accent + name with the same
    // status-icon strip the block shows, the date, the full time range + duration, the participant
    // roles, then the public note and the internal memo. ToolTip / the hover card supply the themed
    // background + padding, so this is just the content.
    private static FrameworkElement DefaultTooltip(AppointmentContext ctx)
    {
        var panel = new StackPanel { Spacing = 4, MaxWidth = 340 };

        // Title row: subject (takes the width) + the same icon strip as the tile, pinned right.
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var subject = new TextBlock
        {
            Text = ctx.Subject,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14,
            Foreground = ctx.Foreground, // contrast text, matching the tile
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(subject, 0);
        titleRow.Children.Add(subject);

        if (BuildIndicators(ctx, 13) is { } indicators)
        {
            indicators.VerticalAlignment = VerticalAlignment.Top;
            indicators.Margin = new Thickness(8, 1, 0, 0);
            Grid.SetColumn(indicators, 1);
            titleRow.Children.Add(indicators);
        }

        // Title band: appointment fill + contrast text, with the accent color as the left bar. Both
        // layers share the SAME corner radius as the tile, so the color box's rounded corners match the
        // event block (the outer bar-colored layer shows a 4px strip on the left = the bar).
        var titleCorner = new CornerRadius(ctx.CornerRadius);
        panel.Children.Add(new Border
        {
            Background = ctx.BarBrush,
            CornerRadius = titleCorner,
            Padding = new Thickness(4, 0, 0, 0),
            Child = new Border
            {
                Background = ctx.BackBrush,
                CornerRadius = titleCorner,
                Padding = new Thickness(8, 4, 8, 4),
                Child = titleRow,
            },
        });

        // Date.
        panel.Children.Add(new TextBlock
        {
            Text = ctx.Start.ToString("dddd, MMM d, yyyy"),
            FontSize = 12,
            Opacity = 0.8,
        });

        // Full time range + duration (e.g. "9:00 AM - 12:30 PM  (3h 30m)").
        var durationText = ctx.IsAllDay ? null : FormatDuration(ctx.Appointment.Duration);
        panel.Children.Add(new TextBlock
        {
            Text = durationText is null ? ctx.TimeRange : $"{ctx.TimeRange}  ({durationText})",
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
        });

        // Participant roles (host-supplied), each "Label: value".
        var details = ctx.Details.Where(d => !string.IsNullOrWhiteSpace(d.Value)).ToList();
        if (details.Count > 0)
        {
            panel.Children.Add(Divider(ctx));
            foreach (var d in details)
            {
                panel.Children.Add(DetailRow(ctx, d.Label, d.Value));
            }
        }

        if (!string.IsNullOrWhiteSpace(ctx.Note))
        {
            panel.Children.Add(Divider(ctx));
            panel.Children.Add(new TextBlock { Text = ctx.Note, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }

        if (!string.IsNullOrWhiteSpace(ctx.Memo))
        {
            panel.Children.Add(Divider(ctx));
            panel.Children.Add(new TextBlock
            {
                Text = "Internal memo",
                FontSize = 10,
                Opacity = 0.6,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            panel.Children.Add(new TextBlock
            {
                Text = ctx.Memo,
                FontSize = 12,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // The data tiles and adornments also appear in the popup (so the hover card carries the same badges
        // the block shows in its gutters).
        AppendDecorations(panel, ctx, "Tiles", ctx.Tiles);
        AppendDecorations(panel, ctx, "Adornments", ctx.Adornments);

        return panel;
    }

    // A labeled row of decoration chips (tiles / adornments) for the popup; no-op when the list is empty.
    private static void AppendDecorations(StackPanel panel, AppointmentContext ctx, string header, IList<AppointmentDecoration> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        panel.Children.Add(Divider(ctx));
        panel.Children.Add(new TextBlock
        {
            Text = header,
            FontSize = 10,
            Opacity = 0.6,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var d in items)
        {
            if (TooltipDecorationChip(d, ctx) is { } chip)
            {
                row.Children.Add(chip);
            }
        }
        if (row.Children.Count > 0)
        {
            panel.Children.Add(row);
        }
    }

    // One decoration rendered for the popup: image, glyph, or a colored text pill (shares the tile pill look).
    private static FrameworkElement? TooltipDecorationChip(AppointmentDecoration d, AppointmentContext ctx)
    {
        if (d.Image is { Length: > 0 } bytes)
        {
            return TileImage(bytes);
        }
        FrameworkElement inner;
        if (!string.IsNullOrEmpty(d.Glyph))
        {
            var icon = new FontIcon
            {
                Glyph = d.Glyph,
                FontSize = 12,
                Foreground = d.ForeColor is Color gc ? new SolidColorBrush(gc) : GlyphDark,
            };
            if (d.GlyphFontFamily is not null) icon.FontFamily = d.GlyphFontFamily;
            inner = icon;
        }
        else if (!string.IsNullOrWhiteSpace(d.Text))
        {
            inner = new TextBlock
            {
                Text = d.Text,
                FontSize = 11,
                FontWeight = d.Bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = d.ForeColor is Color tc ? new SolidColorBrush(tc) : GlyphDark,
            };
        }
        else
        {
            return null;
        }

        return new Border
        {
            Child = inner,
            Background = TimePillBackBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 1),
        };
    }

    private static Border Divider(AppointmentContext ctx) =>
        new() { Height = 1, Background = ctx.BorderBrush, Margin = new Thickness(0, 3, 0, 3) };

    // One "Label   value" line for the roles section: a muted, fixed-width caption then the wrapping value.
    private static Grid DetailRow(AppointmentContext ctx, string label, string value)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(label) ? string.Empty : label,
            FontSize = 12,
            Opacity = 0.7,
            MinWidth = 64,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(labelBlock, 0);
        row.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);
        return row;
    }

    // "3h 30m", "45m", "2h" — a friendly span length.
    private static string FormatDuration(TimeSpan d)
    {
        var h = (int)d.TotalHours;
        var m = d.Minutes;
        if (h > 0 && m > 0) return $"{h}h {m}m";
        if (h > 0) return $"{h}h";
        return $"{m}m";
    }

    // ---- Tiles -----------------------------------------------------------------------------------

    // The data-driven tiles (host "tile" feature) on one side of the block (see TileLayout for where each
    // Position lands). One strip per alignment group, overlaid in the same cell and aligned along the strip
    // (Near / Middle / Far). Null when there are none on that side or tiles are hidden ("Show tiles" off).
    private static FrameworkElement? BuildTiles(AppointmentContext ctx, TileSide side, bool multiLine, double defaultSize)
    {
        if (!ctx.ShowTiles || ctx.Tiles.Count == 0)
        {
            return null;
        }

        var vertical = TileLayout.IsVertical(side, multiLine);
        var host = new Grid();
        foreach (var (alignment, tiles) in TileLayout.Groups(ctx.Tiles, side, multiLine))
        {
            var strip = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = 2 };
            if (vertical)
            {
                strip.VerticalAlignment = alignment switch
                {
                    DecorationAlignment.Far => VerticalAlignment.Bottom,
                    DecorationAlignment.Middle => VerticalAlignment.Center,
                    _ => VerticalAlignment.Top,
                };
            }
            else
            {
                strip.HorizontalAlignment = alignment switch
                {
                    DecorationAlignment.Far => HorizontalAlignment.Right,
                    DecorationAlignment.Middle => HorizontalAlignment.Center,
                    _ => HorizontalAlignment.Left,
                };
            }

            foreach (var t in tiles)
            {
                if (Tile(t, ctx, defaultSize) is not { } el)
                {
                    continue;
                }
                // In a vertical gutter, pills hug the block's outer edge.
                if (vertical)
                {
                    el.HorizontalAlignment = side == TileSide.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                }
                if (t.OffsetX != 0 || t.OffsetY != 0)
                {
                    el.RenderTransform = new TranslateTransform { X = t.OffsetX, Y = t.OffsetY };
                }
                if (!string.IsNullOrEmpty(t.Tooltip))
                {
                    ToolTipService.SetToolTip(el, t.Tooltip);
                }
                strip.Children.Add(el);
            }
            if (strip.Children.Count > 0)
            {
                host.Children.Add(strip);
            }
        }
        return host.Children.Count > 0 ? host : null;
    }

    // One tile: its image, or a pill with its glyph and/or text side by side. Null when it has none of them.
    private static FrameworkElement? Tile(AppointmentDecoration t, AppointmentContext ctx, double defaultSize)
    {
        if (t.Image is { Length: > 0 } bytes)
        {
            return TileImage(bytes);
        }
        var hasGlyph = !string.IsNullOrEmpty(t.Glyph);
        var hasText = !string.IsNullOrWhiteSpace(t.Text);
        if (!hasGlyph && !hasText)
        {
            return null;
        }

        var size = t.FontSize > 0 ? t.FontSize : defaultSize;
        var foreground = t.ForeColor is Color c ? new SolidColorBrush(c) : GlyphDark;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        if (hasGlyph)
        {
            var icon = new FontIcon { Glyph = t.Glyph!, FontSize = size, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center };
            if (t.GlyphFontFamily is not null) icon.FontFamily = t.GlyphFontFamily;
            row.Children.Add(icon);
        }
        if (hasText)
        {
            row.Children.Add(new TextBlock
            {
                Text = t.Text,
                FontSize = size,
                FontWeight = t.Bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            });
        }

        // The tile's own fill/border when set; otherwise a neutral pill (the same gray as the time/icon
        // chips) so colored text stays legible on any block fill.
        return new Border
        {
            Child = row,
            Background = t.BackColor is Color bc ? new SolidColorBrush(bc) : TimePillBackBrush,
            BorderBrush = t.BorderColor is Color brc ? new SolidColorBrush(brc) : null,
            BorderThickness = t.BorderColor is null ? new Thickness(0) : new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5, 1, 5, 1),
        };
    }

    private static FrameworkElement TileImage(byte[] bytes)
    {
        var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        _ = LoadTileImageAsync(bmp, bytes);
        return new Image { Source = bmp, Stretch = Stretch.Uniform, MaxHeight = 18, MaxWidth = 18 };
    }

    private static async Task LoadTileImageAsync(Microsoft.UI.Xaml.Media.Imaging.BitmapImage bmp, byte[] bytes)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(bytes);
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
        }
        catch
        {
            // A malformed tile image renders nothing — never break the block.
        }
    }

    // ---- Indicator strip -----------------------------------------------------------------------

    // The horizontal run of status glyphs (standard flags first, then custom indicators), or null
    // when the appointment has none.
    private static StackPanel? BuildIndicators(AppointmentContext ctx, double size, Orientation orientation = Orientation.Horizontal, bool includeLock = true)
    {
        var strip = new StackPanel { Orientation = orientation, Spacing = 3 };

        // Neutral statuses use a fixed dark glyph (so they read on the gray chip on any fill); Complete
        // and Confirmed keep their accent colors. The timed block draws lock as a red bottom strip, so
        // it suppresses the gutter glyph (includeLock:false); chips/bars still show it here.
        if (ctx.IsRecurring) strip.Children.Add(Glyph(RecurrenceGlyph, GlyphDark, size, "Recurring"));
        if (includeLock && ctx.IsLocked) strip.Children.Add(Glyph(LockedGlyph, GlyphDark, size, "Locked"));
        if (ctx.IsComplete) strip.Children.Add(Glyph(CompleteGlyph, CompleteGreen, size, "Complete"));
        if (ctx.IsConfirmed) strip.Children.Add(Glyph(ConfirmedGlyph, ConfirmedBlue, size, "Confirmed"));
        if (ctx.IsCancelled) strip.Children.Add(Glyph(CanceledGlyph, GlyphDark, size, "Canceled"));
        if (ctx.IsTentative) strip.Children.Add(Glyph(TentativeGlyph, GlyphDark, size, "Tentative"));

        foreach (var ind in ctx.Indicators)
        {
            if (string.IsNullOrEmpty(ind.Glyph)) continue;
            var brush = ind.Color is Color c ? new SolidColorBrush(c) : GlyphDark;
            strip.Children.Add(Glyph(ind.Glyph, brush, size, ind.Tooltip, ind.FontFamily));
        }

        return strip.Children.Count > 0 ? strip : null;
    }

    // Gray disc behind each status glyph (the same gray as the time pill). It needn't contrast every
    // fill — if a fill happens to match the gray, the colored glyph still has enough contrast on its
    // own. The glyph color is never gray (accent colors, or a fixed dark for the neutral statuses).
    private static FrameworkElement Glyph(string glyph, Brush brush, double size, string? tooltip = null, FontFamily? font = null)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = size,
            Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (font is not null) icon.FontFamily = font;

        var diameter = size + 8;
        var chip = new Border
        {
            Width = diameter,
            Height = diameter,
            CornerRadius = new CornerRadius(diameter / 2),
            Background = TimePillBackBrush,
            Child = icon,
        };
        if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(chip, tooltip);
        return chip;
    }
}
