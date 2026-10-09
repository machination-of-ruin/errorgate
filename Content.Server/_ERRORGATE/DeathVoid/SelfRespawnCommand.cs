using Content.Shared._ERRORGATE.DeathVoid;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Console;

namespace Content.Server._ERRORGATE.DeathVoid;

/// <summary>
///     Lets a player who is dead (or has no body left) go back to the lobby.
///     Admins use <c>forcerespawn</c> to respawn anyone.
/// </summary>
public abstract class SelfRespawnCommandBase : LocalizedEntityCommands
{
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-respawn-no-player"));
            return;
        }

        if (args.Length != 0)
        {
            shell.WriteError(Loc.GetString("cmd-respawn-invalid-args"));
            return;
        }

        if (player.AttachedEntity is { } entity
            && !EntityManager.HasComponent<DeathVoidComponent>(entity)
            && !_mobState.IsDead(entity))
        {
            shell.WriteError(Loc.GetString("cmd-respawn-not-dead"));
            return;
        }

        EntityManager.System<DeathVoidSystem>().TryRespawn(player, out _);
    }
}

[AnyCommand]
public sealed class SelfRespawnCommand : SelfRespawnCommandBase
{
    public override string Command => "respawn";
}

/// <summary>
///     Same as <c>respawn</c>, under the name the death screen tells players to use.
/// </summary>
[AnyCommand]
public sealed class RiseCommand : SelfRespawnCommandBase
{
    public override string Command => "rise";
}
