using System.Globalization;
using Microsoft.UI;
using Windows.System;

namespace Xtreme.Components.Primitives;

/// <summary>
/// A <see cref="CalendarDatePicker"/> you can type into. The date label becomes a text box: click it
/// and type <c>10/15/2026</c>, <c>10/15/26</c>, <c>101526</c> or <c>t</c> for today (grammar in
/// <see cref="DateInputParser"/>). The calendar button still opens the calendar, and so do Alt+Down and
/// F4 from the text box.
/// </summary>
/// <remarks>
/// <para>It IS a <see cref="CalendarDatePicker"/>, so swapping one in is a tag rename: <c>Date</c>,
/// <c>DateChanged</c> (with its real event args), <c>MinDate</c>/<c>MaxDate</c>, <c>Header</c>,
/// <c>PlaceholderText</c> and code-behind typed as <see cref="CalendarDatePicker"/> all keep working.
/// It also keeps <see cref="CompactCalendarDatePicker"/>'s compact calendar fonts.</para>
/// <para>Typed text commits on Enter and when focus leaves the box. An empty box clears the date. Text
/// that isn't a date, or a date outside <c>MinDate</c>/<c>MaxDate</c>, is kept on screen with a red
/// border and a message under the field; the last good <c>Date</c> is left alone and
/// <see cref="HasInvalidEntry"/> turns true until the entry is fixed, cleared or Escape reverts it.</para>
/// <para>Time zone: a typed date is stored as local midnight (<c>new DateTimeOffset(date)</c>, the
/// same as a calendar pick), never UTC midnight, which would show as the previous day west of UTC.
/// A typed date equal to the current one leaves <c>Date</c> untouched, so retyping raises no change.</para>
/// </remarks>
public partial class TypeableDatePicker : CompactCalendarDatePicker
{
    private TextBox? _editor;
    private TextBlock? _dateText;
    private bool _editorFocused;

    // The host's own BorderBrush / Description, put back when the error clears.
    private object? _savedBorderBrush;
    private object? _savedDescription;

    public TypeableDatePicker()
    {
        // Focus belongs to the text box. Left as a tab stop, the picker would take Tab first and
        // Shift+Tab out of the box would land back on it.
        IsTabStop = false;
        RegisterPropertyChangedCallback(DateProperty, (_, _) => OnDateValueChanged());
        RegisterPropertyChangedCallback(PlaceholderTextProperty, (_, _) => UpdatePlaceholder());
    }

    public static readonly DependencyProperty HasInvalidEntryProperty = DependencyProperty.Register(
        nameof(HasInvalidEntry), typeof(bool), typeof(TypeableDatePicker), new PropertyMetadata(false));

    /// <summary>True while the box holds text that isn't an acceptable date. <c>Date</c> still holds the
    /// last good value; a host that saves can check this to stop a save until the entry is fixed.</summary>
    public bool HasInvalidEntry
    {
        get => (bool)GetValue(HasInvalidEntryProperty);
        private set => SetValue(HasInvalidEntryProperty, value);
    }

    /// <summary>The text box inside the picker, for hosts that need to focus it.</summary>
    public TextBox? Editor => _editor;

    /// <summary>Moves focus into the text box. The picker itself is not a tab stop, so
    /// <c>picker.Focus(...)</c> does nothing; call this instead.</summary>
    public bool FocusEditor(FocusState state = FocusState.Programmatic)
        => _editor?.Focus(state) ?? false;

    protected override void OnApplyTemplate()
    {
        // A re-applied template (theme change, hot reload) must not drop an entry still being fixed.
        var pendingInvalidText = HasInvalidEntry ? _editor?.Text : null;
        DetachEditor();
        base.OnApplyTemplate();

        _dateText = GetTemplateChild("DateText") as TextBlock;
        if (_dateText?.Parent is not Panel host) return;

        _editor = BuildEditor(_dateText);
        host.Children.Insert(host.Children.IndexOf(_dateText) + 1, _editor);
        // The template's label stays (the base control keeps writing into it) but out of sight.
        _dateText.Visibility = Visibility.Collapsed;

        _editor.KeyDown += OnEditorKeyDown;
        _editor.GotFocus += OnEditorGotFocus;
        _editor.LostFocus += OnEditorLostFocus;

        UpdatePlaceholder();
        if (pendingInvalidText is not null) _editor.Text = pendingInvalidText;
        else ShowCommittedText();
    }

