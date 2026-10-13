using Xtreme.Components.Gallery.Pages;

namespace Xtreme.Components.Gallery;

public sealed partial class MainPage : Page
{
    private static readonly (string Tag, string Title, string Blurb, Func<UIElement> Create)[] Catalog =
    [
        ("scheduler", "Scheduler",
         "Xtreme.Components.Scheduler. Day / Week / Month / Agenda / Planner. Drag an appointment to move it, drag its edge to resize, click an empty slot to create. The Examples switches add a host-side resource picker, data-driven tiles (with the toolbar's tile options), indicator icons, and positioned adornments (the toolbar's Urgent / Paid toggles act on the selected appointment).",
         () => new SchedulerPage()),
        ("docking", "Docking",
         "Xtreme.Components.Docking. A centre document with panes on all four edges, two stacked on the left and two tabbed. Drag a header or tab onto the guides, pin a pane to its rail, narrow the window below ~580px to see the overlay mode.",
         () => new DockingPage()),
        ("cards", "Cards",
         "Xtreme.Components.Cards. SectionCard frames with a tab strip, and a CardDockerControl: drag cards between panels, into the empty slot at a column's foot, or into the tray.",
         () => new CardsPage()),
        ("primitives", "Primitives",
         "Xtreme.Components.Primitives. Masked input, typeable time, date pickers, paging, panels, toast, the editor action bar, chip picker and the overlay glyph icon.",
         () => new PrimitivesPage()),
        ("tableview", "TableView",
         "Xtreme.Components.TableView. RowFlagDecorator frames flagged WinUI.TableView rows and tags them; toggle a row's state and the frame follows.",
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
                }
                Host.UpdateLayout();
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

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
            return;
        var entry = Catalog.First(c => c.Tag == tag);
        PageTitle.Text = entry.Title;
        PageBlurb.Text = entry.Blurb;
        Host.Content = entry.Create();
    }

    private void DarkToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // Drive the theme from the root element so the switch is live; tell the library where to look.
        RequestedTheme = DarkToggle.IsOn ? ElementTheme.Dark : ElementTheme.Light;
        Core.ComponentHost.CurrentTheme = () => ActualTheme;
    }
}
