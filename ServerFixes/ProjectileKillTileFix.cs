using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace ServerFixes;

/// <summary>
/// Lets a thrown mud ball (and the other tile-placing balls) place its tile when the server's own
/// copy of the projectile has already died.
/// </summary>
/// <remarks>
/// <para>
/// When a mud ball lands, the owning client sends ProjectileDestroy (29) and then a TileEdit placing
/// the mud. TShock's Bouncer only accepts that TileEdit if the player is holding a mud ball, or if
/// <see cref="TSPlayer.LastKilledProjectile"/> says their last kill was one. Throw your last mud ball
/// and only the second path is left.
/// </para>
/// <para>
/// TShock 6.2 (commit a1c4c332) made <c>HandleProjectileKill</c> drop any kill whose projectile the
/// server already has as inactive ("stale projectile key"), before it records
/// <c>LastKilledProjectile</c>. The server simulates every projectile itself, and its copy can land
/// first: a bounce off a ceiling and a wall is enough for the two simulations to drift. Vanilla
/// expects this (its own packet-29 handler checks <c>proj.active</c> and relays the kill anyway), but
/// TShock's early return means the mud ball is never recorded. Bouncer rejects the TileEdit and
/// resyncs the tile, and the client already consumed the item, so the mud just vanishes.
/// </para>
/// <para>
/// This handler runs ahead of TShock on the same packet. In exactly that stale case it records what
/// TShock would have: the projectile's type, for a projectile this player owned that creates a tile.
/// It never marks the packet handled, so TShock still applies all of its own checks.
/// </para>
/// </remarks>
internal sealed class ProjectileKillTileFix
{
    // Mirrors TShock's GetDataHandlers.projectileCreatesTile (internal) minus the rope coils, which
    // Bouncer validates through RecentlyCreatedProjectiles rather than LastKilledProjectile.
    internal static readonly IReadOnlyDictionary<int, int> TilePlacingProjectiles = new Dictionary<int, int>
    {
        { ProjectileID.DirtBall, TileID.Dirt },
        { ProjectileID.MudBallPlayer, TileID.Mud },
        { ProjectileID.SandBallGun, TileID.Sand },
        { ProjectileID.EbonsandBallGun, TileID.Ebonsand },
        { ProjectileID.PearlSandBallGun, TileID.Pearlsand },
        { ProjectileID.CrimsandBallGun, TileID.Crimsand },
    };

    // Ahead of TShock, which registers NetGetData at its plugin Order (0). Higher runs first.
    private const int HookPriority = 100;

    private readonly TerrariaPlugin _plugin;
    private readonly Func<bool> _logActivations;
    private long _recovered;

    public ProjectileKillTileFix(TerrariaPlugin plugin, Func<bool> logActivations)
    {
        _plugin = plugin;
        _logActivations = logActivations;
    }

    public long Recovered => Interlocked.Read(ref _recovered);

    public void Attach() => ServerApi.Hooks.NetGetData.Register(_plugin, OnGetData, HookPriority);

    public void Detach() => ServerApi.Hooks.NetGetData.Deregister(_plugin, OnGetData);

    private void OnGetData(GetDataEventArgs e)
    {
        if (e.Handled || e.MsgID != PacketTypes.ProjectileDestroy)
        {
            return;
        }

        // Payload starts at e.Index (the id byte is already consumed): int32 ProjectileKey, Vector2 pos.
        if (e.Length - 1 < sizeof(int) || e.Index + sizeof(int) > e.Msg.readBuffer.Length)
        {
            return;
        }

        int whoAmI = e.Msg.whoAmI;
        var player = whoAmI is >= 0 and < 255 ? TShock.Players[whoAmI] : null;
        if (player is null)
        {
            return;
        }

        var key = (ProjectileKey)BitConverter.ToInt32(e.Msg.readBuffer, e.Index);
        if (!key.TryGet(out var proj) || proj is null)
        {
            return;
        }

        if (!ShouldRecord(proj.active, proj.owner, key.Spawner, whoAmI, proj.type))
        {
            return;
        }

        if (TShock.ProjectileBans.ProjectileIsBanned((short)proj.type, player))
        {
            return;
        }

        player.LastKilledProjectile = proj.type;
        Interlocked.Increment(ref _recovered);

        if (_logActivations())
        {
            TShock.Log.ConsoleInfo(
                $"[ServerFixes] Recorded stale kill of projectile {proj.type} for {player.Name} so its tile can be placed.");
        }
    }

    /// <summary>
    /// The stale case TShock drops: an inactive projectile, owned by and keyed to the sender, of a
    /// type that places a tile when it dies. An active one goes through TShock's normal path.
    /// </summary>
    internal static bool ShouldRecord(bool active, int owner, int keySpawner, int sender, int type) =>
        !active
        && owner == sender
        && keySpawner == sender
        && TilePlacingProjectiles.ContainsKey(type);
}