    private TextBox BuildEditor(TextBlock label)
    {
        var box = new TextBox
        {
            Margin = label.Margin,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            MinHeight = 0,
            MinWidth = 0,
            Background = new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = label.FontSize,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
        };
        // The picker draws the field; the box must not draw a second one inside it.
        var clear = new SolidColorBrush(Colors.Transparent);
        foreach (var key in new[]
                 {
                     "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBackgroundDisabled", "TextControlBorderBrush",
                     "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
                     "TextControlBorderBrushDisabled",
                 })
        {
            box.Resources[key] = clear;
        }
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        box.Resources["TextControlThemePadding"] = new Thickness(0);

        Grid.SetRow(box, Grid.GetRow(label));
        Grid.SetColumn(box, Grid.GetColumn(label));
        Grid.SetRowSpan(box, Grid.GetRowSpan(label));
        Grid.SetColumnSpan(box, Grid.GetColumnSpan(label));
        return box;
    }

    private void DetachEditor()
    {
        if (_editor is null) return;
        _editor.KeyDown -= OnEditorKeyDown;
        _editor.GotFocus -= OnEditorGotFocus;
        _editor.LostFocus -= OnEditorLostFocus;
        (_editor.Parent as Panel)?.Children.Remove(_editor);
        _editor = null;
    }

    // ===== Pointer: the box is for typing, the rest of the field (the calendar button) opens the calendar =====

    private bool _pressedOutsideEditor;

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (IsInEditor(e.OriginalSource))
        {
            _pressedOutsideEditor = false;
            return;
        }
        _pressedOutsideEditor = IsEnabled && !e.GetCurrentPoint(this).Properties.IsRightButtonPressed;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (IsInEditor(e.OriginalSource))
        {
            _pressedOutsideEditor = false;
            return;
        }
        var open = _pressedOutsideEditor;
        _pressedOutsideEditor = false;
        base.OnPointerReleased(e);
        // The base control's own press/release bookkeeping doesn't survive the inserted box (its pressed
        // flag is gone by release), so open the calendar here. A typed-but-uncommitted entry goes in first.
        if (open && !IsCalendarOpen)
        {
            Commit();
            IsCalendarOpen = true;
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        // Space and Enter open the calendar in the base control; inside the box they are typing.
        if (IsInEditor(e.OriginalSource)) return;
        base.OnKeyDown(e);
    }

