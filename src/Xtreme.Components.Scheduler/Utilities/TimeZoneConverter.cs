namespace Xtreme.Components.Scheduler.Utilities;

/// <summary>
/// Converts UTC instants to a resource's local wall-clock by IANA timezone id. .NET 6+ resolves
/// IANA ids cross-platform via ICU; on a Windows host without ICU it falls back to the
/// IANA→Windows id mapping. An unknown/empty id returns the input unchanged (render as-is).
/// </summary>
public static class TimeZoneConverter
{
    private static readonly Dictionary<string, TimeZoneInfo?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Convert a UTC <paramref name="utc"/> instant to the wall-clock time in
    /// <paramref name="ianaId"/>. Null/blank/unknown id → <paramref name="utc"/> unchanged.
    /// </summary>
    public static DateTime ToZone(DateTime utc, string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            return utc;
        }

        var tz = Resolve(ianaId);
        if (tz is null)
        {
            return utc;
        }

        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
    }

    /// <summary>
    /// Inverse of <see cref="ToZone"/>: convert a wall-clock time in <paramref name="ianaId"/>
    /// back to a UTC instant. Used when a Planner gesture in a per-resource-timezone lane produces
    /// a new wall-clock time that must be stored in the appointment's UTC convention.
    /// Null/blank/unknown id → <paramref name="zoned"/> unchanged.
    /// </summary>
    public static DateTime FromZone(DateTime zoned, string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            return zoned;
        }

        var tz = Resolve(ianaId);
        if (tz is null)
        {
            return zoned;
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(zoned, DateTimeKind.Unspecified), tz);
    }

    /// <summary>A short display abbreviation for the zone (the IANA id's last segment), or null.</summary>
    public static string? ShortLabel(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            return null;
        }
        var slash = ianaId.LastIndexOf('/');
        return (slash >= 0 ? ianaId[(slash + 1)..] : ianaId).Replace('_', ' ');
    }

    private static TimeZoneInfo? Resolve(string ianaId)
    {
        if (Cache.TryGetValue(ianaId, out var cached))
        {
            return cached;
        }

        TimeZoneInfo? tz = null;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows without ICU: map the IANA id to a Windows id and retry.
            try
            {
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out var windowsId) && windowsId is not null)
                {
                    tz = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                }
            }
            catch (TimeZoneNotFoundException)
            {
                tz = null;
            }
        }
        catch (InvalidTimeZoneException)
        {
            tz = null;
        }

        Cache[ianaId] = tz;
        return tz;
    }
}
