using Microsoft.UI.Xaml;

namespace Xtreme.Components.Docking.Platform;

/// <summary>
/// The real head's window policy for the dock manager (XU-446): float support detection plus a
/// small pool of spare windows, so a tear-out never pays construction cost under the user's click.
/// Ported from the Harness reference implementation, which remains the proving ground.
/// </summary>
/// <remarks>
/// <para>One class serves every head TFM: the platform facts are RUNTIME decisions
/// (<see cref="SupportsFloatingWindows"/> is false on WASM/mobile, the user32 interop is guarded by
/// <c>OperatingSystem.IsWindows()</c>), which is exactly the shape <see cref="IDockPlatformServices"/>
/// exists to allow — Xtreme.Components is a single net10.0 assembly with no <c>#if WINDOWS</c>.</para>
/// <para>The pool's rules were all bought with observed failures in the Harness:</para>
/// <list type="bullet">
/// <item><b>Verify the hide, don't trust it.</b> <c>AppWindow.Hide()</c> returns successfully WITHOUT
/// hiding on Uno Skia desktop; trusting it pooled a visible blank window.</item>
/// <item><b>Own liveness.</b> That visible blank window got closed by the user, and the next float
/// activated the corpse and threw. Pooled windows are watched via <c>Closed</c> and evicted.</item>
/// <item><b>Never construct over a user gesture.</b> On a healthy machine <c>new Window()</c> costs
/// ~12ms, but it goes through GL context creation, and a bad display driver can turn that into
/// SECONDS with the UI thread blocked — clicks then land inside the freeze (the "slow dock-back").
/// See <see cref="PoolTarget"/>. Seeding therefore waits for a genuinely idle moment.</item>
/// </list>
/// </remarks>
public sealed class Win32DockPlatformServices : IDockPlatformServices
{
    /// <summary>Optional diagnostics sink ("[DockPerf] ..." / "[DockWin] ..." lines). Null = silent.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>
    /// How many spare windows to keep, so a tear-out never constructs under the user's click.
    /// <para>
    /// <b>Why a pool exists at all (XU-450).</b> <c>new Window()</c> goes through OpenGL context
    /// creation on Win32, which is ~12ms on a healthy driver and pathological on a bad one. On the
    /// dev box it measured <b>3.6 SECONDS</b>, every time, with the UI thread blocked (3608ms wall
    /// against 406ms CPU) — reproduced on a blank Uno head, so neither this app nor Uno was at
    /// fault. Bisecting by Skia backend found it: opengl 3608ms, software 17ms, vulkan 716ms. The
    /// cause was an 18-month-old Intel Arc driver (32.0.101.6559); updating to 32.0.101.8974 took
    /// the same measurement to <b>12ms</b> and removed the pathology entirely.
    /// </para>
    /// <para>
    /// The pool is kept anyway, as insurance: users run whatever driver they run, and we cannot
    /// audit their machines. Filling three costs ~76ms on a healthy driver, so it is nearly free
    /// when it is not needed and covers several seconds of freeze when it is. Three is simply how
    /// many panes can float at once before the pool empties — the dock manager's realistic
    /// simultaneous floats, with room to spare.
    /// </para>
    /// </summary>
    private const int PoolTarget = 3;

    private readonly List<Window> _pool = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _seedTimer;

    public bool SupportsFloatingWindows { get; } =
        !OperatingSystem.IsBrowser() && !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();

    /// <summary>Lets the dock manager place a torn-out pane's window at the cursor.</summary>
    /// <summary>The app's main window; the owner of every floating window. Set it once after the window exists.</summary>
    public Window? HostWindow { get; set; }

