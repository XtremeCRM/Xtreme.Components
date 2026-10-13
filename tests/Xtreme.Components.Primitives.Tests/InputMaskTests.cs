namespace Xtreme.Components.Primitives.Tests;

public class InputMaskTests
{
    private const string Phone = "(###) ###-####";
    private const string PostalCode = "A#A #A#";

    [Theory]
    [InlineData("5551234567", "(555) 123-4567")]
    [InlineData("(555) 123-4567", "(555) 123-4567")]
    [InlineData("555-123-4567", "(555) 123-4567")]
    [InlineData("55512", "(555) 12")]
    [InlineData("555", "(555")]
    [InlineData("(555) 12_-____", "(555) 12")] // the control's own display text
    [InlineData("555123456789", "(555) 123-4567")] // extra input is dropped
    [InlineData("no digits", "")]
    public void Format_lays_digits_into_a_phone_mask(string raw, string expected)
        => Assert.Equal(expected, InputMask.Format(raw, Phone));

    [Fact]
    public void FormatTemplate_fills_slots_and_shows_placeholders()
        => Assert.Equal("(555) 12_-____", InputMask.FormatTemplate("55512", Phone, '_'));

    [Theory]
    [InlineData("k1a0b1", "k1a 0b1")]
    [InlineData("K1A 0B1", "K1A 0B1")]
    [InlineData("K1A", "K1A")]
    [InlineData("1K1A", "K1A")] // a digit where a letter is due is skipped
    public void Letter_slots_accept_letters_only(string raw, string expected)
        => Assert.Equal(expected, InputMask.Format(raw, PostalCode));

    [Fact]
    public void ExtractSlots_returns_slot_characters_without_literals()
        => Assert.Equal("K1A0B1", InputMask.ExtractSlots("K1A 0B1", PostalCode));

    [Fact]
    public void Postal_template_shows_every_slot()
        => Assert.Equal("K1A ___", InputMask.FormatTemplate("K1A", PostalCode, '_'));

    [Theory]
    [InlineData("ab12", "ab-12")]
    [InlineData("a-b-1-2", "ab-12")]
    public void Star_slot_takes_letters_or_digits(string raw, string expected)
        => Assert.Equal(expected, InputMask.Format(raw, "**-**"));

    [Fact]
    public void Backslash_escapes_a_slot_character_into_a_literal()
    {
        const string mask = @"\A-###";
        Assert.Equal(3, InputMask.PlaceholderCount(mask));
        Assert.Equal("A-___", InputMask.FormatTemplate("", mask, '_'));
        Assert.Equal("A-123", InputMask.Format("123", mask));
        Assert.Equal("A-123", InputMask.Format("A-123", mask));
    }

    [Fact]
    public void Escaped_backslash_is_a_literal_backslash()
        => Assert.Equal(@"12\34", InputMask.Format("1234", @"##\\##"));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("(---)", false)]
    [InlineData(@"\#\A\*", false)]
    [InlineData("#", true)]
    [InlineData("A", true)]
    [InlineData("*", true)]
    public void HasMask_needs_an_unescaped_slot(string? mask, bool expected)
        => Assert.Equal(expected, InputMask.HasMask(mask));

    [Fact]
    public void PlaceholderCount_counts_every_slot_kind()
        => Assert.Equal(6, InputMask.PlaceholderCount(PostalCode));

    [Fact]
    public void Unusable_mask_returns_raw_unchanged()
    {
        Assert.Equal("anything", InputMask.Format("anything", null));
        Assert.Equal("anything", InputMask.Format("anything", "---"));
        Assert.Equal("anything", InputMask.FormatTemplate("anything", "", '_'));
    }

    [Theory]
    [InlineData("+1 (###) ###-####", "5551234567")]
    [InlineData("ID A###", "K123")]
    [InlineData(PostalCode, "k1a0b1")]
    [InlineData(Phone, "5551234567")]
    public void Formatting_a_formatted_value_is_stable(string mask, string raw)
    {
        var once = InputMask.Format(raw, mask);
        Assert.Equal(once, InputMask.Format(once, mask));
        Assert.Equal(once, InputMask.Format(InputMask.FormatTemplate(raw, mask, '_'), mask));
    }

    [Theory]
    [InlineData("15551234567", "+1 (555) 123-4567")]
    [InlineData("1234567890", "+1 (234) 567-890")] // documented: a leading 1 reads as the +1 literal
    public void Leading_literal_characters_read_as_formatted_input(string raw, string expected)
        => Assert.Equal(expected, InputMask.Format(raw, "+1 (###) ###-####"));

    [Fact]
    public void ExtractDigits_is_unchanged()
        => Assert.Equal("5551", InputMask.ExtractDigits("(555) 1", 4));
}
