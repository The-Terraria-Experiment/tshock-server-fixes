using ServerFixes;
using Terraria;

namespace ServerFixes.Tests;

/// <summary>
/// Drives the real <see cref="WorldGen.HandleRopeEndFraming"/> through OTAPI's hook, so a Terraria
/// update that stops raising the event (or renames it) fails here rather than silently unguarding
/// the server. Coordinates are out of the world, so vanilla returns before touching any tile.
/// </summary>
public class RopeFramingHookTests : IDisposable
{
    private readonly RopeFramingGuard _guard = new();

    public RopeFramingHookTests() => _guard.Attach();

    public void Dispose() => _guard.Detach();

    [Fact]
    public void Vanilla_calls_route_through_the_guard()
    {
        WorldGen.HandleRopeEndFraming(-10, -10);

        Assert.Equal(1, _guard.Calls);
        Assert.Equal(0, _guard.Skipped);
        // Vanilla's own wrapper still ran, so it reset its flag as usual.
        Assert.False(WorldGen._preventInfiniteRopeFraming);
    }

    [Fact]
    public void A_nested_vanilla_call_is_skipped()
    {
        WorldGen.HandleRopeEndFraming(-10, -10);
        _guard.Run(-20, -20, (_, _) => WorldGen.HandleRopeEndFraming(-10, -10));

        Assert.Equal(2, _guard.Calls);
        Assert.Equal(1, _guard.Skipped);
    }
}
