using System.Globalization;

namespace Xtreme.Components.Primitives.Tests;

public class DateInputParserTests
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private static readonly DateOnly Today = new(2026, 11, 10);

    [Theory]
    [InlineData("10/15/2026", 2026, 10, 15)]
    [InlineData("10/15/26", 2026, 10, 15)]
    [InlineData("1/5/26", 2026, 1, 5)]
    [InlineData("01/05/2026", 2026, 1, 5)]
    [InlineData("10-15-2026", 2026, 10, 15)]
    [InlineData("10.15.26", 2026, 10, 15)]
    [InlineData("10 15 2026", 2026, 10, 15)]
    [InlineData("  10/15/2026  ", 2026, 10, 15)]
    [InlineData("2/29/2028", 2028, 2, 29)]
    public void Parses_separated_month_day_year(string input, int y, int m, int d)
        => AssertParses(input, Us, y, m, d);

    [Theory]
    [InlineData("10/15", 2026, 10, 15)] // no year = this year
    [InlineData("1/5", 2026, 1, 5)]
    [InlineData("1015", 2026, 10, 15)]  // four digits = MMdd
    [InlineData("101526", 2026, 10, 15)]
    [InlineData("10152026", 2026, 10, 15)]
    [InlineData("010585", 1985, 1, 5)]
    public void Parses_short_forms(string input, int y, int m, int d)
        => AssertParses(input, Us, y, m, d);

    [Theory]
    [InlineData("26", 2026)]
    [InlineData("49", 2049)]
    [InlineData("50", 1950)]
    [InlineData("85", 1985)]
    [InlineData("00", 2000)]
    public void Two_digit_years_use_the_calendar_window(string yy, int year)
        => AssertParses($"6/1/{yy}", Us, year, 6, 1);

    [Theory]
    [InlineData("t")]
    [InlineData("T")]
    [InlineData("today")]
    public void Today_shorthand(string input) => AssertParses(input, Us, 2026, 11, 10);

    [Theory]
    [InlineData("Oct 15 2026", 2026, 10, 15)]
    [InlineData("October 15, 2026", 2026, 10, 15)]
    public void Falls_back_to_the_culture_parser_for_month_names(string input, int y, int m, int d)
        => AssertParses(input, Us, y, m, d);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("13/1/2026")]   // no 13th month
    [InlineData("2/30/2026")]   // no Feb 30
    [InlineData("2/29/2026")]   // not a leap year
    [InlineData("0/10/2026")]
    [InlineData("10/0/2026")]
    [InlineData("10/15/202")]   // three-digit year
    [InlineData("10/15/2026/1")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("10:30")]       // a time, not a date
    public void Rejects_what_is_not_a_date(string input)
        => Assert.False(DateInputParser.TryParse(input, Today, Us, out _));

    [Fact]
    public void Day_month_cultures_read_day_first()
    {
        var gb = CultureInfo.GetCultureInfo("en-GB");
        AssertParses("15/10/2026", gb, 2026, 10, 15);
        AssertParses("151026", gb, 2026, 10, 15);
        Assert.False(DateInputParser.TryParse("10/15/2026", Today, gb, out _));
    }

    [Fact]
    public void Year_first_cultures_read_year_first()
    {
        var ca = CultureInfo.GetCultureInfo("en-CA");
        AssertParses("2026-10-15", ca, 2026, 10, 15);
        AssertParses("20261015", ca, 2026, 10, 15);
    }

    [Fact]
    public void Hint_and_format_follow_the_culture()
    {
        Assert.Equal("mm/dd/yyyy", DateInputParser.Hint(Us));
        Assert.Equal("10/15/2026", DateInputParser.Format(new DateOnly(2026, 10, 15), Us));
        Assert.Equal("1/5/2026", DateInputParser.Format(new DateOnly(2026, 1, 5), Us));
    }

    [Fact]
    public void Formatted_dates_read_back_as_themselves()
    {
        for (var d = new DateOnly(2026, 1, 1); d.Year == 2026; d = d.AddDays(7))
            AssertParses(DateInputParser.Format(d, Us), Us, d.Year, d.Month, d.Day);
    }

    private static void AssertParses(string input, CultureInfo culture, int y, int m, int d)
    {
        Assert.True(DateInputParser.TryParse(input, Today, culture, out var result), $"'{input}' should parse");
        Assert.Equal(new DateOnly(y, m, d), result);
    }
}
