# ServerFixes

A TShock **6.2.1** plugin (.NET 9) that works around two bugs seen on the TTE fleet. Neither is ours
to fix properly: one is in Terraria 1.4.5's own code, the other is a TShock 6.2 regression. Each fix
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
  "LogActivations": true
}
```

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
- `ProjectileKillTileFixTests`: the stale-kill predicate, and the `ProjectileKey` bit layout the
  packet decode depends on.

## Upgrading TShock or Terraria

Both fixes depend on internals, so re-check them before bumping the TShock package:

- Rope: is `_preventInfiniteRopeFraming` still reset in `HandleRopeEndFraming`'s wrapper? If
  Re-Logic fixes it, turn `RopeFramingGuard` off.
- Mud ball: does TShock's `HandleProjectileKill` still return before setting
  `LastKilledProjectile` for an inactive projectile? If it now records it, turn
  `ProjectileKillTileFix` off.
