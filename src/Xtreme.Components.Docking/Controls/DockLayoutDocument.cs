using System.Globalization;

namespace Xtreme.Components.Docking.Controls;

/// <summary>
/// One item's entry in a saved layout document. Pure data: the host fills it from a
/// <see cref="DockItem"/> on save and applies it back on restore.
/// </summary>
/// <remarks>
/// Fields added by later document versions are nullable (or false) when the record was read from an
/// older document, so a restore can tell "not stored" from "stored as the default".
/// </remarks>
internal sealed record DockLayoutRecord(string DockId)
{
    /// <summary>The document version the record was read from (or <see cref="DockLayoutDocument.CurrentVersion"/>).</summary>
    public int Version { get; init; } = DockLayoutDocument.CurrentVersion;

    /// <summary>A single edge, or null when the stored value was not one.</summary>
    public DockEdge? Edge { get; init; }

    public double? Length { get; init; }

    public bool IsOpen { get; init; } = true;

    // v2
    public string? TabGroup { get; init; }
    public bool? IsPinned { get; init; }

    // v3: between-layer weight (0 or null = none stored).
    public double? SlotWeight { get; init; }

    // v4
    public DockBand? Band { get; init; }

    // v5
    public string? LayerGroup { get; init; }

    // v6
    public bool IsFloating { get; init; }

    /// <summary>The floating window's frame in physical screen pixels, when one was recorded.</summary>
    public DockWindowBounds? FloatBounds { get; init; }

    /// <summary>Whether this item was the selected tab of its tab group.</summary>
    public bool IsSelectedTab { get; init; }

    /// <summary>Weight of this item's slot among the side-by-side slots of its layer (0 or null = none).</summary>
    public double? ColumnWeight { get; init; }
}

/// <summary>A window frame in physical screen pixels.</summary>
internal readonly record struct DockWindowBounds(int X, int Y, int Width, int Height);

/// <summary>
/// Reads and writes the layout document <see cref="DockHost"/> stores through
/// <see cref="IDockPersistence"/>.
/// </summary>
/// <remarks>
/// <para>A hand-rolled delimited format rather than System.Text.Json, deliberately: reflection-based
/// serialization is exactly what the WASM publish trimmer strips, and a layout document that silently
/// fails to round-trip only on a published build is a bad trade for a few fields per item.</para>
/// <para>Records are separated by <c>;</c> and written in item order (record order IS the persisted
/// slot and layer order). Fields are separated by <c>|</c>. The version is implied by the field
/// count:</para>
/// <list type="table">
/// <item><term>v1 (4)</term><description>dockId, edge, length, isOpen</description></item>
/// <item><term>v2 (6)</term><description>+ tabGroup, isPinned</description></item>
/// <item><term>v3 (7)</term><description>+ slotWeight (between-layer ratio)</description></item>
/// <item><term>v4 (8)</term><description>+ band</description></item>
/// <item><term>v5 (9)</term><description>+ layerGroup</description></item>
/// <item><term>v6 (16)</term><description>+ isFloating, floatX, floatY, floatWidth, floatHeight,
/// selectedTab, columnWeight (side-by-side ratio inside a layer)</description></item>
/// </list>
/// <para>Records with more fields than the current version are read for the fields this version
/// knows, so a document written by a later version degrades instead of being dropped.</para>
/// </remarks>
internal static class DockLayoutDocument
{
    public const int CurrentVersion = 6;

