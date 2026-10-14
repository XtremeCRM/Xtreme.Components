using Xtreme.Components.Gallery.Pages;

namespace Xtreme.Components.Gallery;

public sealed partial class MainPage : Page
{
    private const string Repo = "https://github.com/XtremeCRM/Xtreme.Components/blob/main/";

    private static readonly (string Tag, string Title, string Blurb, string Docs, Func<UIElement> Create)[] Catalog =
    [
        ("scheduler", "Scheduler",
         "Xtreme.Components.Scheduler. Day / Week / Month / Agenda / Planner. Drag an appointment to move it, drag its edge to resize, double-click or drag across an empty slot to create. The Examples switches add a host-side resource picker, data-driven tiles (status, technician, and a customer balance joined in by event id; the toolbar's tile options switch each one), indicator icons, positioned adornments (the toolbar's Urgent / Paid toggles act on the selected appointment), tooltip details (Details rows, a Description, and a custom renderer on the Install job, on top of the always-on subject-and-time tooltip), and an appointment editor: with it on, double-click an appointment or an empty slot to open a side form built from SectionCard, DateTimePicker and EditorActionBar. Context menu: right-click an appointment for a host-built MenuFlyout (edit, complete, urgent, duplicate, delete).",
         Repo + "docs/scheduler.md",
         () => new SchedulerPage()),
        ("docking", "Docking",
         "Xtreme.Components.Docking. A center document with panes on all four edges, two stacked on the left and two tabbed. Drag a header or tab onto the guides, pin a pane to its rail, narrow the window below ~580px to see the overlay mode.",
         Repo + "docs/docking.md",
         () => new DockingPage()),
        ("cards", "Cards",
         "Xtreme.Components.Cards. SectionCard frames with a tab strip, and a CardDockerControl: drag cards between panels, into the empty slot at a column's foot, or into the tray.",
         Repo + "docs/cards.md",
         () => new CardsPage()),
        ("primitives", "Primitives",
         "Xtreme.Components.Primitives. Masked input, typeable time, date pickers, paging, panels, toast, the editor action bar, chip picker and the overlay glyph icon.",
         Repo + "docs/primitives.md",
         () => new PrimitivesPage()),
        ("tableview", "TableView",
         "Xtreme.Components.TableView. RowFlagDecorator frames flagged WinUI.TableView rows and tags them; toggle a row's state and the frame follows.",
         Repo + "docs/tableview.md",
         () => new TableViewPage()),
    ];

    public MainPage()
    {
        InitializeComponent();
        VersionText.Text = "Xtreme.Components " + (typeof(Core.ComponentHost).Assembly.GetName().Version?.ToString(3) ?? "");
        Loaded += (_, _) =>
        {
            DarkToggle.IsOn = ActualTheme == ElementTheme.Dark;
            Nav.SelectedItem = Nav.MenuItems[0];
        };
    }

