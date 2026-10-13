using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Xtreme.Components.Primitives;

/// <summary>
/// A titled group of Advanced Search criteria, laid out label-left in a two-column grid.
///
/// <b>Why this exists.</b> The criteria area used to be one flat list of fields reflowed into 1–4
/// columns by width. Because it flowed INDIVIDUAL FIELDS, a resize re-shuffled everything: the
/// address fields could end up split across columns with a date picker wedged between them, and the
/// section headings no longer sat above the fields they described. Grouping into panels and flowing
/// the PANELS keeps each group intact at any width — a group is the unit that moves, not a field.
///
/// <b>Layout.</b> Rows are built in code-behind rather than declared, so column 0 auto-sizes to the
/// widest label in this panel and column 1 stretches. That is what makes every field in a panel the
/// same width and every label align, without hand-maintaining <c>Grid.Row</c> indices across ~20
/// fields per area.
///
/// <code>
/// &lt;controls:CriteriaPanel Title="Address" MinWidth="320"&gt;
///     &lt;TextBox controls:CriteriaPanel.Label="Street" Text="{Binding Street, Mode=TwoWay}" /&gt;
///     &lt;TextBox controls:CriteriaPanel.Label="City"   Text="{Binding City,   Mode=TwoWay}" /&gt;
/// &lt;/controls:CriteriaPanel&gt;
/// </code>
///
/// Fields keep their own bindings and styles; only their labels move. Set the label with the
/// attached <see cref="LabelProperty"/> instead of the control's own <c>Header</c>, or the label
/// renders twice.
///
/// <see cref="FieldValueChanged"/> reports edits in any of the fields (for a live "Search" enable or a
/// criteria summary), so a host does not have to wire every field itself.
/// </summary>
[ContentProperty(Name = nameof(Fields))]
public sealed partial class CriteriaPanel : UserControl
{
    // One unhook action per hooked element (a field, or an input nested in a field's panel).
    private readonly Dictionary<FrameworkElement, Action> _hooks = new();

    public CriteriaPanel()
    {
        InitializeComponent();
        TitleText.Style = HeadingStyle();
        Fields.CollectionChanged += OnFieldsChanged;
        Loaded += (_, _) => Rebuild();
    }

    /// <summary>
    /// Raised when the value of a field in <see cref="Fields"/> changes; the argument is that field
    /// (the element in <see cref="Fields"/>, also when the edit was in an input nested inside it).
    /// Recognized inputs: TextBox, PasswordBox, AutoSuggestBox, ComboBox and other Selectors,
    /// ToggleButton / CheckBox / RadioButton, ToggleSwitch, CalendarDatePicker, DatePicker, TimePicker,
    /// NumberBox, RangeBase (Slider), and this package's MaskedTextBox, TypeableTimeBox, DateTimePicker,
    /// MonthYearPicker and ChipPickerControl. Panels are searched for inputs (their Children, not
    /// templated content) when the field is added. Other elements are laid out but not watched.
    /// </summary>
    public event EventHandler<FrameworkElement>? FieldValueChanged;

    // BodyStrongTextBlockStyle comes from XamlControlsResources; without it, a local equivalent.
    private Style HeadingStyle()
        => Application.Current?.Resources.TryGetValue("BodyStrongTextBlockStyle", out var s) == true && s is Style style
            ? style
            : (Style)Resources["CriteriaHeadingFallbackStyle"];

