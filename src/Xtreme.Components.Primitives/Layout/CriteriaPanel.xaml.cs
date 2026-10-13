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
/// </summary>
[ContentProperty(Name = nameof(Fields))]
public sealed partial class CriteriaPanel : UserControl
{
    public CriteriaPanel()
    {
        InitializeComponent();
        Fields.CollectionChanged += OnFieldsChanged;
        Loaded += (_, _) => Rebuild();
    }

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

    private void OnFieldsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

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
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
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
