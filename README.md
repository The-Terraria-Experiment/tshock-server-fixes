# ServerFixes

A TShock **6.2.1** plugin (.NET 9) that works around three bugs seen on the TTE fleet. None is ours
to fix properly: one is in Terraria 1.4.5's own code, and the other two are TShock 6.2 bugs. Each fix
can be switched off on its own once upstream catches up.

DISCLAIMER: This plugin is almost entirely created by AI. Use with caution.

## Fix 1: rope framing stack overflow (server crash)

**Symptom:** the game server process dies mid-session and the world rolls back to its last save.
The instance stays up. stderr (`logs/errout/<date>.log`) starts with `Stack overflow.`, followed by
thousands of repeats of
`TileFrame → TileFrameImportant → HandleRopeEndFraming → TileFrame → TileFrameCosmetic → …`, and
systemd reports `code=dumped, status=6/ABRT`. Seen on 2026-10-10.

**Cause:** Terraria 1.4.5 added framing for rope ends that meet platforms or minecart track, with a
static `_preventInfiniteRopeFraming` flag to stop it recursing. The flag is cleared by the wrapper of
*every* call, including a nested one that the flag just blocked. So the outer call continues with the
guard off, and a rope can ping-pong between its two ends until the stack overflows. .NET cannot catch
that, so the process aborts. Verified in OTAPI 3.3.14 (Terraria 1.4.5.8).

**Fix:** `RopeFramingGuard` hooks OTAPI's `HandleRopeEndFraming` event. While one call is running on
a thread, every nested call is skipped, which is what the vanilla flag was meant to do. It differs
from vanilla only on the re-entries the broken reset let through.

## Fix 2: a single thrown mud ball vanishes

**Symptom:** throw your *last* mud ball so that it bounces (for example off the underside of a block
and then a wall), and when it lands no mud appears. It happens most of the time. With more than one
mud ball in the stack it works.

**Cause:** when the ball dies, the client sends ProjectileDestroy and then a TileEdit placing mud.
TShock's Bouncer accepts that TileEdit only if the player is holding a mud ball, or if
`TSPlayer.LastKilledProjectile` says their last kill was one. With the stack used up, only the
second path is left. TShock 6.2 (commit `a1c4c332`) drops any kill whose projectile the server
already has as inactive ("stale projectile key") before recording `LastKilledProjectile`. The server
simulates its own copy of every projectile, and after a few bounces its copy can land first. Vanilla
expects that; TShock's early return means the kill is never recorded, Bouncer rejects the mud, and
the client has already used the item.

**Fix:** `ProjectileKillTileFix` handles ProjectileDestroy ahead of TShock (NetGetData priority 100;
TShock is at 0). Only in that stale case, and only for a tile-placing ball the sender owned, it
records `LastKilledProjectile` the way TShock would have. It never marks the packet handled, so
TShock still runs every check of its own, and a projectile ban still blocks it. Covers dirt, mud and
the sand-gun balls, mirroring TShock's own `projectileCreatesTile` list.

This diagnosis comes from reading the source, not from watching it happen. Each time the fix steps
in it logs `[ServerFixes] Recorded stale kill of projectile …`. If those lines appear when the bug
used to happen, and the mud stays, the diagnosis is confirmed.

## Fix 3: invisible, unhittable NPCs

**Symptom:** mobs turn invisible for a few seconds, usually right after being hit. While invisible
they can't be damaged (projectiles pass through) but still deal contact damage and fire
projectiles. They then reappear somewhere else at the health they had before the hit. A "killed" mob
can linger invisibly like this. There is no lag. Rejoining fixes it for a while, then it comes back.
Seen from 2026-10-03, and badly during the 2026-10-24 event.

**Cause:** Terraria 1.4.5.7 added client-side hit prediction. A client records each hit it sends
(StrikeNPC, packet 28) in `NPC.pendingDamages` and shows an NPC's life as the server's value minus
every hit not yet acknowledged. The vanilla server answers *every* packet 28 with an empty packet
162. It sends it before it even checks the NPC's generation, because strikes on a just-recycled NPC
slot are normal. The client's `NPC.AckDamage` then pops the oldest pending hit, by position only.

