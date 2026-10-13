namespace Xtreme.Components.Primitives;

/// <summary>
/// A hand-rolled masked TextBox for Uno (the Community Toolkit <c>TextBoxExtensions.Mask</c> throws
/// on every keystroke under Uno — see the data-entry plan). Shows the full template up front
/// (e.g. <c>(___) ___-____</c>) and fills the placeholders as the user types. Mask grammar:
/// <c>#</c> = digit, anything else = a literal shown as-is (see <see cref="InputMask"/>).
///
/// Invalid keystrokes (a non-digit, or a digit once every slot is full) are cancelled in
/// <see cref="OnBeforeTextChanging"/> so they never reach the screen — no flash. Deletions are left
/// to the native TextBox and reconciled in <see cref="OnTextChanged"/>, which drops the last digit
/// when a literal/placeholder was the thing deleted (so backspace never feels stuck and never
/// removes two characters). The bound <see cref="Text"/> holds the placeholder-free value to persist;
/// the underscores are display-only. An empty/null <see cref="Mask"/> = plain passthrough TextBox.
/// </summary>
public sealed partial class MaskedTextBox : UserControl
{
    private const char Placeholder = '_';

    private bool _suppress;
    private string _prevText = string.Empty;

    public MaskedTextBox()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MaskedTextBox),
        new PropertyMetadata(string.Empty, OnTextProperty));

    /// <summary>The placeholder-free value. Two-way bindable.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty MaskProperty = DependencyProperty.Register(
        nameof(Mask), typeof(string), typeof(MaskedTextBox),
        new PropertyMetadata(null, OnMaskChanged));

    /// <summary>The mask pattern (e.g. <c>(###) ###-####</c>). Empty/null = plain text box.</summary>
    public string? Mask
    {
        get => (string?)GetValue(MaskProperty);
        set => SetValue(MaskProperty, value);
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(object), typeof(MaskedTextBox),
        new PropertyMetadata(null));

    /// <summary>Header text/content forwarded to the inner TextBox.</summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(MaskedTextBox),
        new PropertyMetadata(string.Empty));

    /// <summary>Placeholder text forwarded to the inner TextBox (shown only when there is no mask).</summary>
    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    private bool HasMask => InputMask.HasMask(Mask);

    private static void OnTextProperty(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MaskedTextBox box || box._suppress) return;
        box.SyncFromValue();
    }

    private static void OnMaskChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MaskedTextBox box)
            box.SyncFromValue();
    }

    /// <summary>Render the inner display from the current <see cref="Text"/> value and keep Text canonical.</summary>
    private void SyncFromValue()
    {
        if (!HasMask)
        {
            SetInnerText(Text ?? string.Empty, caretToFirstSlot: false);
            return;
        }

        var digits = InputMask.ExtractDigits(Text, InputMask.PlaceholderCount(Mask));
        RenderTemplate(digits);

        var canonical = InputMask.Format(digits, Mask);
        if (canonical != Text)
            Text = canonical; // idempotent re-entry; settles on the next pass
    }

    /// <summary>
    /// Cancel keystrokes that can't be placed BEFORE they render (no flash): a non-digit insertion,
    /// or a digit once every slot is full. Deletions and valid digits pass through to the TextBox.
    /// </summary>
    private void OnBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        if (_suppress || !HasMask) return;

        var current = sender.Text ?? string.Empty;
        var proposed = args.NewText ?? string.Empty;

        if (proposed.Length <= current.Length) return; // deletion handled in OnTextChanged

        var cap = InputMask.PlaceholderCount(Mask);
        var curDigits = InputMask.ExtractDigits(current, int.MaxValue);
        var newDigits = InputMask.ExtractDigits(proposed, int.MaxValue);

        if (newDigits.Length <= curDigits.Length || newDigits.Length > cap)
            args.Cancel = true;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;

        if (!HasMask)
        {
            Text = PART_Box.Text;
            _prevText = PART_Box.Text;
            return;
        }

        var cap = InputMask.PlaceholderCount(Mask);
        var newText = PART_Box.Text ?? string.Empty;
        var newDigits = InputMask.ExtractDigits(newText, cap);

        // A deletion that removed a literal/placeholder leaves the digit count unchanged — that would
        // feel "stuck", so drop the last digit instead. (A deletion that removed an actual digit
        // already reduced the count, so leave it — no double delete.)
        if (newText.Length < _prevText.Length)
        {
            var prevDigits = InputMask.ExtractDigits(_prevText, cap);
            if (newDigits.Length == prevDigits.Length && newDigits.Length > 0)
                newDigits = newDigits.Substring(0, newDigits.Length - 1);
        }

        RenderTemplate(newDigits);
        Text = InputMask.Format(newDigits, Mask);
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (!HasMask) return;

        var digits = InputMask.ExtractDigits(PART_Box.Text, InputMask.PlaceholderCount(Mask));
        RenderTemplate(digits);
        Text = InputMask.Format(digits, Mask);
    }

    /// <summary>Render the full template for the given digits, caret at the first empty slot.</summary>
    private void RenderTemplate(string digits)
    {
        var display = InputMask.FormatTemplate(digits, Mask, Placeholder);
        SetInnerText(display, caretToFirstSlot: true);
    }

    /// <summary>Set the inner TextBox text without re-triggering the masking pass; position the caret.</summary>
    private void SetInnerText(string value, bool caretToFirstSlot)
    {
        _suppress = true;
        try
        {
            if (PART_Box.Text != value)
                PART_Box.Text = value;

            var caret = caretToFirstSlot ? value.IndexOf(Placeholder) : -1;
            if (caret < 0) caret = value.Length;

            PART_Box.SelectionStart = caret;
            PART_Box.SelectionLength = 0;
        }
        finally
        {
            _prevText = value;
            _suppress = false;
        }
    }
}