    /// <summary>
    /// Opens every page in turn and reports the first exception. Driven by <c>--selftest</c> on the
    /// desktop head so a build can prove each page constructs and lays out without a person watching.
    /// </summary>
    public async Task<int> SelfTestAsync()
    {
        var failures = 0;
        var core = CoreSelfCheck();
        foreach (var problem in core)
            Console.WriteLine($"selftest: core FAILED: {problem}");
        if (core.Count == 0)
            Console.WriteLine("selftest: core ok");
        failures += core.Count;

        foreach (var entry in Catalog)
        {
            try
            {
                Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == entry.Tag);
                await Task.Delay(700);
                if (Host.Content is SchedulerPage scheduler)
                {
                    scheduler.EnableAllExamples();   // tiles, icons, adornments, resource picker
                    await Task.Delay(700);
                    if (await scheduler.PickerSelfTestAsync() is { Count: > 0 } pickerProblems)
                        throw new InvalidOperationException("Date picker checks failed: " + string.Join("; ", pickerProblems));
                }
                Host.UpdateLayout();
                if (Host.Content is TableViewPage table && await table.SelfCheckAsync() is { Count: > 0 } problems)
                    throw new InvalidOperationException("RowFlagDecorator checks failed: " + string.Join("; ", problems));
                if (Host.Content is DockingPage docking && await docking.SelfTestAsync() is { Count: > 0 } dockProblems)
                    throw new InvalidOperationException("Dock drag checks failed: " + string.Join("; ", dockProblems));
                Console.WriteLine($"selftest: {entry.Tag} ok ({Host.ActualWidth:0}x{Host.ActualHeight:0})");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"selftest: {entry.Tag} FAILED: {ex}");
            }
        }
        return failures;
    }

    /// <summary>
    /// <c>--selftest</c> checks for Xtreme.Components.Core against the live application resources:
    /// per-theme lookup through a merged dictionary's ThemeDictionaries, app-level overrides winning
    /// over merged ones, shared surface brushes, the startup hooks, and the dialog XamlRoot.
    /// </summary>
    private List<string> CoreSelfCheck()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }
        static Color? ColorOf(Brush? b) => (b as SolidColorBrush)?.Color;

        var red = Color.FromArgb(0xFF, 0xFF, 0, 0);
        var blue = Color.FromArgb(0xFF, 0, 0, 0xFF);
        var green = Color.FromArgb(0xFF, 0, 0x80, 0);

        // A palette merged the way a library or brand dictionary would be: per-theme values only.
        var palette = new ResourceDictionary();
        var light = new ResourceDictionary { ["SelfTestThemedBrush"] = new SolidColorBrush(red), ["SelfTestOverriddenBrush"] = new SolidColorBrush(red) };
        var dark = new ResourceDictionary { ["SelfTestThemedBrush"] = new SolidColorBrush(blue), ["SelfTestOverriddenBrush"] = new SolidColorBrush(blue) };
        palette.ThemeDictionaries["Light"] = light;
        palette.ThemeDictionaries["Dark"] = dark;
        var wrapper = new ResourceDictionary();   // one level deeper, to prove the walk recurses
        wrapper.MergedDictionaries.Add(palette);

        var app = Application.Current.Resources;
        app.MergedDictionaries.Add(wrapper);
        app["SelfTestOverriddenBrush"] = new SolidColorBrush(green);   // an app-level flat override
        try
        {
            Check(ColorOf(Core.ComponentHost.TryGetBrush("SelfTestThemedBrush", ElementTheme.Light)) == red, "merged ThemeDictionaries: Light value");
            Check(ColorOf(Core.ComponentHost.TryGetBrush("SelfTestThemedBrush", ElementTheme.Dark)) == blue, "merged ThemeDictionaries: Dark value");
            Check(ColorOf(Core.ComponentHost.TryGetBrush("SelfTestOverriddenBrush", ElementTheme.Dark)) == green, "app-level override beats a merged theme value");
            Check(Core.ComponentHost.TryGetBrush("SelfTestMissingBrush", ElementTheme.Dark) is null, "missing key is null");
            Check(ColorOf(Core.ComponentHost.TryGetBrush("GalleryFlagChangedBrush", ElementTheme.Dark)) == Color.FromArgb(0xFF, 0xF0, 0x8A, 0x7E), "app ThemeDictionaries: Dark value");
        }
        finally
        {
            app.Remove("SelfTestOverriddenBrush");
            app.MergedDictionaries.Remove(wrapper);
        }

        Check(ReferenceEquals(Core.ComponentTheme.Surface(ElementTheme.Dark), Core.ComponentTheme.Surface(ElementTheme.Dark)), "Surface brush is cached per theme");
        Check(!ReferenceEquals(Core.ComponentTheme.Surface(ElementTheme.Dark), Core.ComponentTheme.Surface(ElementTheme.Light)), "Surface brush differs per theme");

        Check(Core.ComponentHost.CurrentTheme?.Invoke() == ActualTheme, "CurrentTheme hook set at startup follows the root page");
        Check(Core.ComponentHost.ResolveGeometry?.Invoke("star") is not null, "ResolveGeometry hook set at startup");

        var dialog = new ContentDialog();
        Core.ComponentHost.PrepareDialog(dialog, XamlRoot);
        Check(ReferenceEquals(dialog.XamlRoot, XamlRoot), "PrepareDialog sets the given XamlRoot");

        return failures;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
            return;
        var entry = Catalog.First(c => c.Tag == tag);
        PageTitle.Text = entry.Title;
        PageBlurb.Text = entry.Blurb;
        DocsLink.NavigateUri = new Uri(entry.Docs);
        DocsLink.Content = $"Docs: {entry.Title} guide";
        Host.Content = entry.Create();
    }

    private void DarkToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // Drive the theme from the root element so the switch is live. The library reads it through
        // ComponentHost.CurrentTheme, which App.OnLaunched pointed at this page once at startup.
        RequestedTheme = DarkToggle.IsOn ? ElementTheme.Dark : ElementTheme.Light;
    }
}
