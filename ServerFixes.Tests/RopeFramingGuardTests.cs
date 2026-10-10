using ServerFixes;

namespace ServerFixes.Tests;

public class RopeFramingGuardTests
{
    [Fact]
    public void Runs_a_top_level_call()
    {
        var guard = new RopeFramingGuard();
        var ran = false;

        Assert.True(guard.Run(10, 20, (_, _) => ran = true));
        Assert.True(ran);
        Assert.Equal(1, guard.Calls);
        Assert.Equal(0, guard.Skipped);
    }

    [Fact]
    public void Skips_every_nested_call_and_terminates_a_cycle()
    {
        // Models the production crash: each framing call frames the other rope end, which calls
        // back in. Unguarded this recurses until the stack overflows.
        var guard = new RopeFramingGuard();
        var entered = 0;
        Action<int, int>? frame = null;
        frame = (i, j) =>
        {
            entered++;
            guard.Run(i, j + 1, frame!);
            guard.Run(i, j - 1, frame!);
        };

        guard.Run(5, 5, frame);

        Assert.Equal(1, entered);
        Assert.Equal(1, guard.Calls);
        Assert.Equal(2, guard.Skipped);
    }

    [Fact]
    public void Depth_is_restored_after_the_original_throws()
    {
        var guard = new RopeFramingGuard();

        Assert.Throws<InvalidOperationException>(() =>
            guard.Run(1, 1, (_, _) => throw new InvalidOperationException()));

        // A leaked depth would skip every later call for the life of the server.
        Assert.True(guard.Run(1, 1, (_, _) => { }));
    }

    [Fact]
    public void Depth_is_per_thread()
    {
        var guard = new RopeFramingGuard();
        bool? ranOnOtherThread = null;

        guard.Run(1, 1, (_, _) =>
        {
            // World generation frames tiles off the main thread; one thread's call must not
            // suppress another's.
            ranOnOtherThread = Task.Run(() => guard.Run(2, 2, (_, _) => { })).GetAwaiter().GetResult();
        });

        Assert.True(ranOnOtherThread);
    }
}
