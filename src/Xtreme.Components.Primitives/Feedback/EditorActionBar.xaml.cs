using Microsoft.UI.Xaml.Controls;

namespace Xtreme.Components.Primitives;

/// <summary>
/// The standard editor button panel. Encodes the consistency rule structurally so it
/// can't be gotten wrong: a trash-icon <b>Delete</b> on the far left, an optional
/// <see cref="CenterContent"/> slot for odd extras, and the <b>Save</b>/<b>Cancel</b>
/// pair pinned far right. Save is accent (primary), Cancel default (secondary). Both
/// Save and Cancel are enabled only while <see cref="IsDirty"/> is true; Save is
/// additionally gated by <see cref="CanSave"/> (validation), and Cancel can be forced
/// always-live with <see cref="AlwaysEnableCancel"/>.
///
/// <para>Hosts wire <see cref="SaveRequested"/>/<see cref="CancelRequested"/>/
/// <see cref="DeleteRequested"/> to their existing handlers. When
/// <see cref="DeleteConfirmMessage"/> is set, the bar shows the standard confirm
/// dialog before raising <see cref="DeleteRequested"/>; otherwise it raises
/// immediately and the host owns confirmation.</para>
/// </summary>
public sealed partial class EditorActionBar : UserControl
{
    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? DeleteRequested;

    public EditorActionBar()
    {
        InitializeComponent();
        // The change callback only runs when DeleteText changes, so seed the default tooltip here.
        ToolTipService.SetToolTip(DeleteButton, DeleteText);
    }

    // ---- IsDirty: drives Save + Cancel enabled-state ----
    public static readonly DependencyProperty IsDirtyProperty = DependencyProperty.Register(
        nameof(IsDirty), typeof(bool), typeof(EditorActionBar),
        new PropertyMetadata(false, OnStateChanged));

    public bool IsDirty
    {
        get => (bool)GetValue(IsDirtyProperty);
        set => SetValue(IsDirtyProperty, value);
    }

    // ---- CanSave: extra validation gate, ANDed into Save only ----
    public static readonly DependencyProperty CanSaveProperty = DependencyProperty.Register(
        nameof(CanSave), typeof(bool), typeof(EditorActionBar),
        new PropertyMetadata(true, OnStateChanged));

    public bool CanSave
    {
        get => (bool)GetValue(CanSaveProperty);
        set => SetValue(CanSaveProperty, value);
    }

    // ---- AlwaysEnableCancel: keeps Cancel live even on a pristine editor ----
    // In a blade or section footer Cancel means "revert my changes", so gating it on IsDirty
    // is correct — there is nothing to revert. In an in-list row editor Cancel is also the only
    // way OUT of edit mode, so a row opened and left untouched would have no exit. Hosts in that
    // shape set this true; the dirty gate on Save is unaffected.
    public static readonly DependencyProperty AlwaysEnableCancelProperty = DependencyProperty.Register(
        nameof(AlwaysEnableCancel), typeof(bool), typeof(EditorActionBar),
        new PropertyMetadata(false, OnStateChanged));

    public bool AlwaysEnableCancel
    {
        get => (bool)GetValue(AlwaysEnableCancelProperty);
        set => SetValue(AlwaysEnableCancelProperty, value);
    }

    // ---- ShowDelete: shows/collapses the trash button ----
    public static readonly DependencyProperty ShowDeleteProperty = DependencyProperty.Register(
        nameof(ShowDelete), typeof(bool), typeof(EditorActionBar),
        new PropertyMetadata(false, OnShowDeleteChanged));

    public bool ShowDelete
    {
        get => (bool)GetValue(ShowDeleteProperty);
        set => SetValue(ShowDeleteProperty, value);
    }

    // ---- Button text (Save defaults "Save", allow "OK"; Delete text = tooltip) ----
    public static readonly DependencyProperty SaveTextProperty = DependencyProperty.Register(
        nameof(SaveText), typeof(string), typeof(EditorActionBar),
        new PropertyMetadata("Save", OnSaveTextChanged));

    public string SaveText
    {
        get => (string)GetValue(SaveTextProperty);
        set => SetValue(SaveTextProperty, value);
    }

    public static readonly DependencyProperty CancelTextProperty = DependencyProperty.Register(
        nameof(CancelText), typeof(string), typeof(EditorActionBar),
        new PropertyMetadata("Cancel", OnCancelTextChanged));