    /// <summary>The dock manager's warm-up hint (an AllowFloat item is hosted) — top the pool up.</summary>
    public void WarmFloatSupport(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
        => SeedPoolAtIdle(dispatcher);

    /// <summary>
    /// Prime the pool AFTER launch has landed, in the first quiet moment the user gives us. Called
    /// from <c>App.OnLaunched</c>, which returns immediately — startup itself stays unencumbered.
    /// <para>
    /// Priming synchronously inside <c>OnLaunched</c> was tried first and added its whole cost to
    /// launch (3717ms on the bad driver that prompted this work). Deferring spends the same cost in
    /// a gap the user chose, which is strictly better and matters only on a machine where that cost
    /// is large — see <see cref="PoolTarget"/>.
    /// </para>
    /// </summary>
    public void PrimePoolWhenIdle(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
        => SeedPoolAtIdle(dispatcher, delayMs: LandingDelayMs);

    /// <summary>How long after launch to start LOOKING for a quiet moment. Not itself the guarantee —
    /// <see cref="UserIsQuiet"/> is — just enough that we aren't polling through the login screen's
    /// own startup work.</summary>
    private const int LandingDelayMs = 2500;

    /// <summary>How long the user must have been completely idle before we dare block the UI thread.
    /// Covers the case the button check alone misses: typing. On a machine where construction is
    /// seconds rather than milliseconds, a freeze mid-password is worse than the one this whole
    /// exercise removes.</summary>
    private const int QuietPeriodMs = 1200;

    /// <summary>How often to re-check once we are waiting for that gap.</summary>
    private const int QuietRetryMs = 750;

    /// <summary>
    /// Construct every window the pool is missing, synchronously and WITHOUT yielding — the burst is
    /// the whole point. Where GL context creation is pathologically slow the cost is charged per
    /// dispatcher work item rather than per window (measured: 3618ms for the first, ~46ms for each
    /// of the next seven), so yielding between constructions would re-arm it every time. On a
    /// healthy driver this is simply the cheapest way to do it.
    /// </summary>
    private int FillPool()
    {
        var created = 0;
        while (_pool.Count < PoolTarget)
        {
            var window = new Window();
            window.Closed += OnPooledWindowClosed;
            _pool.Add(window);
            created++;
        }

        return created;
    }

    public Window? TryAcquireFloatWindow()
    {
        if (_pool.Count == 0) return null;

        var window = _pool[^1];
        _pool.RemoveAt(_pool.Count - 1);
        window.Closed -= OnPooledWindowClosed; // it's the dock manager's window again now

        Log?.Invoke($"[DockPerf] pool: acquired ({_pool.Count} left)");
        return window;
    }

    public bool TryReleaseFloatWindow(Window window)
    {
        try
        {
            // Preferred: a real hide. Observed to silently no-op on Uno Skia desktop (XU-450), so
            // VERIFY via IsVisible rather than trusting the call.
            window.AppWindow.Hide();
            if (!window.AppWindow.IsVisible)
            {
                AddToPool(window);
                Log?.Invoke($"[DockPerf] pool: released, verified hidden ({_pool.Count} pooled)");
                return true;
            }

            // Hide didn't hide. Parking off-screen instead leaves a taskbar/alt-tab ghost
            // (IsShownInSwitchers is unimplemented on this backend), which users find and close.
            // Decline instead: the library's close is measured-cheap (18ms), and re-seeding at idle
            // rebuilds the warm pool without ever constructing over a user gesture.
            Log?.Invoke("[DockPerf] pool: Hide() no-oped — declining close; re-seeding at idle");
            SeedPoolAtIdle(window.DispatcherQueue, delayMs: 2500);
            return false;
        }
        catch
        {
            return false; // window already dead, or the windowing APIs unavailable — library closes it
        }
    }

    private void AddToPool(Window window)
    {
        window.Closed += OnPooledWindowClosed;
        _pool.Add(window);
    }

    /// <summary>
    /// Refills the pool on a timer so the construction freeze lands during idle rather than under a
    /// user gesture. A window constructed here has never been activated, so it is invisible by
    /// nature and needs no hide.
    /// <para>
    /// The refill is a BURST back up to <see cref="PoolTarget"/>, not a single window: since the
    /// ~3.6s is charged per work item rather than per window (XU-450), refilling one at a time on
    /// separate ticks would pay it once per window — the worst possible schedule. One freeze buys
    /// the whole pool.
    /// </para>
    /// </summary>
    private void SeedPoolAtIdle(Microsoft.UI.Dispatching.DispatcherQueue queue, int delayMs = 600)
    {
        if (_pool.Count >= PoolTarget || _seedTimer is not null || !SupportsFloatingWindows) return;

        _seedTimer = queue.CreateTimer();
        _seedTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _seedTimer.IsRepeating = false;
        _seedTimer.Tick += (timer, _) =>
        {
            if (_pool.Count >= PoolTarget)
            {
                _seedTimer = null;
                return;
            }

            // Never land the ~3.6s freeze on top of the user. Keep re-checking until they hand us a
            // gap; the poll is two user32 reads, so waiting indefinitely is cheaper than guessing.
            if (!UserIsQuiet())
            {
                timer.Interval = TimeSpan.FromMilliseconds(QuietRetryMs);
                timer.Start();
                return;
            }

            _seedTimer = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var created = FillPool();
            Log?.Invoke($"[DockPerf] pool: filled {created} window(s) in a quiet moment = {sw.ElapsedMilliseconds}ms");
        };
        _seedTimer.Start();
    }

    /// <summary>
    /// Is the user not touching anything right now? Two conditions: the primary button is up (not
    /// mid-click or mid-drag), and there has been no keyboard or mouse input at all for
    /// <see cref="QuietPeriodMs"/>.
    /// </summary>
    /// <remarks>
    /// <c>GetLastInputInfo</c> is system-wide rather than per-process, which errs the safe way: it
    /// also holds off while the user is busy in another app, and a background freeze nobody sees
    /// costs us nothing. Non-Windows heads have no equivalent read here and simply proceed — the
    /// interop is the only Windows-specific part, not the pooling.
    /// </remarks>
    private static bool UserIsQuiet()
    {
        if (!OperatingSystem.IsWindows()) return true;

        return !NativeMethods.IsPrimaryButtonDown()
            && NativeMethods.MillisecondsSinceLastInput() >= QuietPeriodMs;
    }

    private void OnPooledWindowClosed(object sender, WindowEventArgs e)
    {
        // A pooled window died outside our control (user, OS, shutdown). Forget it, or the next
        // acquire hands the dock manager a corpse to activate.
        if (sender is Window window)
        {
            window.Closed -= OnPooledWindowClosed;
            _pool.Remove(window);
            Log?.Invoke($"[DockPerf] pool: pooled window closed externally — evicted ({_pool.Count} left)");
        }
    }

    /// <summary>
    /// Makes every spawned float window an OWNED window of the main shell window (XU-438), so the app's
    /// windows raise, minimise and restore as one group instead of a float being buried the moment the
    /// shell is clicked.
    /// </summary>
    /// <remarks>
    /// <para>Owner semantics were chosen deliberately over hooking activation: it is what Visual Studio
    /// and DevExpress do with floating tool windows, so it matches the model the rest of the dock
    /// manager was built against, and the OS does the grouping for free. <b>The accepted trade-off is
    /// that an owned window can never go behind its owner</b> — a floated pane always sits above the
    /// shell.</para>
    /// <para>Applied at PRESENT time rather than construction, which matters because of the pool: a
    /// pooled window is constructed long before anyone knows it will float, and an owner set on a
    /// window that is later released back to the pool simply stays set, which is harmless.</para>
    /// </remarks>
    public void OnWindowSpawned(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var owner = _shellWindowHandle;
            if (owner == IntPtr.Zero || !NativeMethods.IsWindowAlive(owner))
            {
                Log?.Invoke("[DockWin] spawn: shell handle unknown — window left unowned");
                return;
            }

            var owned = NativeMethods.TryGetHwnd(window);
            if (owned == IntPtr.Zero)
                owned = FindSpawnedWindow(window.Title, owner);
            if (owned == IntPtr.Zero || owned == owner)
            {
                // Owning a window to ITSELF hangs rather than failing, so an ambiguous match is
                // refused outright. The cost of refusing is only the grouping — the float itself
                // works exactly as it does today.
                Log?.Invoke($"[DockWin] spawn: no match for '{window.Title}' — window left unowned");
                return;
            }

            Log?.Invoke(NativeMethods.TrySetOwner(owned, owner)
                ? $"[DockWin] spawn: '{window.Title}' 0x{owned:X} owned by shell 0x{owner:X}"
                : $"[DockWin] spawn: SetWindowLongPtr refused (owned=0x{owned:X})");
        }
        catch
        {
            // Window management is garnish — never let it take down a float that otherwise works.
        }
    }

    /// <summary>
    /// Continues a tear-out under the still-held button: the window is carried by the drag tracker's
    /// own timer until the button is released (XU-461).
    /// </summary>
    /// <remarks>
    /// <para><b>The obvious Win32 route was tried first and is wrong on this backend.</b>
    /// <c>ReleaseCapture</c> + a posted <c>WM_NCLBUTTONDOWN</c>/<c>HTCAPTION</c> is the standard "treat
    /// this as a title-bar grab", and it did reach the right window — but the move loop it starts is
    /// MODAL and runs inside that window's own message pump. That stalls the dispatcher the tracker's
    /// timer ticks on, and the loop holds the mouse capture waiting for a button release it never sees:
    /// the pointer is trapped (the taskbar became unreachable) until Alt-Tab cancels the loop. Do not
    /// reintroduce it.</para>
    /// <para>Carrying the window ourselves has none of that: no modal loop, no capture, and it reuses
    /// the polling this tracker already does. The failure mode if it misbehaves is a window that
    /// doesn't follow — never a trapped pointer.</para>
    /// </remarks>
    public bool BeginWindowDrag(Window window)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            if (!NativeMethods.IsPrimaryButtonDown()) return false;  // gesture already over
            if (!NativeMethods.TryGetCursorPos(out var cursor)) return false;

            // Remember where in the window the cursor is, so it stays under the same spot for the whole
            // drag rather than snapping the window's corner to the pointer.
            var pos = window.AppWindow.Position;
            _carriedWindows[window] = new Windows.Graphics.PointInt32
            {
                X = cursor.X - pos.X,
                Y = cursor.Y - pos.Y,
            };

            return true;
        }
        catch
        {
            _carriedWindows.Remove(window);
            return false;
        }
    }

    /// <summary>
    /// Windows currently being carried by an in-progress tear-out, and the cursor's offset within each.
    /// Present only between the tear-out and the button release that ends it.
    /// </summary>
    private readonly Dictionary<Window, Windows.Graphics.PointInt32> _carriedWindows = new();

    /// <summary>
    /// Picks the HWND of the window that was just spawned, out of this process's visible Uno windows.
    /// </summary>
    /// <remarks>
    /// Two filters do the work, and neither depends on focus or z-order. <b>Already-owned windows are
    /// excluded</b> — a float we owned on an earlier spawn reports the shell as its owner, so the set
    /// of unowned windows is exactly {shell} ∪ {windows not yet adopted}. <b>Title then disambiguates</b>
    /// when several are unadopted at once. A tie is reported as no match rather than guessed at: the
    /// caller refuses, and the only loss is that one window doesn't join the group.
    /// </remarks>
    private static IntPtr FindSpawnedWindow(string? title, IntPtr shell)
    {
        var candidates = NativeMethods.EnumerateAppWindows()
            .Where(h => h != shell && NativeMethods.OwnerOf(h) == IntPtr.Zero)
            .ToList();

        if (candidates.Count == 0) return IntPtr.Zero;
        if (candidates.Count == 1) return candidates[0];

        if (!string.IsNullOrEmpty(title))
        {
            var byTitle = candidates
                .Where(h => string.Equals(NativeMethods.WindowTitle(h), title, StringComparison.Ordinal))
                .ToList();

            if (byTitle.Count == 1) return byTitle[0];
        }

        return IntPtr.Zero; // ambiguous — refuse rather than own the wrong window
    }

    private IntPtr _shellWindowHandle;

    /// <summary>
    /// Resolves and caches the shell's HWND at startup, while it is the process's ONLY visible Uno
    /// window — which is what makes the identification unambiguous. Every later spawn is then found by
    /// elimination against it. Safe to call repeatedly; does nothing once it has succeeded.
    /// </summary>
    /// <remarks>
    /// Polls rather than reading once, because <c>Window.Activate()</c> RETURNS BEFORE the native
    /// window exists on this backend — measured: an enumeration immediately after Activate finds ZERO
    /// windows. Waiting costs nothing (a float needs a user gesture, which is many seconds away) and
    /// removes a startup race that would otherwise silently disable the whole feature.
    /// </remarks>
    public void CaptureShellWindowHandle(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        if (!OperatingSystem.IsWindows() || _shellWindowHandle != IntPtr.Zero) return;

        var attempts = 0;

        // Held in a FIELD, not a local: a DispatcherQueueTimer with no strong reference is collected
        // before it ever ticks — which is exactly what happened here, and it fails silently (no
        // ticks, no error, the feature just never arms).
        _shellHandleTimer = dispatcher.CreateTimer();
        var timer = _shellHandleTimer;
        timer.Interval = TimeSpan.FromMilliseconds(ShellHandlePollMs);
        timer.IsRepeating = true;
        timer.Tick += (t, _) =>
        {
            attempts++;
            var windows = NativeMethods.EnumerateAppWindows();

            // Exactly one is the expected case at startup. If a second Uno window somehow exists this
            // early, fall back to matching the shell's own title rather than picking arbitrarily.
            if (windows.Count == 1)
            {
                _shellWindowHandle = windows[0];
            }
            else if (windows.Count > 1 && HostWindow?.Title is { Length: > 0 } shellTitle)
            {
                var matches = windows
                    .Where(h => string.Equals(NativeMethods.WindowTitle(h), shellTitle, StringComparison.Ordinal))
                    .ToList();

                if (matches.Count == 1) _shellWindowHandle = matches[0];
            }

            if (_shellWindowHandle != IntPtr.Zero)
            {
                t.Stop();
                _shellHandleTimer = null;
                Log?.Invoke($"[DockWin] shell handle = 0x{_shellWindowHandle:X} " +
                                  $"('{NativeMethods.WindowTitle(_shellWindowHandle)}', after {attempts} poll(s))");
            }
            else if (attempts >= ShellHandleMaxPolls)
            {
                t.Stop();
                _shellHandleTimer = null;
                // Floats still work; they just won't join the window group.
                Log?.Invoke($"[DockWin] shell handle unresolved after {attempts} polls — " +
                                  "spawned windows will be left unowned");
            }
        };
        timer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _shellHandleTimer;

    private const int ShellHandlePollMs = 250;
    private const int ShellHandleMaxPolls = 40; // 10s — generous; the window normally appears at once

    /// <summary>
    /// Watches a floating window for the OS title-bar drag by polling — the move loop is the OS's, so
    /// there is no Uno pointer event to subscribe to. A tick reads the window's position (did it move?)
    /// and the primary mouse button (is the user still holding it?); both together mean "this window is
    /// being dragged", and the button releasing afterwards is the drop.
    /// </summary>
    /// <remarks>
    /// The interface's one behavioural rule is enforced by <c>sawIdle</c>: a tear-out spawns the window
    /// UNDER a still-held button, and reporting that in-flight gesture would re-dock the pane the
    /// instant it floated. The tracker therefore refuses to arm until it has seen the button fully
    /// released once. Polling at 33ms keeps guide tracking at drag frame rates for negligible cost
    /// (two user32 reads and a struct compare per tick).
    /// </remarks>
    public IDisposable? TrackFloatingWindowDrag(
        Window window,
        Action<Windows.Graphics.PointInt32> dragMoved,
        Action<Windows.Graphics.PointInt32> dropped)
    {
        // The cursor/button reads below are user32 — other desktop OSes need their own variant, and
        // returning null simply leaves them without the drag-back gesture (window ✕ still works).
        if (!OperatingSystem.IsWindows()) return null;

        var timer = window.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.IsRepeating = true;

        var last = window.AppWindow.Position;
        var sawIdle = false;
        var dragging = false;
        var draggedHwnd = IntPtr.Zero;

        timer.Tick += (_, _) =>
        {
            try
            {
                var pos = window.AppWindow.Position;
                var moved = pos.X != last.X || pos.Y != last.Y;
                last = pos;

                var buttonDown = NativeMethods.IsPrimaryButtonDown();

                // XU-461: a tear-out still under the user's finger. Carry the window with the cursor and
                // report the drag, so the guides light and the release lands the pane where they let go.
                // This runs BEFORE the sawIdle gate deliberately — that gate exists to ignore the
                // spawning gesture, and here the spawning gesture is precisely what we are continuing.
                if (_carriedWindows.TryGetValue(window, out var grab))
                {
                    if (!NativeMethods.TryGetCursorPos(out var carried)) return;

                    window.AppWindow.Move(new Windows.Graphics.PointInt32
                    {
                        X = carried.X - grab.X,
                        Y = carried.Y - grab.Y,
                    });

                    if (!dragging)
                    {
                        dragging = true;
                        draggedHwnd = NativeMethods.TryGetHwnd(window) is var ownHwnd && ownHwnd != IntPtr.Zero ? ownHwnd : NativeMethods.TryGetForegroundWindowOfThisProcess();
                        NativeMethods.TrySetWindowOpacity(draggedHwnd, 0.30); // the DX drag look
                    }

                    if (buttonDown)
                    {
                        dragMoved(carried);
                    }
                    else
                    {
                        // Released: this is the drop. Hand the tracker back to its normal life, already
                        // idle-armed, so a later re-grab of this window behaves exactly as before.
                        _carriedWindows.Remove(window);
                        dragging = false;
                        sawIdle = true;
                        NativeMethods.TrySetWindowOpacity(draggedHwnd, 1.0);
                        draggedHwnd = IntPtr.Zero;
                        dropped(carried);
                    }

                    last = window.AppWindow.Position;
                    return;
                }

                if (!sawIdle)
                {
                    // Still inside the gesture that spawned the window (tear-out holds the button).
                    sawIdle = !buttonDown;
                    return;
                }

                if (!dragging)
                {
                    if (moved && buttonDown)
                    {
                        dragging = true;
                        // "70% transparent" while the panel rides the cursor — the DX drag look, and
                        // the guides underneath stay readable through the window. The HWND comes from
                        // the foreground window: during an OS title-bar drag the dragged window IS
                        // the foreground window (WindowId.Value is NOT the HWND on this backend).
                        draggedHwnd = NativeMethods.TryGetHwnd(window) is var ownHwnd && ownHwnd != IntPtr.Zero ? ownHwnd : NativeMethods.TryGetForegroundWindowOfThisProcess();
                        NativeMethods.TrySetWindowOpacity(draggedHwnd, 0.30);
                    }
                    else
                    {
                        return;
                    }
                }

                if (!NativeMethods.TryGetCursorPos(out var cursor)) return;

                if (buttonDown)
                {
                    dragMoved(cursor);
                }
                else
                {
                    dragging = false;
                    NativeMethods.TrySetWindowOpacity(draggedHwnd, 1.0);
                    draggedHwnd = IntPtr.Zero;
                    dropped(cursor);
                }
            }
            catch
            {
                // The window died mid-tick (AppWindow gone); the dock manager disposes us right after.
            }
        };

        timer.Start();

        // Disposal also restores opacity, so a tracker torn down mid-drag (host unloading, pane
        // re-docked) can never strand a see-through window.
        return new TimerHandle(timer, () => NativeMethods.TrySetWindowOpacity(draggedHwnd, 1.0));
    }

    private sealed class TimerHandle(Microsoft.UI.Dispatching.DispatcherQueueTimer timer, Action onDispose) : IDisposable
    {
        public void Dispose()
        {
            timer.Stop();
            try { onDispose(); } catch { /* the window may already be gone */ }
        }
    }

    private static class NativeMethods
    {
        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int SM_SWAPBUTTON = 23;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>The button that drags windows: VK_LBUTTON unless the user swapped buttons.</summary>
        public static bool IsPrimaryButtonDown()
        {
            var key = GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON;
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        /// <summary>
        /// Time since the last keyboard or mouse input anywhere on the desktop. Returns
        /// <see cref="uint.MaxValue"/> if the call fails — "very idle", so an unavailable API can
        /// never wedge the pool waiting for a quiet moment it will never be told about.
        /// </summary>
        public static uint MillisecondsSinceLastInput()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return uint.MaxValue;

            // Both are unsigned tick counts that wrap every ~49 days; unchecked subtraction gives
            // the right elapsed value across the wrap, where a signed compare would not.
            return unchecked((uint)Environment.TickCount - info.dwTime);
        }

        public static bool TryGetCursorPos(out Windows.Graphics.PointInt32 point)
        {
            if (GetCursorPos(out var native))
            {
                point = new Windows.Graphics.PointInt32 { X = native.X, Y = native.Y };
                return true;
            }

            point = default;
            return false;
        }

        private const int GWL_EXSTYLE = -20;
        private const int GWLP_HWNDPARENT = -8;
        private const long WS_EX_LAYERED = 0x80000;
        private const uint LWA_ALPHA = 0x2;
        private const uint GW_OWNER = 4;

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hwnd, char[] text, int count);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hwnd, char[] text, int count);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        /// <summary>
        /// The HWND of the window the user is currently interacting with, if it belongs to this
        /// process — which, at the moment an OS title-bar drag is detected, is the dragged window.
        /// </summary>
        /// <summary>
        /// The HWND of an Uno Skia window through <c>Uno.UI.Xaml.WindowHelper.GetNativeWindow</c>
        /// (whose result exposes <c>Hwnd</c> on Win32). Resolved by reflection so the library needs no
        /// compile-time dependency on an internal-ish API and degrades to <see cref="IntPtr.Zero"/>
        /// on runtimes that lack it; callers then fall back to the foreground-window heuristic.
        /// <c>AppWindow.Id.Value</c> is NOT the HWND on this backend.
        /// </summary>
        public static IntPtr TryGetHwnd(Window window)
        {
            try
            {
                var helperType = Type.GetType("Uno.UI.Xaml.WindowHelper, Uno.UI", throwOnError: false);
                var method = helperType?.GetMethod("GetNativeWindow", new[] { typeof(Window) });
                var native = method?.Invoke(null, new object[] { window });
                var hwndProp = native?.GetType().GetProperty("Hwnd");
                return hwndProp?.GetValue(native) switch
                {
                    IntPtr h => h,
                    long l => (IntPtr)l,
                    _ => IntPtr.Zero,
                };
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public static IntPtr TryGetForegroundWindowOfThisProcess()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return IntPtr.Zero;
                _ = GetWindowThreadProcessId(hwnd, out var pid);
                return pid == (uint)Environment.ProcessId ? hwnd : IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>Is this a handle to a window that still exists?</summary>
        public static bool IsWindowAlive(IntPtr hwnd)
        {
            try { return hwnd != IntPtr.Zero && IsWindow(hwnd); }
            catch { return false; }
        }

        /// <summary>
        /// The window class Uno's Skia Win32 host gives its top-level windows — the only reliable way
        /// to find our own windows on this backend.
        /// </summary>
        /// <remarks>
        /// Both of the obvious routes were measured and rejected (2026-08-15, XU-438):
        /// <list type="bullet">
        /// <item><c>WinRT.Interop.WindowNative.GetWindowHandle</c> returns <b>0x1</b> — the WindowId,
        /// NOT an HWND (<c>IsWindow</c> reports false on it), exactly like
        /// <c>AppWindow.Id.Value</c>. It is not a handle on this backend at all.</item>
        /// <item>The foreground window is a coin flip: the same probe returned 0x0 on one run and the
        /// real handle on the next, differing only by where focus happened to be at that instant.</item>
        /// </list>
        /// Enumerating by class has neither problem — it asks the OS what exists rather than asking a
        /// framework for a value it does not have.
        /// </remarks>
        private const string UnoWindowClass = "UnoPlatformRegularWindow";

        /// <summary>
        /// Every visible top-level Uno window belonging to this process. Order is the OS's z-order and
        /// is NOT relied upon — callers discriminate by owner state and title, never by position.
        /// </summary>
        public static List<IntPtr> EnumerateAppWindows()
        {
            var found = new List<IntPtr>();
            var me = (uint)Environment.ProcessId;

            try
            {
                EnumWindows((hwnd, _ignored) =>
                {
                    try
                    {
                        _ = GetWindowThreadProcessId(hwnd, out var pid);

                        // Invisible ones are skipped deliberately: pooled windows (XU-450) are
                        // constructed but never activated, and must never be mistaken for the
                        // window that was just spawned.
                        if (pid != me || !IsWindowVisible(hwnd)) return true;

                        var cls = new char[64];
                        var len = GetClassNameW(hwnd, cls, cls.Length);
                        if (len > 0 && new string(cls, 0, len) == UnoWindowClass) found.Add(hwnd);
                    }
                    catch
                    {
                        // One uncooperative window doesn't end the enumeration.
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                // user32 unavailable — the caller degrades to applying no window policy.
            }

            return found;
        }

        /// <summary>Reads a window's title, or empty when it won't say.</summary>
        public static string WindowTitle(IntPtr hwnd)
        {
            try
            {
                var text = new char[256];
                var len = GetWindowTextW(hwnd, text, text.Length);
                return len > 0 ? new string(text, 0, len) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        // NOTE (XU-461): a WM_NCLBUTTONDOWN/HTCAPTION "let the OS drag it" helper lived here and was
        // REMOVED after testing. It reached the right window, but the OS move loop is modal — it stalls
        // Uno's dispatcher and traps the pointer waiting for a release it never receives. See
        // BeginWindowDrag's remarks before considering it again.

        /// <summary>The HWND that owns this window, or zero when it is free-standing.</summary>
        public static IntPtr OwnerOf(IntPtr hwnd)
        {
            try { return GetWindow(hwnd, GW_OWNER); }
            catch { return IntPtr.Zero; }
        }

        /// <summary>
        /// Establishes a Win32 owner relationship, so <paramref name="owned"/> travels with
        /// <paramref name="owner"/> in the z-order. Returns false if either handle is unusable.
        /// </summary>
        /// <remarks>
        /// Both handles are re-validated here even though callers check: a window owning ITSELF is an
        /// OS-level hang rather than an error return, so this is a guard worth paying twice for.
        /// </remarks>
        public static bool TrySetOwner(IntPtr owned, IntPtr owner)
        {
            try
            {
                if (owned == IntPtr.Zero || owner == IntPtr.Zero || owned == owner) return false;
                if (!IsWindowAlive(owned) || !IsWindowAlive(owner)) return false;

                if (GetWindowLongPtr(owned, GWLP_HWNDPARENT) == owner) return true; // already ours

                SetWindowLongPtr(owned, GWLP_HWNDPARENT, owner);
                return GetWindowLongPtr(owned, GWLP_HWNDPARENT) == owner;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Best-effort per-window opacity via a layered window. A zero or dead handle silently does
        /// nothing — the effect is drag garnish, never load-bearing.
        /// </summary>
        public static void TrySetWindowOpacity(IntPtr hwnd, double opacity)
        {
            try
            {
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;

                var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                if ((style & WS_EX_LAYERED) == 0)
                    SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_LAYERED));

                SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Clamp(opacity * 255, 1, 255), LWA_ALPHA);
            }
            catch
            {
                // The APIs refused — the drag continues opaque.
            }
        }
    }
}