    /// <summary>Heading shown above the group (e.g. "Name", "Address", "Dates").</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(CriteriaPanel),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    /// <summary>An empty title collapses the heading row rather than leaving a blank line — the shape
    /// a panel wants when its groups carry their own headings instead.</summary>
    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CriteriaPanel panel && panel.TitleText is not null)
            panel.TitleText.Visibility = string.IsNullOrWhiteSpace(e.NewValue as string)
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    /// <summary>
    /// The fields in this group, in display order. This is the XAML content property, so children
    /// written between the tags land here.
    /// </summary>
    public ObservableCollection<FrameworkElement> Fields { get; } = new();

    // ---- Attached: the label shown in column 0 for a field ----

    /// <summary>
    /// The label for a field, rendered in the panel's left column. Prefer this over a control's own
    /// <c>Header</c>: headers sit ABOVE the control, which is the stacked layout this panel replaces.
    /// A field with no label (or an empty one) simply spans both columns — the shape a checkbox
    /// wants, since its own content already reads as its label.
    /// </summary>
    public static string GetLabel(DependencyObject obj) => (string)obj.GetValue(LabelProperty);

    public static void SetLabel(DependencyObject obj, string value) => obj.SetValue(LabelProperty, value);

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.RegisterAttached("Label", typeof(string), typeof(CriteriaPanel),
            new PropertyMetadata(string.Empty));

    // ---- Attached: a heading that starts a new group of fields WITHIN the panel ----

    /// <summary>
    /// Starts a labelled group inside the panel: the heading renders on its own full-width row
    /// immediately above this field.
    ///
    /// This is what lets several related ranges share one panel instead of each needing its own.
    /// A date range is the motivating case — "Customer Since" and "Date Created" both belong under
    /// one Dates panel, but each needs its own From and To rows, and those two Froms would be
    /// indistinguishable without a heading between them.
    /// </summary>
    public static string GetHeading(DependencyObject obj) => (string)obj.GetValue(HeadingProperty);

    public static void SetHeading(DependencyObject obj, string value) => obj.SetValue(HeadingProperty, value);

    public static readonly DependencyProperty HeadingProperty =
        DependencyProperty.RegisterAttached("Heading", typeof(string), typeof(CriteriaPanel),
            new PropertyMetadata(string.Empty));

    private void OnFieldsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncHooks();
        Rebuild();
    }

    // ---- Field change tracking ----

    // Hook every field (and inputs nested in its panels) that is in Fields and not hooked yet; unhook
    // whatever left. Re-run on every collection change, so Reset / Replace need no special casing.
    private void SyncHooks()
    {
        var wanted = new Dictionary<FrameworkElement, FrameworkElement>(); // element -> owning field
        foreach (var field in Fields)
            Collect(field, field, wanted);

        foreach (var gone in _hooks.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
        {
            _hooks[gone]();
            _hooks.Remove(gone);
        }
        foreach (var (element, owner) in wanted)
        {
            if (!_hooks.ContainsKey(element))
                _hooks[element] = Hook(element, () => FieldValueChanged?.Invoke(this, owner));
        }
    }

    private static void Collect(FrameworkElement element, FrameworkElement owner, Dictionary<FrameworkElement, FrameworkElement> into)
    {
        into.TryAdd(element, owner);
        if (element is Panel panel)
        {
            foreach (var child in panel.Children.OfType<FrameworkElement>())
                Collect(child, owner, into);
        }
    }

    // Subscribes to the element's value-change event (if it is a recognized input) and returns the
    // matching unsubscribe. Order matters where types derive from one another.
    private static Action Hook(FrameworkElement element, Action raise)
    {
        switch (element)
        {
            case PasswordBox pb: // before TextBox: Uno's PasswordBox derives from it
                RoutedEventHandler pw = (_, _) => raise();
                pb.PasswordChanged += pw;
                return () => pb.PasswordChanged -= pw;
            case TextBox tb:
                TextChangedEventHandler text = (_, _) => raise();
                tb.TextChanged += text;
                return () => tb.TextChanged -= text;
            case AutoSuggestBox asb:
                Windows.Foundation.TypedEventHandler<AutoSuggestBox, AutoSuggestBoxTextChangedEventArgs> suggest = (_, _) => raise();
                asb.TextChanged += suggest;
                return () => asb.TextChanged -= suggest;
            case Microsoft.UI.Xaml.Controls.Primitives.Selector sel:
                SelectionChangedEventHandler selection = (_, _) => raise();
                sel.SelectionChanged += selection;
                return () => sel.SelectionChanged -= selection;
            case Microsoft.UI.Xaml.Controls.Primitives.ToggleButton toggle:
                RoutedEventHandler check = (_, _) => raise();
                toggle.Checked += check;
                toggle.Unchecked += check;
                toggle.Indeterminate += check;
                return () => { toggle.Checked -= check; toggle.Unchecked -= check; toggle.Indeterminate -= check; };
            case ToggleSwitch ts:
                RoutedEventHandler toggled = (_, _) => raise();
                ts.Toggled += toggled;
                return () => ts.Toggled -= toggled;
            case CalendarDatePicker cdp:
                Windows.Foundation.TypedEventHandler<CalendarDatePicker, CalendarDatePickerDateChangedEventArgs> cdate = (_, _) => raise();
                cdp.DateChanged += cdate;
                return () => cdp.DateChanged -= cdate;
            case DatePicker dp:
                EventHandler<DatePickerValueChangedEventArgs> date = (_, _) => raise();
                dp.DateChanged += date;
                return () => dp.DateChanged -= date;
            case TimePicker tp:
                EventHandler<TimePickerValueChangedEventArgs> time = (_, _) => raise();
                tp.TimeChanged += time;
                return () => tp.TimeChanged -= time;
            case NumberBox nb:
                Windows.Foundation.TypedEventHandler<NumberBox, NumberBoxValueChangedEventArgs> number = (_, _) => raise();
                nb.ValueChanged += number;
                return () => nb.ValueChanged -= number;
            case Microsoft.UI.Xaml.Controls.Primitives.RangeBase range:
                Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventHandler ranged = (_, _) => raise();
                range.ValueChanged += ranged;
                return () => range.ValueChanged -= ranged;
            case DateTimePicker dtp:
                EventHandler value = (_, _) => raise();
                dtp.ValueChanged += value;
                return () => dtp.ValueChanged -= value;
            case MonthYearPicker myp:
                EventHandler<DateTimeOffset> month = (_, _) => raise();
                myp.SelectedDateChanged += month;
                return () => myp.SelectedDateChanged -= month;
            case MaskedTextBox mtb:
                return WatchProperty(mtb, MaskedTextBox.TextProperty, raise);
            case TypeableTimeBox ttb:
                return WatchProperty(ttb, TypeableTimeBox.TimeProperty, raise);
            case ChipPickerControl chips:
                return WatchProperty(chips, ChipPickerControl.ItemsProperty, raise);
            default:
                return static () => { };
        }
    }

    private static Action WatchProperty(DependencyObject target, DependencyProperty property, Action raise)
    {
        var token = target.RegisterPropertyChangedCallback(property, (_, _) => raise());
        return () => target.UnregisterPropertyChangedCallback(property, token);
    }

    /// <summary>
    /// Rebuild the row grid from <see cref="Fields"/>. Cheap and idempotent: these panels hold a
    /// dozen or so children and only change when the markup does.
    /// </summary>
    private void Rebuild()
    {
        if (FieldGrid is null) return;

        FieldGrid.Children.Clear();
        FieldGrid.RowDefinitions.Clear();

        if (TitleText is not null)
            TitleText.Visibility = string.IsNullOrWhiteSpace(Title) ? Visibility.Collapsed : Visibility.Visible;

        int row = 0;
        bool first = true;
        foreach (var field in Fields)
        {
            // A heading opens a new group: its own row, spanning both columns.
            var heading = GetHeading(field);
            if (!string.IsNullOrEmpty(heading))
            {
                FieldGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var head = new TextBlock
                {
                    Text = heading,
                    Style = HeadingStyle(),
                    // Breathing room above every group but the first, so groups read as separated
                    // without a divider.
                    Margin = new Thickness(0, first ? 0 : 8, 0, 0),
                };
                Grid.SetRow(head, row);
                Grid.SetColumn(head, 0);
                Grid.SetColumnSpan(head, 2);
                FieldGrid.Children.Add(head);
                row++;
            }
            first = false;

            FieldGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = GetLabel(field);
            if (string.IsNullOrEmpty(label))
            {
                // Unlabelled (a checkbox, or a composite that carries its own labels): span both
                // columns so it uses the panel's full width rather than sitting in the field column
                // with an empty gutter beside it.
                Grid.SetRow(field, row);
                Grid.SetColumn(field, 0);
                Grid.SetColumnSpan(field, 2);
            }
            else
            {
                var text = new TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                text.Style = (Style)Resources["CriteriaLabelStyle"];
                Grid.SetRow(text, row);
                Grid.SetColumn(text, 0);
                FieldGrid.Children.Add(text);

                Grid.SetRow(field, row);
                Grid.SetColumn(field, 1);
                Grid.SetColumnSpan(field, 1);
            }

            field.HorizontalAlignment = HorizontalAlignment.Stretch;
            FieldGrid.Children.Add(field);
            row++;
        }
    }
}