    public string CancelText
    {
        get => (string)GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public static readonly DependencyProperty DeleteTextProperty = DependencyProperty.Register(
        nameof(DeleteText), typeof(string), typeof(EditorActionBar),
        new PropertyMetadata("Delete", OnDeleteTextChanged));

    public string DeleteText
    {
        get => (string)GetValue(DeleteTextProperty);
        set => SetValue(DeleteTextProperty, value);
    }

    // ---- Optional baked-in delete confirmation ----
    public static readonly DependencyProperty DeleteConfirmTitleProperty = DependencyProperty.Register(
        nameof(DeleteConfirmTitle), typeof(string), typeof(EditorActionBar),
        new PropertyMetadata("Delete?"));

    public string DeleteConfirmTitle
    {
        get => (string)GetValue(DeleteConfirmTitleProperty);
        set => SetValue(DeleteConfirmTitleProperty, value);
    }

    /// <summary>When non-empty, the bar shows a standard confirm dialog before raising
    /// <see cref="DeleteRequested"/>. When empty, Delete raises immediately.</summary>
    public static readonly DependencyProperty DeleteConfirmMessageProperty = DependencyProperty.Register(
        nameof(DeleteConfirmMessage), typeof(string), typeof(EditorActionBar),
        new PropertyMetadata(null));

    public string? DeleteConfirmMessage
    {
        get => (string?)GetValue(DeleteConfirmMessageProperty);
        set => SetValue(DeleteConfirmMessageProperty, value);
    }

    // ---- ShowSaveCancel: hides the Save/Cancel pair (e.g. a read-only / locked mode) ----
    public static readonly DependencyProperty ShowSaveCancelProperty = DependencyProperty.Register(
        nameof(ShowSaveCancel), typeof(bool), typeof(EditorActionBar),
        new PropertyMetadata(true, OnShowSaveCancelChanged));

    public bool ShowSaveCancel
    {
        get => (bool)GetValue(ShowSaveCancelProperty);
        set => SetValue(ShowSaveCancelProperty, value);
    }

    // ---- IsDeleteEnabled: disables (but keeps visible) the Delete button ----
    public static readonly DependencyProperty IsDeleteEnabledProperty = DependencyProperty.Register(
        nameof(IsDeleteEnabled), typeof(bool), typeof(EditorActionBar),
        new PropertyMetadata(true, OnIsDeleteEnabledChanged));

    public bool IsDeleteEnabled
    {
        get => (bool)GetValue(IsDeleteEnabledProperty);
        set => SetValue(IsDeleteEnabledProperty, value);
    }

    // ---- CenterContent: host-specific extra (e.g. an Unlock button) ----
    public static readonly DependencyProperty CenterContentProperty = DependencyProperty.Register(
        nameof(CenterContent), typeof(object), typeof(EditorActionBar),
        new PropertyMetadata(null, OnCenterContentChanged));

    public object? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    // ---- Property-changed plumbing ----
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EditorActionBar)d).UpdateButtonStates();

    private void UpdateButtonStates()
    {
        SaveButton.IsEnabled = IsDirty && CanSave;
        CancelButton.IsEnabled = IsDirty || AlwaysEnableCancel;
    }

    private static void OnShowDeleteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (EditorActionBar)d;
        bar.DeleteButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnShowSaveCancelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (EditorActionBar)d;
        bar.SaveCancelPanel.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnIsDeleteEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EditorActionBar)d).DeleteButton.IsEnabled = (bool)e.NewValue;

    private static void OnSaveTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EditorActionBar)d).SaveButton.Content = (string)e.NewValue;

    private static void OnCancelTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EditorActionBar)d).CancelButton.Content = (string)e.NewValue;

    private static void OnDeleteTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ToolTipService.SetToolTip(((EditorActionBar)d).DeleteButton, (string)e.NewValue);

    private static void OnCenterContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (EditorActionBar)d;
        bar.CenterHost.Content = e.NewValue;
        // Pad the slot only when it holds something. An empty ContentControl still claims its
        // margin when the star column is measured unconstrained, which pushes Save/Cancel around
        // in Auto-width hosts (see the in-list row editors in EntityBatteryPlansControl).
        bar.CenterHost.Margin = e.NewValue is null ? new Thickness(0) : new Thickness(12, 0, 12, 0);
    }

    // ---- Click handlers ----
    private void OnSaveClick(object sender, RoutedEventArgs e)
        => SaveRequested?.Invoke(this, EventArgs.Empty);

    private void OnCancelClick(object sender, RoutedEventArgs e)
        => CancelRequested?.Invoke(this, EventArgs.Empty);

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(DeleteConfirmMessage))
        {
            var dialog = new ContentDialog
            {
                Title = DeleteConfirmTitle,
                Content = DeleteConfirmMessage,
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            // App dialog style + the app's effective theme (ComponentHost.CurrentTheme), like every
            // other code-built dialog in the library.
            Xtreme.Components.Core.ComponentHost.PrepareDialog(dialog);

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;
        }

        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }
}
