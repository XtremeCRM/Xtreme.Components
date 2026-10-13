using System.Text;

namespace Xtreme.Components.Primitives;

/// <summary>
/// Pure, best-effort input-mask helpers. Mask grammar:
/// <list type="bullet">
///   <item><c>#</c> = one digit</item>
///   <item><c>A</c> = one letter</item>
///   <item><c>*</c> = one letter or digit</item>
///   <item><c>\</c> = the next character is a literal (<c>\A</c> shows a literal A, <c>\\</c> a backslash)</item>
///   <item>anything else = a literal shown as-is</item>
/// </list>
/// The tenant <c>ContactType.DisplayFormat</c> masks (<c>(###) ###-####</c>) use only <c>#</c>.
///
/// Two output shapes:
///   <see cref="Format"/>          — the placeholder-free value to persist (e.g. <c>(555) 123-4567</c>).
///   <see cref="FormatTemplate"/>  — the full template for display (e.g. <c>(___) ___-____</c>),
///                                   unfilled slots shown as a placeholder char.
///
/// Everything here is forgiving — it never throws and never rejects. Input is read against the mask
/// position by position (<see cref="ExtractSlots"/>): a character equal to the mask's literal at that
/// point is taken as that literal, a character that fits the next slot fills it, anything else is
/// skipped. So raw (<c>5551234567</c>), already-formatted (<c>(555) 123-4567</c>) and template
/// (<c>(555) 12_-____</c>) input all normalize identically. Letters keep the case they were typed in.
/// </summary>
public static class InputMask
{
    private const char DigitSlot = '#';
    private const char LetterSlot = 'A';
    private const char AnySlot = '*';
    private const char Escape = '\\';

    /// <summary>True when the mask is usable (non-blank and contains at least one slot: <c>#</c>, <c>A</c> or <c>*</c>).</summary>
    public static bool HasMask(string? mask) =>
        !string.IsNullOrWhiteSpace(mask) && PlaceholderCount(mask) > 0;

    /// <summary>Number of slots (<c>#</c>, <c>A</c>, <c>*</c>, not escaped) in the mask, i.e. how many characters it accepts.</summary>
    public static int PlaceholderCount(string? mask)
    {
        if (string.IsNullOrEmpty(mask)) return 0;
        var n = 0;
        foreach (var t in Tokens(mask))
            if (t.IsSlot) n++;
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
    /// The characters of <paramref name="raw"/> that fill the mask's slots, in order (no literals),
    /// e.g. <c>K1A0B1</c> for <c>k1a 0b1</c> against <c>A#A #A#</c> (case is kept). Walks the input and
    /// the mask together: an input character equal to the current literal (letters compared
    /// case-insensitively) consumes it, a character that fits the current slot fills it, anything else
    /// is skipped. A literal the input does not repeat is simply stepped over. Returns <c>""</c> when
    /// the mask is unusable.
    /// </summary>
    public static string ExtractSlots(string? raw, string? mask)
    {
        if (string.IsNullOrEmpty(raw) || !HasMask(mask)) return string.Empty;

        var tokens = Tokens(mask!);
        var sb = new StringBuilder(tokens.Count);
        var m = 0;
        var r = 0;
        while (r < raw.Length && m < tokens.Count)
        {
            var t = tokens[m];
            var c = raw[r];
            if (!t.IsSlot)
            {
                // The input repeats the literal: consume both. Otherwise the literal is implied.
                if (char.ToUpperInvariant(c) == char.ToUpperInvariant(t.Char)) r++;
                m++;
            }
            else if (Fits(t.Char, c))
            {
                sb.Append(c);
                r++;
                m++;
            }
            else
            {
                r++; // a character no slot here can take (placeholder, stray separator, wrong kind)
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// The value to persist: <paramref name="raw"/>'s slot characters laid out against <paramref name="mask"/>,
    /// with literals injected only up to the last entered character (no trailing separators, no placeholders).
    /// Returns the raw value unchanged when the mask has no slot, and <c>""</c> when nothing in
    /// <paramref name="raw"/> fits a slot.
    /// </summary>
    public static string Format(string? raw, string? mask)
    {
        raw ??= string.Empty;
        if (!HasMask(mask)) return raw;

        var slots = ExtractSlots(raw, mask);
        if (slots.Length == 0) return string.Empty;

        var sb = new StringBuilder(mask!.Length);
        var d = 0;
        foreach (var t in Tokens(mask))
        {
            if (d >= slots.Length) break;
            sb.Append(t.IsSlot ? slots[d++] : t.Char);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The full template for display: entered characters fill the slots left-to-right, remaining slots
    /// show <paramref name="placeholder"/>, and literals are always shown (escapes resolved). Returns the
    /// raw value unchanged when the mask is unusable.
    /// </summary>
    public static string FormatTemplate(string? raw, string? mask, char placeholder)
    {
        if (!HasMask(mask)) return raw ?? string.Empty;

        var slots = ExtractSlots(raw, mask);
        var sb = new StringBuilder(mask!.Length);
        var d = 0;
        foreach (var t in Tokens(mask))
        {
            if (t.IsSlot)
                sb.Append(d < slots.Length ? slots[d++] : placeholder);
            else
                sb.Append(t.Char);
        }
        return sb.ToString();
    }

    private static bool Fits(char slot, char c) => slot switch
    {
        DigitSlot => char.IsDigit(c),
        LetterSlot => char.IsLetter(c),
        AnySlot => char.IsLetterOrDigit(c),
        _ => false,
    };

    private readonly record struct Token(char Char, bool IsSlot);

    // The mask with escapes resolved: one token per displayed character.
    private static List<Token> Tokens(string mask)
    {
        var list = new List<Token>(mask.Length);
        for (var i = 0; i < mask.Length; i++)
        {
            var c = mask[i];
            if (c == Escape && i + 1 < mask.Length)
                list.Add(new Token(mask[++i], false));
            else
                list.Add(new Token(c, c is DigitSlot or LetterSlot or AnySlot));
        }
        return list;
    }
}
