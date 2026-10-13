using Microsoft.UI.Xaml;

namespace Xtreme.Components.Docking;

/// <summary>
/// The per-platform capabilities the dock manager needs but cannot determine for itself.
/// </summary>
/// <remarks>
/// <para>Xtreme.Components is a SINGLE <c>net10.0</c> assembly on purpose — platform TFMs would drag a
/// platform runtime into the library and collide with the head's Skia runtime (UNOB0017). That means
/// there is no <c>#if WINDOWS</c> available here, so anything platform-specific has to be a RUNTIME
/// decision behind this interface. The consuming head supplies the implementation.</para>
/// <para>The dock manager must run on WebAssembly and mobile, where tearing a pane out into its own OS
/// window is not possible. Everything else — docking, grouping, splitters, responsive collapse,
/// persistence — works everywhere. So "can this float?" is a question asked of the platform, not a
/// compile-time constant.</para>
/// <para><b>Window lifecycle is host policy too.</b> Measured on Uno Skia desktop: construction can
/// cost seconds of UI-thread freeze on a bad display driver, close is ~18ms, and
/// <c>AppWindow.Hide()</c> RETURNS SUCCESSFULLY WITHOUT HIDING — so whether a freed window can be pooled for reuse, and where the
/// construction freeze may land, are platform judgments the library cannot make. The acquire/release
/// pair lets a host pool windows (with a hide it has VERIFIED works); the library's fallback is
/// construct-on-demand and the cheap close.</para>
/// </remarks>
public interface IDockPlatformServices
{
    /// <summary>
    /// Whether this platform can host a floated pane in a separate OS window. When false,
    /// <see cref="Controls.DockItem.AllowFloat"/> is ignored and no float affordance is shown.
    /// </summary>
    bool SupportsFloatingWindows { get; }

    /// <summary>
    /// The window hosting the dock manager, when the host cares to say. Used to convert in-host
    /// coordinates to screen coordinates — e.g. placing a torn-out pane's window at the cursor.
    /// Null (the default) degrades gracefully: the float still happens, just at the OS default spot.
    /// </summary>
    Window? HostWindow => null;

    /// <summary>
    /// Returns a ready-to-use window for a float, or null when the host has none to offer — the dock
    /// manager then constructs one itself, paying the platform's construction cost under the gesture.
    /// </summary>
    Window? TryAcquireFloatWindow() => null;

    /// <summary>
    /// Offers a freed window (content already detached) back to the host for reuse. Return true only if
    /// the host has taken ownership AND the window is genuinely off-screen — verify, don't trust
    /// <c>AppWindow.Hide()</c>'s return. Returning false hands it back to the dock manager to close.
    /// </summary>
    bool TryReleaseFloatWindow(Window window) => false;

    /// <summary>
    /// Starts watching a floating window for the OS title-bar drag, so the dock manager can light its
    /// docking guides and re-dock the pane on drop. <paramref name="dragMoved"/> fires repeatedly with
    /// the cursor in SCREEN coordinates while the user drags the window with the primary button held;
    /// <paramref name="dropped"/> fires once when that button is released after such a drag. Callbacks
    /// MUST arrive on the host's UI thread. Dispose the returned handle to stop watching. Null (the
    /// default) means the platform cannot track OS window drags — the drag-back-to-redock gesture is
    /// then unavailable, and a floating pane's window ✕ (close the pane) is its only way home.
    /// </summary>
    /// <remarks>
    /// Platform territory for the same reason as the pool: whether the primary button is still down
    /// during an OS window move, and where the cursor is, take per-OS interop (or windowing events
    /// that may or may not exist) that this single-TFM library cannot contain. One behavioral
    /// contract implementations must honor: do NOT report the gesture already in flight when the
    /// window spawns — a tear-out floats the pane while the user's button is STILL DOWN, and
    /// reporting that same gesture would re-dock the pane the instant it floated. Require a fully
    /// released pointer before the first report.
    /// </remarks>
    IDisposable? TrackFloatingWindowDrag(
        Window window,
        Action<Windows.Graphics.PointInt32> dragMoved,
        Action<Windows.Graphics.PointInt32> dropped) => null;

    /// <summary>
    /// A hint that floating is likely to be used soon — the dock manager calls this when it hosts an
    /// item with <see cref="Controls.DockItem.AllowFloat"/> on a float-capable platform. A pooling
    /// host warms a spare window during idle here, so the first tear-out doesn't pay the window
    /// construction cost under the user's gesture. Must be safe to call repeatedly.
    /// Nothing happens by default.
    /// </summary>
    void WarmFloatSupport(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        // Default: no pool, nothing to warm.
    }

    /// <summary>
    /// Hands a just-spawned window to the OS as if the user had grabbed its title bar, so a tear-out
    /// CONTINUES under the still-held button instead of ending where the pane left the host. Returns
    /// true if the platform took over the drag. False (the default) means it could not, and the pane
    /// simply lands where it was torn out — the user re-grabs the window to move it.
    /// </summary>
    /// <remarks>
    /// <para>Called only while the primary button is genuinely down, immediately after the float window
    /// is presented. The intended shape is DevExpress/Visual Studio's: drag a header out, the window
    /// follows the cursor, and it lands where the button is RELEASED — including back over the host to
    /// re-dock.</para>
    /// <para><b>Implementations that return true must also arm their
    /// <see cref="TrackFloatingWindowDrag"/> tracker immediately for this window</b>, bypassing the
    /// "wait for a fully released pointer" rule that method documents. That rule exists to stop the
    /// spawning gesture from being read as a drop; here the spawning gesture IS the drag, and the
    /// window is outside the host at this moment, so there is nothing under it to drop onto yet.</para>
    /// <para><b>Do not implement this by handing the drag to a native modal move loop</b> (on Win32,
    /// <c>WM_NCLBUTTONDOWN</c>/<c>HTCAPTION</c>). It was tried: the loop runs inside the window's own
    /// message pump, stalls the dispatcher this interface's callbacks depend on, and traps the pointer
    /// until the user Alt-Tabs. Move the window from the tracker's own polling instead.</para>
    /// </remarks>
    bool BeginWindowDrag(Window window) => false;

    /// <summary>
    /// Called once for each floating window the dock manager spawns, after it is activated. The host
    /// applies its window-management policy here — nothing happens by default.
    /// </summary>
    /// <remarks>
    /// The motivating case: an unowned spawned window and the main window are unrelated top-levels
    /// to the OS, so activating one leaves the other buried. The fix is host territory — establishing a
    /// Win32 owner relationship, or hooking activation to raise the group — because it needs the main
    /// window's handle and per-OS interop this single-TFM library deliberately cannot contain. Note the
    /// trade-off before choosing owner semantics: an OWNED window can never go behind its owner.
    /// </remarks>
    void OnWindowSpawned(Window window)
    {
        // Default interface implementation: no policy. Hosts that want group activation override.
    }
}

/// <summary>
/// Conservative fallback used when the host supplies no <see cref="IDockPlatformServices"/>: reports no
/// multi-window support, so the dock manager degrades to docked-only behavior rather than attempting a
/// float the platform may not survive. A head that wants floating opts in explicitly.
/// </summary>
public sealed class DockedOnlyPlatformServices : IDockPlatformServices
{
    public static readonly DockedOnlyPlatformServices Instance = new();

    private DockedOnlyPlatformServices() { }

    public bool SupportsFloatingWindows => false;
}