TShock 6.2.1 rejects strikes by marking the packet handled: a generation mismatch in
`HandleNpcStrike` comes first, then Bouncer's disabled/throttled/range checks, town NPCs and so on.
Vanilla's handler never runs, so no ack goes out. Each rejection leaves one hit in that client's
queue for good, so damage is counted twice from then on. NPCs read as dead on the client while still
alive on the server, until the stale entry works its way out of the queue. Rejoining clears the
queue (`ClearWorld`). OTAPI 3.3.11 (TShock 6.1.0) has no ack at all, which is why this appeared with
the 6.2.1 upgrade. Verified in OTAPI 3.3.14 (Terraria 1.4.5.8).

**Fix:** `NpcStrikeAckFix` registers on NetGetData at priority -1000, after TShock and everything
else. When packet 28 arrives already marked handled, it sends the sender packet 162, which is what
vanilla would have done. Unhandled strikes are left alone because vanilla acks those itself, and a
second ack would pop a hit that is still in flight. It never changes `Handled` and never applies the
strike, so every TShock rejection still stands.

Activations aren't logged per line, since on a busy server this fires on every rejected hit. Use
`/serverfixes` to see the count. TShock logs the rejections themselves at debug level, so turning on
`DebugLogs` shows which check is doing the rejecting.

## Build & install

```sh
dotnet build ServerFixes/ServerFixes.csproj -c Release
```

Copy `ServerFixes/bin/Release/ServerFixes.dll` into the server's `ServerPlugins/` folder and restart.
Only that one DLL is produced; TShock/OTAPI are compile-time references. To auto-copy on build:

```sh
dotnet build ServerFixes/ServerFixes.csproj -c Release -p:ServerPluginsPath="C:\TShock\ServerPlugins"
```

On first start it writes `tshock/ServerFixes.json`:

```json
{
  "RopeFramingGuard": true,
  "ProjectileKillTileFix": true,
  "NpcStrikeAckFix": true,
  "LogActivations": true
}
```

A key missing from an existing file reads as `true`, so a server upgraded from 1.0 gets new fixes
switched on without the file being rewritten. `LogActivations` covers the projectile fix only.

Changes take effect on the next restart. `/serverfixes` (permission `serverfixes.status`, also
usable from the console) shows which fixes are on and how often each has stepped in.

## Tests

```sh
dotnet test
```

- `RopeFramingGuardTests`: the guard runs top-level calls, skips nested ones, ends a modelled
  ping-pong cycle, recovers after an exception, and doesn't block other threads.
- `RopeFramingHookTests`: calls the real `WorldGen.HandleRopeEndFraming` with out-of-world
  coordinates, so a Terraria update that stops raising the OTAPI hook fails here.
- `NpcStrikeAckFixTests`: acks only handled strikes, and the client still has the
  `AckDamage`/`pendingDamages` queue the ack feeds.
- `ProjectileKillTileFixTests`: the stale-kill predicate, and the `ProjectileKey` bit layout the
  packet decode depends on.

## Upgrading TShock or Terraria

All three fixes depend on internals, so re-check them before bumping the TShock package:

- Rope: is `_preventInfiniteRopeFraming` still reset in `HandleRopeEndFraming`'s wrapper? If
  Re-Logic fixes it, turn `RopeFramingGuard` off.
- Mud ball: does TShock's `HandleProjectileKill` still return before setting
  `LastKilledProjectile` for an inactive projectile? If it now records it, turn
  `ProjectileKillTileFix` off.
- NPC ack: does TShock now send packet 162 itself when it rejects a strike (grep for `162` or
  `AckDamage`)? If so, turn `NpcStrikeAckFix` off, or clients get two acks per rejected strike.
  Also check vanilla still acks before its generation check in `MessageBuffer` case 28.
