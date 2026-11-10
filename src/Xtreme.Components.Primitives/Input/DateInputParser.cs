using System.Globalization;
using System.Text.RegularExpressions;

namespace Xtreme.Components.Primitives;

/// <summary>
/// The free-typed date grammar behind <see cref="TypeableDatePicker"/>. Pure (no UI types), so it is
/// unit-tested on its own.
///
/// Field order (month/day/year, day/month/year or year/month/day) comes from the culture's short date
/// pattern, so en-US reads <c>10/15/26</c> as October 15 and en-CA reads <c>2026-10-15</c>. Accepted:
/// <list type="bullet">
///   <item><c>t</c> or <c>today</c>.</item>
///   <item>Two or three number groups split by <c>/ - .</c>, spaces or commas: <c>10/15/2026</c>,
///         <c>10-15-26</c>, <c>10.15</c> (no year = this year).</item>
///   <item>Digits alone: 4 = month+day this year (<c>1015</c>), 6 = with a 2-digit year (<c>101526</c>),
///         8 = with a 4-digit year (<c>10152026</c>).</item>
///   <item>Anything the culture's own date parser takes, such as <c>Oct 15 2026</c>.</item>
/// </list>
/// A 1–2 digit year goes through the culture calendar's <see cref="Calendar.TwoDigitYearMax"/>
/// window (2049 by default), so <c>26</c> is 2026 and <c>85</c> is 1985.
/// </summary>
internal static partial class DateInputParser
{
    private enum Field { Month, Day, Year }

    [GeneratedRegex(@"^\d+(?:[\s/\-.,]+\d+){1,2}$")]
    private static partial Regex NumberGroups();

    [GeneratedRegex(@"[\s/\-.,]+")]
    private static partial Regex Separators();

    public static bool TryParse(string? input, DateOnly today, CultureInfo culture, out DateOnly result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim();
        if (s.Equals("t", StringComparison.OrdinalIgnoreCase) || s.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            result = today;
            return true;
        }

        var order = FieldOrder(culture);

        if (s.All(char.IsAsciiDigit))
            return TryDigits(s, order, today, culture, out result);

        if (NumberGroups().IsMatch(s))
            return TryGroups(Separators().Split(s), order, today, culture, out result);

        // Month names and other culture forms. Numeric-only input never gets here, so the culture parser
        // can't read "10/15/26" in a different order than the rules above.
        if (DateTime.TryParse(s, culture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out var parsed)
            && parsed.Year > 1)
        {
            result = DateOnly.FromDateTime(parsed);
            return true;
        }

        return false;
    }

    /// <summary>The culture's short date pattern as an editing hint: "M/d/yyyy" becomes "mm/dd/yyyy".</summary>
    public static string Hint(CultureInfo culture)
    {
        var sep = culture.DateTimeFormat.DateSeparator;
        return string.Join(sep, FieldOrder(culture).Select(f => f switch
        {
            Field.Month => "mm",
            Field.Day => "dd",
            _ => "yyyy",
        }));
    }

    /// <summary>Formats a date the way the culture's short date pattern writes it.</summary>
    public static string Format(DateOnly date, CultureInfo culture)
        => date.ToString(culture.DateTimeFormat.ShortDatePattern, culture);

    private static Field[] FieldOrder(CultureInfo culture)
    {
        var p = culture.DateTimeFormat.ShortDatePattern;
        var positions = new[]
        {
            (Field.Month, p.IndexOf('M')),
            (Field.Day, p.IndexOf('d')),
            (Field.Year, p.IndexOf('y')),
        };
        if (positions.Any(x => x.Item2 < 0)) return new[] { Field.Month, Field.Day, Field.Year };
        return positions.OrderBy(x => x.Item2).Select(x => x.Item1).ToArray();
    }

    private static bool TryDigits(string digits, Field[] order, DateOnly today, CultureInfo culture, out DateOnly result)
    {
        result = default;
        switch (digits.Length)
        {
            case 4:
            {
                // Month and day only, in the culture's order; the year is this year.
                var md = order.Where(f => f != Field.Year).ToArray();
                var parts = new[] { digits[..2], digits[2..] };
                return TryBuild(md, parts, today, culture, out result);
            }
            case 6:
            case 8:
            {
                var yearLen = digits.Length == 6 ? 2 : 4;
                var parts = new string[3];
                var at = 0;
                for (var i = 0; i < 3; i++)
                {
                    var len = order[i] == Field.Year ? yearLen : 2;
                    parts[i] = digits.Substring(at, len);
                    at += len;
                }
                return TryBuild(order, parts, today, culture, out result);
            }
            default:
                return false;
        }
    }

    private static bool TryGroups(string[] parts, Field[] order, DateOnly today, CultureInfo culture, out DateOnly result)
    {
        result = default;
        if (parts.Length == 2)
            return TryBuild(order.Where(f => f != Field.Year).ToArray(), parts, today, culture, out result);
        return parts.Length == 3 && TryBuild(order, parts, today, culture, out result);
    }

    private static bool TryBuild(Field[] fields, string[] parts, DateOnly today, CultureInfo culture, out DateOnly result)
    {
        result = default;
        int month = 0, day = 0, year = today.Year;
        for (var i = 0; i < fields.Length; i++)
        {
            var text = parts[i];
            if (text.Length == 0 || text.Length > 4) return false;
            var n = int.Parse(text, CultureInfo.InvariantCulture);
            switch (fields[i])
            {
                case Field.Month:
                    if (text.Length > 2) return false;
                    month = n;
                    break;
                case Field.Day:
                    if (text.Length > 2) return false;
                    day = n;
                    break;
                case Field.Year:
                    if (text.Length == 3) return false;
                    year = text.Length <= 2 ? culture.Calendar.ToFourDigitYear(n) : n;
                    break;
            }
        }

        if (year is < 1 or > 9999 || month is < 1 or > 12) return false;
        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        result = new DateOnly(year, month, day);
        return true;
    }
}
