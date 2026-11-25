namespace Xtreme.Components.Scheduler.Controls;

public sealed partial class SchedulerToolbar
{
    /// <summary>
    /// <c>--selftest</c> checks for the date-range picker (driven by the Gallery): it opens with a saved
    /// size larger than the window and must still fit the window and show months; a resize drag must
    /// keep the panel's top-left corner where it is. The saved size is put back afterwards.
    /// </summary>
    internal async Task<List<string>> PickerSelfTestAsync(Action<string> log)
    {
        var problems = new List<string>();
        void Check(bool ok, string what)
        {
            if (!ok) problems.Add(what);
        }

        var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
        values.TryGetValue(PickerWidthKey, out var savedW);
        values.TryGetValue(PickerHeightKey, out var savedH);
        var root = XamlRoot!.Content!;
        var window = XamlRoot.Size;

        Rect Bounds() => PickerContainer.TransformToVisual(root).TransformBounds(
            new Rect(0, 0, PickerContainer.ActualWidth, PickerContainer.ActualHeight));

        try
        {
            // An oversized size saved in a bigger window (another silo, a larger monitor).
            values[PickerWidthKey] = 5000.0;
            values[PickerHeightKey] = 5000.0;
            log($"picker: button at {PeriodButton.TransformToVisual(null).TransformPoint(default)} {PeriodButton.ActualWidth:0}x{PeriodButton.ActualHeight:0}");
            OpenPicker();
            await Task.Delay(700);

            var open = Bounds();
            log($"picker: window {window.Width:0}x{window.Height:0}, opened at {Describe(open)}, months {RangeCalendar.VisibleMonthCount}");
            Check(open.Left >= 0 && open.Top >= 0 && open.Right <= window.Width + 1 && open.Bottom <= window.Height + 1,
                $"opened outside the window ({Describe(open)} in {window.Width:0}x{window.Height:0})");
            Check(RangeCalendar.VisibleMonthCount > 0, "opened with no months showing");

            // Shrink then grow from the grip; the top-left must not move.
            OnResizeStarted(null, EventArgs.Empty);
            foreach (var (dx, dy) in new[] { (-60.0, -60.0), (-160.0, -120.0), (-3000.0, -3000.0), (-40.0, -20.0), (3000.0, 3000.0) })
            {
                OnResizeDrag(null, new Point(dx, dy));
                await Task.Delay(150);
                var b = Bounds();
                log($"picker: drag ({dx:0},{dy:0}) -> {Describe(b)}, months {RangeCalendar.VisibleMonthCount}");
                Check(Math.Abs(b.Left - open.Left) < 1 && Math.Abs(b.Top - open.Top) < 1,
                    $"drag ({dx:0},{dy:0}) moved the origin from ({open.Left:0},{open.Top:0}) to ({b.Left:0},{b.Top:0})");
                Check(RangeCalendar.VisibleMonthCount > 0, $"drag ({dx:0},{dy:0}) left no months showing");
                Check(b.Right <= window.Width + 1 && b.Bottom <= window.Height + 1, $"drag ({dx:0},{dy:0}) grew past the window ({Describe(b)})");
            }
            OnResizeCompleted(null, EventArgs.Empty);
        }
        finally
        {
            PeriodFlyout.Hide();
            if (savedW is null) values.Remove(PickerWidthKey); else values[PickerWidthKey] = savedW;
            if (savedH is null) values.Remove(PickerHeightKey); else values[PickerHeightKey] = savedH;
        }
        return problems;
    }

    /// <summary>
    /// <c>--selftest</c> check for a toolbar that has been moved into another window after the picker
    /// already opened in the first one (XU-1360): the picker must open, in this toolbar's window.
    /// </summary>
    internal async Task<List<string>> PickerOpensInOwnWindowSelfTestAsync(Action<string> log)
    {
        var problems = new List<string>();
        try
        {
            OpenPicker();
            await Task.Delay(700);
            var same = ReferenceEquals(PickerContainer.XamlRoot, PeriodButton.XamlRoot);
            log($"picker (second window): open {PeriodFlyout.IsOpen}, same window {same}, panel {PickerContainer.ActualWidth:0}x{PickerContainer.ActualHeight:0}");
            if (!PeriodFlyout.IsOpen) problems.Add("picker did not open");
            if (!same) problems.Add("picker opened in a different window from its button");
            if (PickerContainer.ActualWidth <= 0 || PickerContainer.ActualHeight <= 0) problems.Add("picker panel has no size");
        }
        finally
        {
            PeriodFlyout.Hide();
        }
        return problems;
    }

    /// <summary><c>--selftest</c>: open each visible toolbar menu (Adornments, Tiles) and check it opened. In a
    /// second window, opening threw "XamlRoot is ambiguous" before XU-1360.</summary>
    internal async Task<List<string>> MenusOpenSelfTestAsync(Action<string> log, string where)
    {
        var problems = new List<string>();
        foreach (var (menu, button, label) in new (MenuFlyout Menu, FrameworkElement Button, string Label)[]
                 { (AdornmentMenu, AdornmentDropdown, "adornments"), (TileOptionsMenu, TilesButton, "tiles") })
        {
            if (button.Visibility != Visibility.Visible || menu.Items.Count == 0)
            {
                log($"menu {label} ({where}): not shown, skipped");
                continue;
            }
            try
            {

                var events = new List<string>();
                void Opened(object? s, object e) => events.Add("opened");
                void Closed(object? s, object e) => events.Add("closed");
                await Task.Delay(300); // let the previous flyout finish closing, or this one closes at once
                menu.Opened += Opened;
                menu.Closed += Closed;
                menu.ShowAt(button);
                await Task.Delay(400);
                menu.Opened -= Opened;
                menu.Closed -= Closed;
                log($"menu {label} ({where}): open {menu.IsOpen}, events [{string.Join(",", events)}], same window {ReferenceEquals(menu.XamlRoot, button.XamlRoot)}");
                if (!menu.IsOpen) problems.Add($"{label} menu did not open ({where})");
            }
            catch (Exception ex)
            {
                problems.Add($"{label} menu threw ({where}): {ex.Message}");
            }
            finally
            {
                menu.Hide();
            }
        }
        return problems;
    }

    private static string Describe(Rect r) => $"({r.Left:0},{r.Top:0}) {r.Width:0}x{r.Height:0}";
}
