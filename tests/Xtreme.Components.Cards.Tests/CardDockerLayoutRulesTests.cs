using Xtreme.Components.Cards;
using Xunit;

namespace Xtreme.Components.Cards.Tests;

/// <summary>
/// The CardDocker's normalization and edit rules, without a control: every test builds a layout,
/// applies a rule, and reads the layout back.
/// </summary>
public sealed class CardDockerLayoutRulesTests
{
    private static readonly string[] Ids = ["a", "b", "c", "d", "e", "f"];

    private static CardDockerPanel Panel(string id, int column, int sort, params string[] cards)
        => new() { Id = id, Column = column, Sort = sort, CardIds = [.. cards] };

    /// <summary>Five panels over three columns, "f" in the tray.</summary>
    private static CardDockerLayout ThreeColumns() => new()
    {
        Panels =
        [
            Panel("p1", 0, 0, "a"),
            Panel("p2", 0, 1, "b"),
            Panel("p3", 1, 0, "c"),
            Panel("p4", 2, 0, "d"),
            Panel("p5", 2, 1, "e"),
        ],
        TrayCardIds = { "f" },
    };

    private static string Columns(CardDockerLayout layout, int columnCount)
        => string.Join(" / ", Enumerable.Range(0, columnCount).Select(c =>
            string.Join(",", CardDockerLayoutRules.PanelsIn(layout, c).Select(p => p.Id))));

    private static void Normalize(CardDockerLayout layout, int columns = 3)
        => CardDockerLayoutRules.Normalize(layout, Ids, columns);

    // ---- Normalization ----

    [Fact]
    public void Normalize_drops_unknown_ids_and_empty_panels()
    {
        var layout = new CardDockerLayout
        {
            Panels = [Panel("p1", 0, 0, "a", "zzz"), Panel("p2", 0, 1, "ghost")],
            TrayCardIds = { "nope" },
        };

        Normalize(layout);

        Assert.Equal(["p1"], layout.Panels.Select(p => p.Id));
        Assert.Equal(["a"], layout.Panels[0].CardIds);
        Assert.DoesNotContain("nope", layout.TrayCardIds);
    }

    [Fact]
    public void Normalize_sends_unplaced_cards_to_the_tray_in_card_order()
    {
        var layout = new CardDockerLayout { Panels = [Panel("p1", 0, 0, "c")] };

        Normalize(layout);

        Assert.Equal(["a", "b", "d", "e", "f"], layout.TrayCardIds);
    }

    [Fact]
    public void Normalize_keeps_only_the_first_occurrence_of_a_duplicated_card()
    {
        var layout = new CardDockerLayout
        {
            Panels = [Panel("p1", 0, 0, "a", "b"), Panel("p2", 1, 0, "a")],
            TrayCardIds = { "b" },
        };

        Normalize(layout);

        Assert.Equal(["a", "b"], layout.Panels.Single().CardIds);
        Assert.DoesNotContain("a", layout.TrayCardIds);
        Assert.DoesNotContain("b", layout.TrayCardIds);
    }

    [Fact]
    public void Normalize_clamps_min_height_and_makes_sort_dense()
    {
        var layout = new CardDockerLayout
        {
            Panels =
            [
                new CardDockerPanel { Id = "p1", Column = 0, Sort = 10, MinHeight = 5, CardIds = { "a" } },
                new CardDockerPanel { Id = "p2", Column = 0, Sort = 3, MinHeight = 99999, CardIds = { "b" } },
            ],
        };

        Normalize(layout);

        Assert.Equal(CardDockerLayoutRules.MinPanelHeight, layout.Panels.Single(p => p.Id == "p1").MinHeight);
        Assert.Equal(CardDockerLayoutRules.MaxPanelHeight, layout.Panels.Single(p => p.Id == "p2").MinHeight);
        Assert.Equal("p2,p1", Columns(layout, 1));
        Assert.Equal([0, 1], CardDockerLayoutRules.PanelsIn(layout, 0).Select(p => p.Sort));
    }

    [Fact]
    public void Normalize_of_an_empty_layout_with_no_cards_is_empty()
    {
        var layout = new CardDockerLayout { Panels = [Panel("p1", 0, 0, "a")], TrayCardIds = { "b" } };

        CardDockerLayoutRules.Normalize(layout, [], 3);

        Assert.Empty(layout.Panels);
        Assert.Empty(layout.TrayCardIds);
    }

    [Fact]
    public void Normalize_is_idempotent()
    {
        var layout = ThreeColumns();
        Normalize(layout, 1);
        var once = CardDockerLayoutRules.Signature(layout);

        Normalize(layout, 1);

        Assert.Equal(once, CardDockerLayoutRules.Signature(layout));
    }

    // ---- Lowering and raising the column count ----

