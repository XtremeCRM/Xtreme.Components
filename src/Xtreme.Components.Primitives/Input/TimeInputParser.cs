using System.Globalization;
using System.Text.RegularExpressions;

namespace Xtreme.Components.Primitives;

/// <summary>
/// The free-typed time grammar behind <see cref="TypeableTimeBox.TryParseTime"/>. Pure (no UI types),
/// so it is unit-tested on its own.
///
/// Order: digits alone (below), the exact <c>h:mm tt</c>-family formats, the current culture's
/// <see cref="TimeOnly.TryParse(string, IFormatProvider, DateTimeStyles, out TimeOnly)"/>, then the
/// a/p-marker shorthand:
/// <list type="bullet">
///   <item>an a/p marker after an hour, <c>h:mm</c> or 3–4 digits: <c>4p</c>, <c>9:30p</c>, <c>930p</c>, <c>1130 am</c></item>
///   <item>digits alone read as a 24-hour clock: <c>4</c> = 4:00, <c>16</c> = 16:00, <c>930</c> = 9:30,
///         <c>1603</c> = 16:03. So a bare <c>130</c> is 1:30 AM; type <c>130p</c> or <c>1330</c> for the afternoon.</item>
/// </list>
/// </summary>
internal static partial class TimeInputParser
{
    // "%h", not "h": a one-letter format string is read as a STANDARD format specifier, and "h" is not
    // one, so TryParseExact threw FormatException for any input that got that far ("25", "4p").
    private static readonly string[] ExactFormats =
    {
        "h:mm tt", "h:mmtt", "h tt", "htt",
        "H:mm", "HH:mm",
        "h:mm", "%h",
    };

    // An hour (1-2 digits) with optional ":m"/":mm" or bare "mm", then an a/p marker ("4p", "9:30PM", "930 a").
    [GeneratedRegex(@"^(\d{1,2})(?::(\d{1,2})|(\d{2}))?\s*([AP])\.?M?\.?$")]
    private static partial Regex MarkerPattern();

    public static bool TryParse(string? input, out TimeOnly result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim().ToUpperInvariant();

        // Digit-only shorthand is decided first, so "12" is noon (not the 12-hour "%h" midnight) and the
        // culture parser cannot read "930" some other way.
        if (s.Length is >= 1 and <= 4 && s.All(char.IsAsciiDigit))
            return TryDigits(s, out result);

        if (TimeOnly.TryParseExact(s, ExactFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;

        if (TimeOnly.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out result))
            return true;

        var match = MarkerPattern().Match(s);
        if (match.Success)
        {
            var hour = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minuteText = match.Groups[2].Success ? match.Groups[2].Value
                           : match.Groups[3].Success ? match.Groups[3].Value
                           : null;
            var minute = minuteText is null ? 0 : int.Parse(minuteText, CultureInfo.InvariantCulture);
            var pm = match.Groups[4].Value == "P";
            if (hour is >= 1 and <= 12 && minute is >= 0 and < 60)
            {
                if (hour == 12) hour = pm ? 12 : 0;
                else if (pm) hour += 12;
                result = new TimeOnly(hour, minute);
                return true;
            }
        }

        result = default;
        return false;
    }

    // 1-2 digits = an hour (0-23); 3 digits = h + mm; 4 digits = HH + mm.
    private static bool TryDigits(string digits, out TimeOnly result)
    {
        result = default;
        var n = int.Parse(digits, CultureInfo.InvariantCulture);
        var (hour, minute) = digits.Length <= 2 ? (n, 0) : (n / 100, n % 100);
        if (hour is < 0 or > 23 || minute is < 0 or > 59) return false;
        result = new TimeOnly(hour, minute);
        return true;
    }
}
