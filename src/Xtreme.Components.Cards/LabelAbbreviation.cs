namespace Xtreme.Components.Cards;

/// <summary>
/// One way to shorten a label that does not fit: keep the first N letters and add
/// <see cref="Suffix"/> (".."). At zero letters the result is empty, meaning "show the icon alone".
/// <see cref="SectionCard"/> uses it for <see cref="SectionCardHeaderFit.Abbreviate"/>; hosts can use
/// it for their own buttons and labels so everything shortens the same way.
/// </summary>
public static class LabelAbbreviation
{
    /// <summary>Appended to a shortened label.</summary>
    public const string Suffix = "..";

    /// <summary>
    /// The first <paramref name="maxLetters"/> letters of <paramref name="text"/> followed by
    /// <see cref="Suffix"/>; empty when <paramref name="maxLetters"/> is zero or less. The text comes
    /// back whole when cutting it would not make it shorter (it already fits in
    /// <paramref name="maxLetters"/> plus the suffix), so "Notes" at 3 letters stays "Notes" rather
    /// than becoming "Not..".
    /// </summary>
    public static string Abbreviate(string? text, int maxLetters)
    {
        if (string.IsNullOrEmpty(text) || maxLetters <= 0) return string.Empty;
        if (text.Length <= maxLetters + Suffix.Length) return text;
        return string.Concat(text.AsSpan(0, maxLetters).TrimEnd(), Suffix);
    }
}