    [Fact]
    public void Lowering_column_count_parks_panels_below_the_last_column_in_original_order()
    {
        var layout = ThreeColumns();

        Normalize(layout, 1);

        Assert.Equal("p1,p2,p3,p4,p5", Columns(layout, 1));
        Assert.Equal(1, layout.Panels.Single(p => p.Id == "p3").PreferredColumn);
        Assert.Equal(2, layout.Panels.Single(p => p.Id == "p5").PreferredColumn);
        Assert.Null(layout.Panels.Single(p => p.Id == "p1").PreferredColumn);
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 2 })]
    [InlineData(new[] { 1, 2 })]
    [InlineData(new[] { 2, 1 })]
    public void Raising_column_count_again_restores_the_original_arrangement(int[] via)
    {
        var layout = ThreeColumns();
        Normalize(layout, 3);
        var original = CardDockerLayoutRules.Signature(layout);

        foreach (var count in via) Normalize(layout, count);
        Normalize(layout, 3);

        Assert.Equal(original, CardDockerLayoutRules.Signature(layout));
    }

    [Fact]
    public void Lowering_in_steps_parks_in_the_same_order_as_lowering_at_once()
    {
        var layout = ThreeColumns();

        Normalize(layout, 2);
        Normalize(layout, 1);

        Assert.Equal("p1,p2,p3,p4,p5", Columns(layout, 1));
    }

    [Fact]
    public void A_user_reorder_of_the_last_column_survives_renormalization()
    {
        var layout = ThreeColumns();
        Normalize(layout, 1);

        CardDockerLayoutRules.MovePanel(layout, "p1", 0, 5);   // to the foot, below the parked panels
        Normalize(layout, 1);
        Normalize(layout, 1);

        Assert.Equal("p2,p3,p4,p5,p1", Columns(layout, 1));
    }

    [Fact]
    public void Partially_raising_restores_what_fits_and_keeps_the_rest_parked_last()
    {
        var layout = ThreeColumns();
        Normalize(layout, 1);

        Normalize(layout, 2);

        Assert.Equal("p1,p2 / p3,p4,p5", Columns(layout, 2));
        Assert.Null(layout.Panels.Single(p => p.Id == "p3").PreferredColumn);
        Assert.Equal(2, layout.Panels.Single(p => p.Id == "p4").PreferredColumn);
    }

    [Fact]
    public void Preferred_column_survives_a_clone()
    {
        var layout = ThreeColumns();
        Normalize(layout, 1);

        var copy = layout.Clone();
        Normalize(copy, 3);

        Assert.Equal("p1,p2 / p3 / p4,p5", Columns(copy, 3));
    }

    // ---- Card moves ----

    [Fact]
    public void Dropping_a_card_on_its_own_seam_is_a_no_op()
    {
        var layout = new CardDockerLayout { Panels = [Panel("p1", 0, 0, "a", "b", "c")] };
        Normalize(layout);
        var before = CardDockerLayoutRules.Signature(layout);

        // "b" sits at index 1: slot 1 (its left seam) and slot 2 (its right seam) are where it is.
        CardDockerLayoutRules.MoveCardToPanel(layout, "b", "p1", 1);
        Normalize(layout);
        Assert.Equal(before, CardDockerLayoutRules.Signature(layout));

        CardDockerLayoutRules.MoveCardToPanel(layout, "b", "p1", 2);
        Normalize(layout);
        Assert.Equal(before, CardDockerLayoutRules.Signature(layout));
    }

    [Fact]
    public void Moving_a_card_within_a_panel_reorders_it()
    {
        var layout = new CardDockerLayout { Panels = [Panel("p1", 0, 0, "a", "b", "c")] };

        CardDockerLayoutRules.MoveCardToPanel(layout, "a", "p1", 3);

        Assert.Equal(["b", "c", "a"], layout.Panels[0].CardIds);
    }

    [Fact]
    public void Moving_the_last_card_out_removes_the_panel()
    {
        var layout = ThreeColumns();

        CardDockerLayoutRules.MoveCardToPanel(layout, "c", "p1", 0);
        Normalize(layout);

        Assert.DoesNotContain(layout.Panels, p => p.Id == "p3");
        Assert.Equal(["c", "a"], layout.Panels.Single(p => p.Id == "p1").CardIds);
    }

    [Fact]
    public void Moving_a_card_into_a_missing_panel_changes_nothing()
    {
        var layout = ThreeColumns();
        Normalize(layout);
        var before = CardDockerLayoutRules.Signature(layout);

        CardDockerLayoutRules.MoveCardToPanel(layout, "a", "gone", 0);
        Normalize(layout);

        Assert.Equal(before, CardDockerLayoutRules.Signature(layout));
    }

    [Fact]
    public void A_tray_card_dropped_in_a_panel_leaves_the_tray()
    {
        var layout = ThreeColumns();

        CardDockerLayoutRules.MoveCardToPanel(layout, "f", "p3", 1);

        Assert.Empty(layout.TrayCardIds);
        Assert.Equal(["c", "f"], layout.Panels.Single(p => p.Id == "p3").CardIds);
    }

    [Fact]
    public void New_panel_gets_the_first_unused_id_at_the_column_foot()
    {
        var layout = new CardDockerLayout { Panels = [Panel("p1", 0, 0, "a", "b"), Panel("p3", 0, 1, "c")] };

        CardDockerLayoutRules.MoveCardToNewPanel(layout, "b", 0);
        Normalize(layout);

        Assert.Equal("p1,p3,p2", Columns(layout, 1));
        Assert.Equal(["b"], layout.Panels.Single(p => p.Id == "p2").CardIds);
    }

    [Fact]
    public void A_lone_card_moved_to_a_new_panel_takes_its_panel_along()
    {
        var layout = ThreeColumns();
        layout.Panels.Single(p => p.Id == "p1").MinHeight = 300;

        CardDockerLayoutRules.MoveCardToNewPanel(layout, "a", 1);
        Normalize(layout);

        var moved = layout.Panels.Single(p => p.CardIds.Contains("a"));
        Assert.Equal("p1", moved.Id);
        Assert.Equal(300, moved.MinHeight);
        Assert.Equal("p2 / p3,p1 / p4,p5", Columns(layout, 3));
    }

    [Fact]
    public void A_lone_card_dropped_on_its_own_column_foot_is_a_no_op()
    {
        var layout = ThreeColumns();
        Normalize(layout);
        var before = CardDockerLayoutRules.Signature(layout);

        CardDockerLayoutRules.MoveCardToNewPanel(layout, "b", 0);
        Normalize(layout);

        Assert.Equal(before, CardDockerLayoutRules.Signature(layout));
    }

    [Fact]
    public void Setting_a_card_aside_appends_it_to_the_tray()
    {
        var layout = ThreeColumns();

        CardDockerLayoutRules.MoveCardToTray(layout, "c");
        Normalize(layout);

        Assert.Equal(["f", "c"], layout.TrayCardIds);
        Assert.DoesNotContain(layout.Panels, p => p.Id == "p3");
    }

    // ---- Panel moves ----

    [Fact]
    public void Moving_a_panel_within_its_column_reorders_it()
    {
        var layout = ThreeColumns();

        CardDockerLayoutRules.MovePanel(layout, "p1", 0, 2);
        Normalize(layout);

        Assert.Equal("p2,p1 / p3 / p4,p5", Columns(layout, 3));
    }

    [Fact]
    public void Moving_a_panel_to_its_own_slot_is_a_no_op()
    {
        var layout = ThreeColumns();
        Normalize(layout);
        var before = CardDockerLayoutRules.Signature(layout);

        CardDockerLayoutRules.MovePanel(layout, "p4", 2, 0);
        CardDockerLayoutRules.MovePanel(layout, "p4", 2, 1);
        Normalize(layout);

        Assert.Equal(before, CardDockerLayoutRules.Signature(layout));
    }

    [Fact]
    public void Moving_a_panel_across_columns_inserts_it_at_the_slot()
    {
        var layout = ThreeColumns();

        CardDockerLayoutRules.MovePanel(layout, "p5", 0, 1);
        Normalize(layout);

        Assert.Equal("p1,p5,p2 / p3 / p4", Columns(layout, 3));
    }

    [Fact]
    public void Moving_a_parked_panel_forgets_its_preferred_column()
    {
        var layout = ThreeColumns();
        Normalize(layout, 1);

        CardDockerLayoutRules.MovePanel(layout, "p5", 0, 0);
        Normalize(layout, 3);

        var p5 = layout.Panels.Single(p => p.Id == "p5");
        Assert.Null(p5.PreferredColumn);
        Assert.Equal(0, p5.Column);
        Assert.Equal("p5,p1,p2 / p3 / p4", Columns(layout, 3));
    }

    // ---- Signature ----

    [Fact]
    public void Signature_ignores_panel_list_order()
    {
        var a = ThreeColumns();
        var b = ThreeColumns();
        b.Panels.Reverse();

        Normalize(a);
        Normalize(b);

        Assert.Equal(CardDockerLayoutRules.Signature(a), CardDockerLayoutRules.Signature(b));
    }

    [Fact]
    public void Signature_sees_a_height_change()
    {
        var layout = ThreeColumns();
        Normalize(layout);
        var before = CardDockerLayoutRules.Signature(layout);

        layout.Panels[0].MinHeight += 10;

        Assert.NotEqual(before, CardDockerLayoutRules.Signature(layout));
    }
}
