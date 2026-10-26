using System.Reflection;
using ServerFixes;
using Terraria;
using TerrariaApi.Server;

namespace ServerFixes.Tests;

public class NpcStrikeAckFixTests
{
    [Fact]
    public void Acks_a_strike_someone_else_rejected()
    {
        Assert.True(NpcStrikeAckFix.ShouldAck(PacketTypes.NpcStrike, handled: true));
    }

    [Fact]
    public void Leaves_unhandled_strikes_to_vanilla()
    {
        // Vanilla acks these itself; a second ack would pop a hit that is still in flight.
        Assert.False(NpcStrikeAckFix.ShouldAck(PacketTypes.NpcStrike, handled: false));
    }

    [Fact]
    public void Ignores_other_handled_packets()
    {
        Assert.False(NpcStrikeAckFix.ShouldAck(PacketTypes.ProjectileDestroy, handled: true));
    }

    [Fact]
    public void Strike_packet_is_still_28()
    {
        Assert.Equal(28, (int)PacketTypes.NpcStrike);
    }

    [Fact]
    public void Client_still_has_the_damage_ack_queue()
    {
        // If Terraria drops or reworks the pending-damage queue, this fix is sending a packet the
        // client no longer expects. Re-check MessageBuffer's case 28/162 before re-enabling.
        Assert.NotNull(typeof(NPC).GetMethod("AckDamage", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(NPC).GetField("pendingDamages", BindingFlags.Public | BindingFlags.Static));
    }
}
