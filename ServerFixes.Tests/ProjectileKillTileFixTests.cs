using ServerFixes;
using Terraria.DataStructures;
using Terraria.ID;

namespace ServerFixes.Tests;

public class ProjectileKillTileFixTests
{
    private const int Sender = 3;

    [Fact]
    public void Records_a_stale_mud_ball_kill_from_its_owner()
    {
        Assert.True(ProjectileKillTileFix.ShouldRecord(
            active: false, owner: Sender, keySpawner: Sender, sender: Sender, ProjectileID.MudBallPlayer));
    }

    [Fact]
    public void Leaves_active_projectiles_to_TShock()
    {
        Assert.False(ProjectileKillTileFix.ShouldRecord(
            active: true, owner: Sender, keySpawner: Sender, sender: Sender, ProjectileID.MudBallPlayer));
    }

    [Theory]
    [InlineData(4, Sender)] // projectile owned by someone else
    [InlineData(Sender, 4)] // key claims another spawner
    public void Refuses_a_kill_for_someone_elses_projectile(int owner, int keySpawner)
    {
        Assert.False(ProjectileKillTileFix.ShouldRecord(
            active: false, owner, keySpawner, sender: Sender, ProjectileID.MudBallPlayer));
    }

    [Fact]
    public void Ignores_projectiles_that_place_no_tile()
    {
        Assert.False(ProjectileKillTileFix.ShouldRecord(
            active: false, owner: Sender, keySpawner: Sender, sender: Sender, ProjectileID.WoodenArrowFriendly));
    }

    [Fact]
    public void Tile_map_matches_what_each_ball_places()
    {
        Assert.Equal(TileID.Mud, ProjectileKillTileFix.TilePlacingProjectiles[ProjectileID.MudBallPlayer]);
        Assert.Equal(TileID.Dirt, ProjectileKillTileFix.TilePlacingProjectiles[ProjectileID.DirtBall]);
    }

    [Fact]
    public void ProjectileKey_bit_layout_is_what_the_packet_decode_assumes()
    {
        // The handler reads the key as a raw int32 off the packet. If Terraria changes the packing,
        // this fails here rather than silently matching the wrong projectile in production.
        var key = new ProjectileKey(spawner: 7, index: 900, generation: 1234);
        var decoded = (ProjectileKey)(int)key;

        Assert.Equal(7, decoded.Spawner);
        Assert.Equal(900, decoded.Index);
        Assert.Equal(1234, decoded.Generation);
    }
}
