using Xtreme.Components.Docking.Controls;

namespace Xtreme.Components.Docking.Tests;

public class FloatPlacementTests
{
    private static readonly DockWindowBounds Primary = new(0, 0, 1920, 1040);
    private static readonly DockWindowBounds LeftMonitor = new(-1600, 0, 1600, 860);
    private static readonly FrameInsets Frame = new(8, 31, 8, 8);

    [Fact]
    public void First_float_opens_over_the_pane_at_its_docked_size()
    {
        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            Pane = new DockWindowBounds(1500, 200, 300, 500),
            Insets = Frame,
            WorkAreas = [Primary],
        });

        // The client area lands exactly where the pane was.
        Assert.Equal(new DockWindowBounds(1500 - 8, 200 - 31, 300 + 16, 500 + 39), plan);
    }

    [Fact]
    public void First_float_is_capped_at_eighty_percent_of_the_monitor()
    {
        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            Pane = new DockWindowBounds(0, 900, 1920, 140),
            Insets = Frame,
            WorkAreas = [Primary],
        });

        Assert.Equal((int)(1920 * FloatPlacement.MaxWorkAreaFraction), plan.Width);
        Assert.True(plan.X >= Primary.X && plan.X + plan.Width <= Primary.Width);
        Assert.True(plan.Y + plan.Height <= Primary.Height);
    }

    [Fact]
    public void Remembered_bounds_win_over_the_pane()
    {
        var remembered = new DockWindowBounds(400, 300, 640, 480);

        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            Pane = new DockWindowBounds(1500, 200, 300, 500),
            Remembered = remembered,
            Insets = Frame,
            WorkAreas = [Primary],
        });

        Assert.Equal(remembered, plan);
    }

    [Fact]
    public void Remembered_position_on_an_unplugged_monitor_moves_onto_a_connected_one()
    {
        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            Remembered = new DockWindowBounds(-1200, 100, 640, 480),
            Insets = Frame,
            WorkAreas = [Primary], // the left monitor is gone
        });

        Assert.Equal(640, plan.Width);
        Assert.Equal(480, plan.Height);
        Assert.Equal(0, plan.X);
        Assert.Equal(100, plan.Y);
    }

    [Fact]
    public void Remembered_bounds_on_a_second_monitor_stay_there()
    {
        var remembered = new DockWindowBounds(-1200, 100, 640, 480);

        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            Remembered = remembered,
            Insets = Frame,
            WorkAreas = [Primary, LeftMonitor],
        });

        Assert.Equal(remembered, plan);
    }

    [Fact]
    public void Tear_out_keeps_the_grab_point_under_the_cursor()
    {
        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            Pane = new DockWindowBounds(1500, 200, 300, 500),
            Cursor = (900, 400),
            GrabX = 60,
            Insets = Frame,
            WorkAreas = [Primary],
        });

        Assert.Equal(900 - 8 - 60, plan.X);
        Assert.Equal(400 - 31 / 2, plan.Y);
        Assert.Equal(300 + 16, plan.Width);
    }

    [Fact]
    public void Tear_out_near_the_top_keeps_the_caption_on_screen_but_not_the_sides()
    {
        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            Pane = new DockWindowBounds(1500, 200, 300, 500),
            Cursor = (1910, 2),
            GrabX = 60,
            Insets = Frame,
            WorkAreas = [Primary],
        });

        Assert.Equal(0, plan.Y);
        Assert.Equal(1910 - 8 - 60, plan.X); // allowed to hang off the right edge with the pointer
    }

    [Fact]
    public void Tear_out_uses_the_remembered_size_at_the_cursor()
    {
        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            Remembered = new DockWindowBounds(100, 100, 700, 450),
            Cursor = (900, 400),
            GrabX = 20,
            Insets = Frame,
            WorkAreas = [Primary],
        });

        Assert.Equal((900 - 8 - 20, 400 - 15, 700, 450), (plan.X, plan.Y, plan.Width, plan.Height));
    }

    [Fact]
    public void Grab_offset_is_kept_inside_the_new_window()
    {
        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            Pane = new DockWindowBounds(0, 0, 300, 400),
            Cursor = (900, 400),
            GrabX = 5000, // grabbed far along a wide header; the window is only 300 wide
            Insets = Frame,
            WorkAreas = [Primary],
        });

        Assert.InRange(900, plan.X, plan.X + plan.Width);
    }

    [Fact]
    public void Unknown_monitors_skip_fitting_but_keep_a_minimum_size()
    {
        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            Pane = new DockWindowBounds(10, 10, 50, 40),
            Insets = default,
        });

        Assert.Equal(new DockWindowBounds(10, 10, FloatPlacement.MinWidth, FloatPlacement.MinHeight), plan);
    }

    [Fact]
    public void Nothing_known_centers_on_the_first_monitor()
    {
        var plan = FloatPlacement.Plan(new FloatPlacementRequest
        {
            FallbackClientSize = (400, 300),
            Insets = Frame,
            WorkAreas = [Primary],
        });

        Assert.Equal(416, plan.Width);
        Assert.Equal((1920 - 416) / 2, plan.X);
    }

    [Fact]
    public void Layout_document_keeps_float_bounds_for_a_docked_pane()
    {
        var record = new DockLayoutRecord("map")
        {
            Edge = DockEdge.Right,
            Length = 300,
            IsFloating = false,
            FloatBounds = new DockWindowBounds(10, 20, 640, 480),
        };

        var parsed = Assert.Single(DockLayoutDocument.Parse(DockLayoutDocument.Serialize([record])));

        Assert.False(parsed.IsFloating);
        Assert.Equal(record.FloatBounds, parsed.FloatBounds);
    }
}