    private bool IsInEditor(object? source)
    {
        if (_editor is null) return false;
        for (var d = source as DependencyObject; d is not null && !ReferenceEquals(d, this); d = VisualTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, _editor)) return true;
        }
        return false;
    }

    // ===== Editing =====

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                // Not handled: a dialog's default button still sees Enter, after the date is in.
                Commit();
                break;
            case VirtualKey.Escape when HasInvalidEntry || _editor?.Text != CommittedText():
                ClearError();
                ShowCommittedText();
                _editor?.SelectAll();
                e.Handled = true;
                break;
            case VirtualKey.F4:
            case VirtualKey.Down when e.KeyStatus.IsMenuKeyDown:
                Commit();
                IsCalendarOpen = true;
                e.Handled = true;
                break;
        }
    }

    private void OnEditorGotFocus(object sender, RoutedEventArgs e)
    {
        _editorFocused = true;
        // A click places the caret where it lands; tabbing in selects the date so typing replaces it.
        if (_editor is { FocusState: not FocusState.Pointer }) _editor.SelectAll();
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs e)
    {
        _editorFocused = false;
        Commit();
    }

    /// <summary>Applies the box's text: empty clears, a valid date sets, anything else flags the field.</summary>
    public void Commit()
    {
        if (_editor is null) return;
        var text = _editor.Text?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            ClearError();
            Date = null;
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        if (!DateInputParser.TryParse(text, DateOnly.FromDateTime(DateTime.Today), culture, out var date))
        {
            ShowError($"Not a date. Type it like {DateInputParser.Format(DateOnly.FromDateTime(DateTime.Today), culture)}.");
            return;
        }

        var (min, max) = (LocalDate(MinDate), LocalDate(MaxDate));
        if (MinDate != default && date < min || MaxDate != default && date > max)
        {
            ShowError(MinDate != default && MaxDate != default
                ? $"Pick a date from {DateInputParser.Format(min, culture)} to {DateInputParser.Format(max, culture)}."
                : MinDate != default
                    ? $"Pick a date on or after {DateInputParser.Format(min, culture)}."
                    : $"Pick a date on or before {DateInputParser.Format(max, culture)}.");
            return;
        }

        ClearError();
        if (Date is { } current && LocalDate(current) == date)
        {
            ShowCommittedText(); // tidy "101526" into "10/15/2026" without touching Date
            return;
        }

        // Local midnight, like a calendar pick (XU-573: UTC midnight renders a day early west of UTC).
        Date = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue));
    }

    private void OnDateValueChanged()
    {
        // A calendar pick, a binding or code set the date: that replaces whatever was typed.
        ClearError();
        if (_editorFocused && _editor is not null && TryReadsAs(_editor.Text, Date))
            return; // the user's own commit; leave the caret alone unless the text needs tidying
        ShowCommittedText();
    }

    private static bool TryReadsAs(string? text, DateTimeOffset? date)
    {
        if (date is null) return string.IsNullOrWhiteSpace(text);
        var culture = CultureInfo.CurrentCulture;
        return DateInputParser.TryParse(text, DateOnly.FromDateTime(DateTime.Today), culture, out var d)
               && d == LocalDate(date.Value)
               && text?.Trim() == DateInputParser.Format(d, culture);
    }

    private string CommittedText()
        => Date is { } d ? DateInputParser.Format(LocalDate(d), CultureInfo.CurrentCulture) : string.Empty;

    private void ShowCommittedText()
    {
        if (_editor is null) return;
        var text = CommittedText();
        if (_editor.Text != text) _editor.Text = text;
    }

    private void UpdatePlaceholder()
    {
        if (_editor is null) return;
        // A host's own placeholder ("No end", "Pick a date") says something; the stock "select a date"
        // doesn't, and a typed field is better served by the format to type.
        // Uno reports the stock text as a local value, so compare against it rather than trusting
        // ReadLocalValue alone.
        var text = PlaceholderText;
        var stock = PlaceholderTextProperty.GetMetadata(typeof(CalendarDatePicker))?.DefaultValue as string;
        _editor.PlaceholderText = !string.IsNullOrEmpty(text) && text != stock && text != "select a date"
            ? text
            : DateInputParser.Hint(CultureInfo.CurrentCulture);
    }

    // The base control positions a DateTimeOffset by local time; read it the same way.
    private static DateOnly LocalDate(DateTimeOffset value) => DateOnly.FromDateTime(value.LocalDateTime);

    // ===== Error display =====

    private void ShowError(string message)
    {
        if (!HasInvalidEntry)
        {
            _savedBorderBrush = ReadLocalValue(BorderBrushProperty);
            _savedDescription = ReadLocalValue(DescriptionProperty);
        }
        HasInvalidEntry = true;
        BorderBrush = ThemeBrush("SystemFillColorCriticalBrush", Colors.Firebrick);
        Description = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("SystemFillColorCriticalBrush", Colors.Firebrick),
            FontSize = FontSizeToken("XcFontSizeCaption", 12),
        };
    }

    private void ClearError()
    {
        if (!HasInvalidEntry) return;
        HasInvalidEntry = false;
        Restore(BorderBrushProperty, _savedBorderBrush);
        Restore(DescriptionProperty, _savedDescription);
        _savedBorderBrush = _savedDescription = null;
    }

    private void Restore(DependencyProperty dp, object? saved)
    {
        if (saved is null || saved == DependencyProperty.UnsetValue) ClearValue(dp);
        else SetValue(dp, saved);
    }

    private static Brush ThemeBrush(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Brush b
            ? b
            : new SolidColorBrush(fallback);

    private static double FontSizeToken(string key, double fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is double d ? d : fallback;
}
