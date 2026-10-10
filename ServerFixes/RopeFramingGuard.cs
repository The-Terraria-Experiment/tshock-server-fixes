namespace ServerFixes;

/// <summary>
/// Stops Terraria 1.4.5's rope-end framing from recursing until the stack overflows.
/// </summary>
/// <remarks>
/// <para>
/// Vanilla (verified against OTAPI 3.3.14 / Terraria 1.4.5.8) guards the recursion with one static
/// flag, but clears it in the wrong place:
/// </para>
/// <code>
/// HandleRopeEndFraming(i, j)       { HandleRopeEndFraming_Inner(i, j); _preventInfiniteRopeFraming = false; }
/// HandleRopeEndFraming_Inner(i, j) { if (flag) return; flag = true; ... TileFrame(rope ends) ... }
/// </code>
/// <para>
/// A nested call is blocked by the flag, as intended, but its outer wrapper then resets the flag on
/// the way back out. The caller carries on to frame the rope's other end with the guard off, and a
/// rope touching platforms or minecart track can ping-pong between its ends forever
/// (<c>TileFrame → TileFrameImportant → HandleRopeEndFraming → TileFrame → TileFrameCosmetic → …</c>).
/// .NET cannot catch a stack overflow, so the server process aborts (SIGABRT) and the world is lost
/// back to its last save. Seen in production on 2026-10-10, triggered by a client's tile placement.
/// </para>
/// <para>
/// The fix is what the flag was meant to do: while one <c>HandleRopeEndFraming</c> is running on a
/// thread, every nested one is skipped. That differs from vanilla only on the re-entries the broken
/// reset let through; the first nested call was already a no-op there.
/// </para>
/// </remarks>
internal sealed class RopeFramingGuard
{
    // Per thread: world generation frames tiles off the main thread.
    [ThreadStatic] private static int _depth;

    private long _calls;
    private long _skipped;

    public long Calls => Interlocked.Read(ref _calls);
    public long Skipped => Interlocked.Read(ref _skipped);

    public void Attach() => HookEvents.Terraria.WorldGen.HandleRopeEndFraming += OnHandleRopeEndFraming;

    public void Detach() => HookEvents.Terraria.WorldGen.HandleRopeEndFraming -= OnHandleRopeEndFraming;

    private void OnHandleRopeEndFraming(object? sender, HookEvents.Terraria.WorldGen.HandleRopeEndFramingEventArgs e)
    {
        // Another handler already replaced the call; leave it alone.
        if (!e.ContinueExecution)
        {
            return;
        }

        // We run (or skip) the original ourselves, so OTAPI must not run it again afterwards.
        e.ContinueExecution = false;
        var original = e.OriginalMethod;
        Run(e.i, e.j, (i, j) => original(i, j));
    }

    /// <summary>
    /// Runs <paramref name="original"/> unless a call is already in progress on this thread.
    /// Returns whether it ran.
    /// </summary>
    internal bool Run(int i, int j, Action<int, int> original)
    {
        if (_depth > 0)
        {
            Interlocked.Increment(ref _skipped);
            return false;
        }

        Interlocked.Increment(ref _calls);
        _depth++;
        try
        {
            original(i, j);
        }
        finally
        {
            _depth--;
        }

        return true;
    }
}