    private const char RecordSeparator = ';';
    private const char FieldSeparator = '|';
    private const int CurrentFieldCount = 16;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Serialize(IEnumerable<DockLayoutRecord> records)
        => string.Join(RecordSeparator, records
            .Where(r => !string.IsNullOrWhiteSpace(r.DockId))
            .Select(r => string.Join(FieldSeparator,
                Sanitize(r.DockId),
                r.Edge is { } edge ? ((int)edge).ToString(Invariant) : string.Empty,
                (r.Length ?? 0).ToString("F0", Invariant),
                Flag(r.IsOpen),
                Sanitize(r.TabGroup),
                Flag(r.IsPinned ?? true),
                (r.SlotWeight ?? 0).ToString("F0", Invariant),
                ((int)(r.Band ?? DockBand.Outer)).ToString(Invariant),
                Sanitize(r.LayerGroup),
                Flag(r.IsFloating),
                r.FloatBounds?.X.ToString(Invariant) ?? string.Empty,
                r.FloatBounds?.Y.ToString(Invariant) ?? string.Empty,
                r.FloatBounds?.Width.ToString(Invariant) ?? string.Empty,
                r.FloatBounds?.Height.ToString(Invariant) ?? string.Empty,
                Flag(r.IsSelectedTab),
                (r.ColumnWeight ?? 0).ToString("F0", Invariant))));

    /// <summary>
    /// Parses a document. Malformed records (an unknown field count, an empty id) are skipped rather
    /// than failing the whole document; unreadable fields fall back to "not stored".
    /// </summary>
    public static IReadOnlyList<DockLayoutRecord> Parse(string? document)
    {
        var result = new List<DockLayoutRecord>();
        if (string.IsNullOrWhiteSpace(document)) return result;

        foreach (var raw in document.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var f = raw.Split(FieldSeparator);
            var version = VersionFor(f.Length);
            if (version == 0 || string.IsNullOrWhiteSpace(f[0])) continue;

            var record = new DockLayoutRecord(f[0])
            {
                Version = version,
                Edge = ParseEdge(f[1]),
                Length = ParseDouble(f[2]),
                IsOpen = f[3] == "1",
            };

            if (version >= 2)
            {
                record = record with
                {
                    TabGroup = NullIfEmpty(f[4]),
                    IsPinned = f[5] == "1",
                };
            }

            if (version >= 3)
                record = record with { SlotWeight = ParseDouble(f[6]) is > 0 and var w ? w : null };

            if (version >= 4 && int.TryParse(f[7], NumberStyles.Integer, Invariant, out var band)
                && Enum.IsDefined(typeof(DockBand), band))
            {
                record = record with { Band = (DockBand)band };
            }

            if (version >= 5)
                record = record with { LayerGroup = NullIfEmpty(f[8]) };

            if (version >= 6)
            {
                DockWindowBounds? bounds = null;
                if (int.TryParse(f[10], NumberStyles.Integer, Invariant, out var x)
                    && int.TryParse(f[11], NumberStyles.Integer, Invariant, out var y)
                    && int.TryParse(f[12], NumberStyles.Integer, Invariant, out var width)
                    && int.TryParse(f[13], NumberStyles.Integer, Invariant, out var height)
                    && width > 0 && height > 0)
                {
                    bounds = new DockWindowBounds(x, y, width, height);
                }

                record = record with
                {
                    IsFloating = f[9] == "1",
                    FloatBounds = bounds,
                    IsSelectedTab = f[14] == "1",
                    ColumnWeight = ParseDouble(f[15]) is > 0 and var c ? c : null,
                };
            }

            result.Add(record);
        }

        return result;
    }

    private static int VersionFor(int fieldCount) => fieldCount switch
    {
        4 => 1,
        6 => 2,
        7 => 3,
        8 => 4,
        9 => 5,
        >= CurrentFieldCount => 6,
        _ => 0,
    };

    private static DockEdge? ParseEdge(string value)
        => int.TryParse(value, NumberStyles.Integer, Invariant, out var edge)
            && (DockEdge)edge is DockEdge.Left or DockEdge.Right or DockEdge.Top or DockEdge.Bottom
                ? (DockEdge)edge
                : null;

    private static double? ParseDouble(string value)
        => double.TryParse(value, NumberStyles.Float, Invariant, out var d) && double.IsFinite(d) ? d : null;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static string Flag(bool value) => value ? "1" : "0";

    /// <summary>A name carrying the separators would shear the document.</summary>
    private static string Sanitize(string? value)
        => (value ?? string.Empty).Replace(FieldSeparator, '_').Replace(RecordSeparator, '_');
}
