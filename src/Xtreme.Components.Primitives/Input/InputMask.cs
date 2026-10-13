using System.Text;

namespace Xtreme.Components.Primitives;

/// <summary>
/// Pure, best-effort input-mask helpers for the tenant <c>ContactType.DisplayFormat</c> grammar:
/// <c>#</c> = one digit, anything else = a literal shown as-is.
///
/// Two output shapes:
///   <see cref="Format"/>          — the placeholder-free value to persist (e.g. <c>(555) 123-4567</c>).
///   <see cref="FormatTemplate"/>  — the full template for display (e.g. <c>(___) ___-____</c>),
///                                   unfilled <c>#</c> positions shown as a placeholder char.
///
/// Everything here is forgiving — it never throws and never rejects. Input is reduced to its
/// digits and re-laid-out against the mask, so raw (<c>5551234567</c>) and already-formatted
/// (<c>(555) 123-4567</c>) input normalize identically.
/// </summary>
public static class InputMask
{
    /// <summary>True when the mask is usable (non-empty and contains at least one <c>#</c>).</summary>
    public static bool HasMask(string? mask) =>
        !string.IsNullOrWhiteSpace(mask) && mask.IndexOf('#') >= 0;

    /// <summary>Number of <c>#</c> placeholders in the mask (i.e. how many digits it accepts).</summary>
    public static int PlaceholderCount(string? mask)
    {
        if (string.IsNullOrEmpty(mask)) return 0;
        var n = 0;
        foreach (var c in mask)
            if (c == '#') n++;
        return n;
    }

    /// <summary>Pull up to <paramref name="max"/> digit characters out of <paramref name="raw"/>, in order.</summary>
    public static string ExtractDigits(string? raw, int max)
    {
        if (string.IsNullOrEmpty(raw) || max <= 0) return string.Empty;
        var sb = new StringBuilder(Math.Min(raw.Length, max));
        foreach (var c in raw)
        {
            if (char.IsDigit(c))
            {
                sb.Append(c);
                if (sb.Length >= max) break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// The value to persist: <paramref name="raw"/>'s digits laid out against <paramref name="mask"/>,
    /// with literals injected only up to the last entered digit (no trailing separators, no placeholders).
    /// Returns the raw value unchanged when the mask is empty or has no <c>#</c>.
    /// </summary>
    public static string Format(string? raw, string? mask)
    {
        raw ??= string.Empty;
        if (!HasMask(mask)) return raw;

        var digits = ExtractDigits(raw, PlaceholderCount(mask));
        if (digits.Length == 0) return string.Empty;

        var sb = new StringBuilder(mask!.Length);
        var d = 0;
        foreach (var m in mask)
        {
            if (m == '#')
            {
                if (d < digits.Length) sb.Append(digits[d++]);
                else break;
            }
            else
            {
                if (d < digits.Length) sb.Append(m);
                else break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// The full template for display: entered digits fill the <c>#</c> slots left-to-right, remaining
    /// <c>#</c> slots show <paramref name="placeholder"/>, and literals are always shown. Returns the
    /// raw value unchanged when the mask is unusable.
    /// </summary>
    public static string FormatTemplate(string? raw, string? mask, char placeholder)
    {
        if (!HasMask(mask)) return raw ?? string.Empty;

        var digits = ExtractDigits(raw, PlaceholderCount(mask));
        var sb = new StringBuilder(mask!.Length);
        var d = 0;
        foreach (var m in mask)
        {
            if (m == '#')
                sb.Append(d < digits.Length ? digits[d++] : placeholder);
            else
                sb.Append(m);
        }
        return sb.ToString();
    }
}
