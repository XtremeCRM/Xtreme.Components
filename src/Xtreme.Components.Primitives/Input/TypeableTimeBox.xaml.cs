using System.Globalization;

namespace Xtreme.Components.Primitives;

/// <summary>
/// Time entry that combines a typeable text field with a 15-minute suggestion list.
/// Mirrors Legacy's behavior: the user can type "4:03pm" or "16:03" freely, but a
/// dropdown of quarter-hour slots is always one click away. Parses on lost-focus and
/// on suggestion-chosen; falls back to the prior value if parsing fails.
/// </summary>
public sealed partial class TypeableTimeBox : UserControl
{
    private bool _suppressTextChanged;

    public TypeableTimeBox()
    {
        this.InitializeComponent();
        PART_Box.ItemsSource = BuildQuarterHourSuggestions();
    }

    public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(
        nameof(Time), typeof(TimeOnly?), typeof(TypeableTimeBox),
        new PropertyMetadata(null, OnTimeChanged));

    public TimeOnly? Time
    {
        get => (TimeOnly?)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public event EventHandler<TimeOnly?>? TimeCommitted;

    private static void OnTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TypeableTimeBox box)
        {
            box._suppressTextChanged = true;
            try
            {
                box.PART_Box.Text = FormatTime(box.Time);
            }
            finally
            {
                box._suppressTextChanged = false;
            }
        }
    }

    private void OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_suppressTextChanged) return;
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        var typed = sender.Text?.Trim() ?? string.Empty;
        if (typed.Length == 0)
        {
            sender.ItemsSource = BuildQuarterHourSuggestions();
            return;
        }

        // Filter quarter-hour suggestions by what the user has typed so far.
        sender.ItemsSource = BuildQuarterHourSuggestions()
            .Where(s => s.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string s && TryParseTime(s, out var parsed))
        {
            Commit(parsed);
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        var typed = PART_Box.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(typed))
        {
            // Treat empty as cleared.
            Commit(null);
            return;
        }

        if (TryParseTime(typed, out var parsed))
        {
            Commit(parsed);
        }
        else
        {
            // Revert to last valid value.
            _suppressTextChanged = true;
            try { PART_Box.Text = FormatTime(Time); }
            finally { _suppressTextChanged = false; }
        }
    }

    private void Commit(TimeOnly? value)
    {
        if (Time != value)
        {
            Time = value;
            TimeCommitted?.Invoke(this, value);
        }
        else
        {
            // Re-format current text in case the user typed a non-canonical form ("4p" -> "4:00 PM").
            _suppressTextChanged = true;
            try { PART_Box.Text = FormatTime(Time); }
            finally { _suppressTextChanged = false; }
        }
    }

    private static string FormatTime(TimeOnly? t) =>
        t.HasValue ? t.Value.ToString("h:mm tt", CultureInfo.CurrentCulture) : string.Empty;

    private static List<string> BuildQuarterHourSuggestions()
    {
        var list = new List<string>(96);
        for (int h = 0; h < 24; h++)
        {
            for (int m = 0; m < 60; m += 15)
            {
                list.Add(new TimeOnly(h, m).ToString("h:mm tt", CultureInfo.CurrentCulture));
            }
        }
        return list;
    }

    /// <summary>
    /// Parse user input. Accepts, case-insensitively: "4", "4p", "4pm", "4 pm", "4:03", "4:03 pm",
    /// "16:03", "930" / "930p" (three digits = h:mm), "1603" / "1130p" (four digits = HH:mm), plus
    /// anything <see cref="TimeOnly.TryParse(string, IFormatProvider, DateTimeStyles, out TimeOnly)"/>
    /// accepts in the current culture. Digits without an AM/PM marker read the way a 24-hour clock
    /// would ("4" and "930" are morning, "1603" is afternoon). See <see cref="TimeInputParser"/>.
    /// </summary>
    public static bool TryParseTime(string input, out TimeOnly result)
        => TimeInputParser.TryParse(input, out result);
}
