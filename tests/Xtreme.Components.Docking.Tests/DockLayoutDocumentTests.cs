using Xtreme.Components.Docking;
using Xtreme.Components.Docking.Controls;

namespace Xtreme.Components.Docking.Tests;

public class DockLayoutDocumentTests
{
    [Fact]
    public void Current_version_round_trips_every_field()
    {
        var original = new DockLayoutRecord("map")
        {
            Edge = DockEdge.Right,
            Length = 480,
            IsOpen = true,
            TabGroup = "right-tools",
            IsPinned = false,
            SlotWeight = 312,
            Band = DockBand.Inner,
            LayerGroup = "layer-editor",
            IsFloating = true,
            FloatBounds = new DockWindowBounds(-1600, 120, 800, 600),
            IsSelectedTab = true,
            ColumnWeight = 245,
        };

        var parsed = Assert.Single(DockLayoutDocument.Parse(DockLayoutDocument.Serialize([original])));

        Assert.Equal(original, parsed);
        Assert.Equal(DockLayoutDocument.CurrentVersion, parsed.Version);
    }

    [Fact]
    public void Record_order_is_preserved()
    {
        var records = new[] { "c", "a", "b" }.Select(id => new DockLayoutRecord(id) { Edge = DockEdge.Left, Length = 200 });

        var ids = DockLayoutDocument.Parse(DockLayoutDocument.Serialize(records)).Select(r => r.DockId);

        Assert.Equal(["c", "a", "b"], ids);
    }

    [Fact]
    public void Unset_optional_fields_read_back_as_not_stored()
    {
        var document = DockLayoutDocument.Serialize([new DockLayoutRecord("output") { Edge = DockEdge.Bottom, Length = 110 }]);

        var parsed = Assert.Single(DockLayoutDocument.Parse(document));

        Assert.Null(parsed.TabGroup);
        Assert.Null(parsed.LayerGroup);
        Assert.Null(parsed.SlotWeight);
        Assert.Null(parsed.ColumnWeight);
        Assert.Null(parsed.FloatBounds);
        Assert.False(parsed.IsFloating);
        Assert.False(parsed.IsSelectedTab);
    }

    [Fact]
    public void Separators_in_names_cannot_shear_the_document()
    {
        var document = DockLayoutDocument.Serialize(
        [
            new DockLayoutRecord("a") { Edge = DockEdge.Left, Length = 200, TabGroup = "x|y;z" },
            new DockLayoutRecord("b") { Edge = DockEdge.Left, Length = 200 },
        ]);

        var parsed = DockLayoutDocument.Parse(document);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("x_y_z", parsed[0].TabGroup);
        Assert.Equal("b", parsed[1].DockId);
    }

    [Theory]
    [InlineData("explorer|1|220|1", 1)]
    [InlineData("explorer|1|220|1|left-tools|0", 2)]
    [InlineData("explorer|1|220|1|left-tools|0|300", 3)]
    [InlineData("explorer|1|220|1|left-tools|0|300|1", 4)]
    [InlineData("explorer|1|220|1|left-tools|0|300|1|layer-a", 5)]
    public void Older_versions_are_still_read(string document, int version)
    {
        var parsed = Assert.Single(DockLayoutDocument.Parse(document));

        Assert.Equal(version, parsed.Version);
        Assert.Equal("explorer", parsed.DockId);
        Assert.Equal(DockEdge.Left, parsed.Edge);
        Assert.Equal(220, parsed.Length);
        Assert.True(parsed.IsOpen);

        Assert.Equal(version >= 2 ? "left-tools" : null, parsed.TabGroup);
        Assert.Equal(version >= 2 ? false : null, parsed.IsPinned);
        Assert.Equal(version >= 3 ? 300d : null, parsed.SlotWeight);
        Assert.Equal(version >= 4 ? DockBand.Inner : null, parsed.Band);
        Assert.Equal(version >= 5 ? "layer-a" : null, parsed.LayerGroup);

        // Fields only v6 knows are "not stored" for every older document.
        Assert.False(parsed.IsFloating);
        Assert.Null(parsed.FloatBounds);
        Assert.False(parsed.IsSelectedTab);
        Assert.Null(parsed.ColumnWeight);
    }

    [Fact]
    public void A_v5_document_written_by_the_previous_host_reads_unchanged()
    {
        // Two records exactly as the version-5 serializer wrote them.
        const string v5 = "filters|1|220|1|left-tools|1|0|0|;props|2|280|0||1|412|0|right-layer";

        var parsed = DockLayoutDocument.Parse(v5);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(5, parsed[0].Version);
        Assert.Null(parsed[0].SlotWeight); // 0 = none stored
        Assert.Null(parsed[0].LayerGroup);
        Assert.Equal(DockEdge.Right, parsed[1].Edge);
        Assert.False(parsed[1].IsOpen);
        Assert.Null(parsed[1].TabGroup);
        Assert.Equal(412, parsed[1].SlotWeight);
        Assert.Equal("right-layer", parsed[1].LayerGroup);
    }

    [Fact]
    public void A_later_version_with_extra_fields_degrades_to_the_known_ones()
    {
        var current = DockLayoutDocument.Serialize([new DockLayoutRecord("map") { Edge = DockEdge.Top, Length = 90, IsFloating = true }]);

        var parsed = Assert.Single(DockLayoutDocument.Parse(current + "|future|fields"));

        Assert.Equal(DockEdge.Top, parsed.Edge);
        Assert.True(parsed.IsFloating);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("a|1|2")]          // too few fields
    [InlineData("a|1|2|1|g")]      // a field count no version used
    [InlineData("|1|220|1")]       // empty id
    public void Malformed_records_are_skipped(string document)
        => Assert.Empty(DockLayoutDocument.Parse(document));

    [Fact]
    public void Malformed_records_do_not_take_good_ones_down_with_them()
    {
        var parsed = DockLayoutDocument.Parse("bad|record;explorer|1|220|1");

        Assert.Equal("explorer", Assert.Single(parsed).DockId);
    }

    [Theory]
    [InlineData("0")]   // None
    [InlineData("3")]   // Left | Right: a set, not an edge
    [InlineData("15")]  // All
    [InlineData("x")]
    public void An_edge_value_that_is_not_a_single_edge_is_not_stored(string edge)
    {
        var parsed = Assert.Single(DockLayoutDocument.Parse($"explorer|{edge}|220|1"));

        Assert.Null(parsed.Edge);
    }

    [Fact]
    public void Floating_bounds_need_a_positive_size()
    {
        var record = new DockLayoutRecord("map") { IsFloating = true, FloatBounds = new DockWindowBounds(10, 10, 0, 300) };

        var parsed = Assert.Single(DockLayoutDocument.Parse(DockLayoutDocument.Serialize([record])));

        Assert.True(parsed.IsFloating);
        Assert.Null(parsed.FloatBounds);
    }

    [Fact]
    public void Lengths_are_written_culture_invariant()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var document = DockLayoutDocument.Serialize([new DockLayoutRecord("a") { Edge = DockEdge.Left, Length = 1234.6 }]);

            Assert.Equal(1235, Assert.Single(DockLayoutDocument.Parse(document)).Length);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Items_without_an_id_are_not_written()
        => Assert.Equal(string.Empty, DockLayoutDocument.Serialize([new DockLayoutRecord(" ") { Edge = DockEdge.Left }]));
}
