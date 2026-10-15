using Xtreme.Components.Cards;
using Xunit;

namespace Xtreme.Components.Cards.Tests;

/// <summary>
/// <see cref="LabelAbbreviation"/> and the XAML-free half of SectionCard's header fitting. Text is
/// "measured" at 7 per character, so widths are easy to reason about.
/// </summary>
public sealed class TabHeaderFitTests
{
    private static double Measure(string text) => text.Length * 7;

    private static TabHeaderFitEntry Tab(string header, bool icon = true) => new(header, icon, TextChrome: 40, IconOnlyWidth: 36);

    [Theory]
    [InlineData("Addresses", 20, "Addresses")]
    [InlineData("Addresses", 4, "Addr..")]
    [InlineData("Addresses", 0, "")]
    [InlineData("Notes", 3, "Notes")]      // "Not.." would be no shorter
    [InlineData("Notes", 2, "No..")]
    [InlineData("Payment Accounts", 8, "Payment..")]   // no space before the suffix
    [InlineData(null, 5, "")]
    public void Abbreviate(string? text, int letters, string expected)
        => Assert.Equal(expected, LabelAbbreviation.Abbreviate(text, letters));

    [Fact]
    public void Everything_fits_keeps_full_headers()
    {
        var tabs = new[] { Tab("Details"), Tab("Notes") };
        var letters = TabHeaderFitSolver.LargestFittingLetters(tabs, Measure, 1000);
        Assert.All(tabs, t => Assert.Equal(t.Header, TabHeaderFitSolver.HeaderAt(t, letters)));
    }

    [Fact]
    public void Picks_the_largest_count_that_fits()
    {
        var tabs = new[] { Tab("Contact Details"), Tab("Addresses"), Tab("Notes") };
        for (var width = 120.0; width < 400; width += 5)
        {
            var letters = TabHeaderFitSolver.LargestFittingLetters(tabs, Measure, width);
            if (letters == 0) continue;
            Assert.True(TabHeaderFitSolver.TotalWidth(tabs, Measure, letters) <= width, $"{letters} letters overflow {width}");
            if (letters < 15)
                Assert.True(TabHeaderFitSolver.TotalWidth(tabs, Measure, letters + 1) > width, $"{letters + 1} letters would also fit {width}");
        }
    }

    [Fact]
    public void Short_headers_stay_whole_while_long_ones_shorten()
    {
        var tabs = new[] { Tab("Contact Details"), Tab("Notes") };
        var letters = TabHeaderFitSolver.LargestFittingLetters(tabs, Measure, 40 + 40 + 7 * 13);
        Assert.Equal("Notes", TabHeaderFitSolver.HeaderAt(tabs[1], letters));
        Assert.EndsWith(LabelAbbreviation.Suffix, TabHeaderFitSolver.HeaderAt(tabs[0], letters));
    }

    [Fact]
    public void Too_narrow_goes_icon_only_but_a_tab_without_an_icon_keeps_a_letter()
    {
        var tabs = new[] { Tab("Details"), Tab("Notes", icon: false) };
        var letters = TabHeaderFitSolver.LargestFittingLetters(tabs, Measure, 10);
        Assert.Equal(0, letters);
        Assert.Equal(string.Empty, TabHeaderFitSolver.HeaderAt(tabs[0], letters));
        Assert.Equal("N..", TabHeaderFitSolver.HeaderAt(tabs[1], letters));
    }

    [Fact]
    public void Width_never_grows_as_letters_drop()
    {
        var tabs = new[] { Tab("Payment Accounts"), Tab("Custom Fields"), Tab("Notes") };
        var previous = double.MaxValue;
        for (var letters = 20; letters >= 0; letters--)
        {
            var width = TabHeaderFitSolver.TotalWidth(tabs, Measure, letters);
            Assert.True(width <= previous, $"width grew at {letters} letters");
            previous = width;
        }
    }
}
