namespace Xtreme.Components.Cards;

/// <summary>One tab as the fit sees it: its full header, whether it has an icon, and its measured chrome.</summary>
/// <param name="Header">The full header text.</param>
/// <param name="HasIcon">A tab with an icon may drop to icon only; one without keeps at least one letter.</param>
/// <param name="TextChrome">Width of the tab around its text (padding, icon, gap): tab width minus text width.</param>
/// <param name="IconOnlyWidth">Width of the tab showing its icon alone.</param>
internal readonly record struct TabHeaderFitEntry(string Header, bool HasIcon, double TextChrome, double IconOnlyWidth);

/// <summary>
/// The XAML-free part of <see cref="SectionCardHeaderFit.Abbreviate"/>: picks one letter count shared
/// by every tab (headers already that short stay whole), the largest at which the strip fits.
/// </summary>
internal static class TabHeaderFitSolver
{
    /// <summary>
    /// The largest letter count at which every tab fits in <paramref name="available"/>, or 0 when
    /// even the shortest form does not fit. A result at or above the longest header means "no cut".
    /// </summary>
    public static int LargestFittingLetters(IReadOnlyList<TabHeaderFitEntry> tabs, Func<string, double> measureText, double available)
    {
        if (tabs.Count == 0) return 0;

        var longest = tabs.Max(t => t.Header.Length);
        for (var letters = longest; letters > 0; letters--)
        {
            if (TotalWidth(tabs, measureText, letters) <= available) return letters;
        }
        return 0;
    }

    /// <summary>The header a tab shows at <paramref name="letters"/>: icon tabs may go empty, others keep one letter.</summary>
    public static string HeaderAt(TabHeaderFitEntry tab, int letters)
        => LabelAbbreviation.Abbreviate(tab.Header, tab.HasIcon ? letters : Math.Max(letters, 1));

    /// <summary>Width of the whole strip with every tab cut to <paramref name="letters"/>.</summary>
    public static double TotalWidth(IReadOnlyList<TabHeaderFitEntry> tabs, Func<string, double> measureText, int letters)
    {
        var total = 0.0;
        foreach (var tab in tabs)
        {
            var text = HeaderAt(tab, letters);
            total += text.Length == 0 ? tab.IconOnlyWidth : tab.TextChrome + measureText(text);
        }
        return total;
    }
}
