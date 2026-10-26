using Terraria;
using TerrariaApi.Server;

namespace ServerFixes;

/// <summary>
/// Acknowledges every NPC strike, including the ones TShock rejects, so 1.4.5.7+ clients' hit
/// prediction queue stays in step with the server.
/// </summary>
/// <remarks>
/// <para>
/// Since 1.4.5.7 a client records each hit it sends (StrikeNPC, 28) in <c>NPC.pendingDamages</c> and
/// subtracts every still-pending hit from the life the server reports for that NPC. The vanilla
/// server answers every packet 28 with an empty packet 162, sent before it even checks the NPC's
/// generation, and the client's <c>NPC.AckDamage</c> pops the oldest pending hit. The protocol relies
/// on exactly one ack per strike, matched by position alone.
/// </para>
/// <para>
/// TShock 6.2.1 rejects strikes (generation mismatch, disabled, throttled, town NPC, and so on) by
/// marking the packet handled, so vanilla's handler never runs and no ack is sent. Each rejection
/// leaves one hit in that client's queue for good. From then on damage is counted twice, so NPCs read
/// as dead on the client while still alive on the server: invisible, unhittable, still dealing
/// contact damage, and snapping back to their real position and life once the stale entry drops out
/// of the queue. Rejoining clears the queue, until the next rejection.
/// </para>
/// <para>
/// This handler runs after TShock and every other plugin on the same packet. It sends the ack only
/// when someone has already marked the strike handled. An unhandled strike reaches vanilla, which
/// acks it itself, and acking twice would pop a hit that is still in flight.
/// </para>
/// </remarks>
internal sealed class NpcStrikeAckFix
{
    // TSAPI's PacketTypes still calls 162 "ServerInfo"; in 1.4.5.8 it is the empty NPC damage ack.
    internal const int NpcDamageAck = 162;

    // Behind TShock (its plugin Order, 0) and anything else at a sane priority. Lower runs later.
    private const int HookPriority = -1000;

    private readonly TerrariaPlugin _plugin;
    private long _acked;

    public NpcStrikeAckFix(TerrariaPlugin plugin)
    {
        _plugin = plugin;
    }

    public long Acked => Interlocked.Read(ref _acked);

    public void Attach() => ServerApi.Hooks.NetGetData.Register(_plugin, OnGetData, HookPriority);

    public void Detach() => ServerApi.Hooks.NetGetData.Deregister(_plugin, OnGetData);

    private void OnGetData(GetDataEventArgs e)
    {
        if (!ShouldAck(e.MsgID, e.Handled))
        {
            return;
        }

        // Not logged per activation: on a busy server this fires on every rejected hit.
        NetMessage.TrySendData(NpcDamageAck, e.Msg.whoAmI);
        Interlocked.Increment(ref _acked);
    }

    /// <summary>
    /// A strike someone else swallowed. Vanilla acks unhandled ones itself.
    /// </summary>
    internal static bool ShouldAck(PacketTypes msgId, bool handled) =>
        handled && msgId == PacketTypes.NpcStrike;
}
