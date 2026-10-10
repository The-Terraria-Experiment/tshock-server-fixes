using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace ServerFixes;

/// <summary>
/// TShock 6.2 plugin: server-side workarounds for Terraria and TShock bugs. See README for each fix.
/// </summary>
[ApiVersion(2, 1)]
public sealed class ServerFixesPlugin : TerrariaPlugin
{
    public override string Name => "ServerFixes";
    public override string Author => "Caleb Dougal";
    public override string Description => "Workarounds for Terraria/TShock bugs: rope framing crash, vanishing mud balls.";
    public override Version Version => new(1, 0, 0, 0);

    private ServerFixesConfig _config = new();
    private RopeFramingGuard? _ropeGuard;
    private ProjectileKillTileFix? _killFix;
    private Command? _command;

    public ServerFixesPlugin(Main game) : base(game)
    {
    }

    public override void Initialize()
    {
        _config = ServerFixesConfig.LoadOrCreate();

        if (_config.RopeFramingGuard)
        {
            _ropeGuard = new RopeFramingGuard();
            _ropeGuard.Attach();
        }

        if (_config.ProjectileKillTileFix)
        {
            _killFix = new ProjectileKillTileFix(this, () => _config.LogActivations);
            _killFix.Attach();
        }

        _command = new Command("serverfixes.status", ShowStatus, "serverfixes")
        {
            HelpText = "Shows which ServerFixes are active and how often each has stepped in.",
            AllowServer = true,
        };
        Commands.ChatCommands.Add(_command);

        TShock.Log.ConsoleInfo(
            $"[ServerFixes] Rope framing guard: {OnOff(_ropeGuard)}. Projectile kill tile fix: {OnOff(_killFix)}.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ropeGuard?.Detach();
            _killFix?.Detach();
            if (_command is not null)
            {
                Commands.ChatCommands.Remove(_command);
            }
        }

        base.Dispose(disposing);
    }

    private void ShowStatus(CommandArgs args)
    {
        args.Player.SendInfoMessage(_ropeGuard is null
            ? "Rope framing guard: off."
            : $"Rope framing guard: on. {_ropeGuard.Calls} framing calls, {_ropeGuard.Skipped} nested calls skipped.");
        args.Player.SendInfoMessage(_killFix is null
            ? "Projectile kill tile fix: off."
            : $"Projectile kill tile fix: on. {_killFix.Recovered} stale kills recorded.");
    }

    private static string OnOff(object? fix) => fix is null ? "off" : "on";
}
