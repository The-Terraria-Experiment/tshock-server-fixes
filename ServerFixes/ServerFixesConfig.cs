using System.Text.Json;
using TShockAPI;

namespace ServerFixes;

/// <summary>
/// <c>tshock/ServerFixes.json</c>. Each fix has its own switch so one can be turned off if a TShock
/// or Terraria update fixes the bug upstream, or if a fix turns out to misbehave. Read at startup.
/// </summary>
internal sealed class ServerFixesConfig
{
    /// <summary>Skip nested rope-end framing calls (prevents the stack-overflow crash).</summary>
    public bool RopeFramingGuard { get; set; } = true;

    /// <summary>Record stale kills of tile-placing balls so their tile isn't rejected.</summary>
    public bool ProjectileKillTileFix { get; set; } = true;

    /// <summary>Ack NPC strikes TShock rejects (prevents invisible, unhittable NPCs on 1.4.5.7+ clients).</summary>
    public bool NpcStrikeAckFix { get; set; } = true;

    /// <summary>Log a console line each time the projectile fix steps in.</summary>
    public bool LogActivations { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(TShock.SavePath, "ServerFixes.json");

    public static ServerFixesConfig LoadOrCreate()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<ServerFixesConfig>(File.ReadAllText(FilePath)) ?? new ServerFixesConfig();
            }

            var config = new ServerFixesConfig();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, JsonOptions));
            return config;
        }
        catch (Exception ex)
        {
            // A broken config must not take the fixes down with it: fall back to all-on.
            TShock.Log.ConsoleError($"[ServerFixes] Could not read {FilePath}, using defaults: {ex.Message}");
            return new ServerFixesConfig();
        }
    }
}
