namespace Xtreme.Components.Docking;

/// <summary>
/// How a <see cref="Controls.DockHost"/> saves and restores its layout. Implemented by the HOST app.
/// </summary>
/// <remarks>
/// <para>Xtreme.Components never reaches into app services — no <c>ServiceLocator</c>, no
/// <c>IUiPreferenceService</c>, no <c>ProjectReference</c> to the host app. The library must stay
/// consumable on its own. So persistence is a contract the host fills: an app can back
/// this with its own preference store (per-user vs device-scoped keys), while a sample or test host can
/// back it with an in-memory dictionary or nothing at all.</para>
/// <para>Deliberately a WHOLE-LAYOUT document keyed by <see cref="Controls.DockHost.PersistenceKey"/>,
/// not one entry per pane. A dock layout is a tree — which edge each item is on, how items are grouped
/// and ordered within an edge, every splitter position, and later each floating window's bounds. Those
/// facts are not independent, so saving them independently cannot round-trip a layout.</para>
/// </remarks>
public interface IDockPersistence
{
    /// <summary>
    /// Returns the previously saved layout document for <paramref name="key"/>, or null when there is
    /// none (first run, or the host has no store). Null means "use the layout as authored in XAML".
    /// </summary>
    Task<string?> LoadAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Persists <paramref name="document"/> against <paramref name="key"/>. Implementations should treat
    /// this as best-effort: a failure to save a UI preference must never surface as an error to the user
    /// or throw into a layout pass.
    /// </summary>
    Task SaveAsync(string key, string document, CancellationToken ct = default);
}
