using System.Globalization;

namespace Xtreme.Components.Primitives.Tests;

public class TimeInputParserTests
{
    public TimeInputParserTests()
    {
        // The parser falls back to the current culture; pin it so the cases mean the same everywhere.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
    }

    [Theory]
    [InlineData("4", 4, 0)]
    [InlineData("4p", 16, 0)]
    [InlineData("4pm", 16, 0)]
    [InlineData("4 PM", 16, 0)]
    [InlineData("4:03 pm", 16, 3)]
    [InlineData("4:3p", 16, 3)]
    [InlineData("9:30p", 21, 30)]
    [InlineData("9:30 a.m.", 9, 30)]
    [InlineData("11A", 11, 0)]
    [InlineData("12a", 0, 0)]
    [InlineData("12p", 12, 0)]
    [InlineData("12", 12, 0)]
    [InlineData("16", 16, 0)]
    [InlineData("16:03", 16, 3)]
    [InlineData("1603", 16, 3)]
    [InlineData("0930", 9, 30)]
    [InlineData("  1603  ", 16, 3)]
    public void Parses_the_existing_shorthand(string input, int hour, int minute)
        => AssertParses(input, hour, minute);

    [Theory]
    [InlineData("930", 9, 30)]   // three digits = h:mm, read like a 24-hour clock (morning)
    [InlineData("130", 1, 30)]
    [InlineData("000", 0, 0)]
    [InlineData("930p", 21, 30)] // a marker decides the half of the day
    [InlineData("130 pm", 13, 30)]
    [InlineData("130a", 1, 30)]
    [InlineData("1130p", 23, 30)]
    public void Parses_three_digit_input(string input, int hour, int minute)
        => AssertParses(input, hour, minute);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("960")]   // minutes out of range
    [InlineData("2460")]
    [InlineData("25")]
    [InlineData("13p")]   // a 24-hour hour with a marker
    [InlineData("1630p")]
    [InlineData("12345")]
    public void Rejects_input_it_cannot_read(string input)
        => Assert.False(TimeInputParser.TryParse(input, out _));

    [Fact]
    public void Null_is_rejected()
        => Assert.False(TimeInputParser.TryParse(null, out _));

    private static void AssertParses(string input, int hour, int minute)
    {
        Assert.True(TimeInputParser.TryParse(input, out var result), $"'{input}' did not parse");
        Assert.Equal(new TimeOnly(hour, minute), result);
    }
}
