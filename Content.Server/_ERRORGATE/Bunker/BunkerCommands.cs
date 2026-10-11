using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._ERRORGATE.Bunker;

/// <summary>
///     Builds the bunker now, whatever the map (for testing). The old bunker of this round is replaced.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class BunkerGenCommand : LocalizedEntityCommands
{
    [Dependency] private readonly BunkerSystem _bunker = default!;

    public override string Command => "bunkergen";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        int? seed = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : null;
        shell.WriteLine(_bunker.TryBuild(out var error, seed: seed) ? "Done. bunkerinfo shows where the hatch is, the log has the layout seed." : $"Failed: {error}");
    }
}

/// <summary>
///     Where the hatch and the bunker are.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class BunkerInfoCommand : LocalizedEntityCommands
{
    [Dependency] private readonly BunkerSystem _bunker = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override string Command => "bunkerinfo";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (_bunker.Hatch is not { } hatch || EntityManager.Deleted(hatch))
        {
            shell.WriteLine("There is no bunker this round.");
            return;
        }

        var xform = EntityManager.GetComponent<TransformComponent>(hatch);
        shell.WriteLine($"Hatch: {EntityManager.GetNetEntity(hatch)} at {_transform.GetWorldPosition(xform):0}, map {xform.MapID}. Bunker map: {(_bunker.BunkerMap is { } map ? EntityManager.GetNetEntity(map).ToString() : "-")}.");
    }
}
