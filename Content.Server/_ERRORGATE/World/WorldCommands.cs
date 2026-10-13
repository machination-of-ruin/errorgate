using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._ERRORGATE.World;

/// <summary>Builds a location now: <c>worldgen &lt;location&gt; [seed]</c>. The old one of that name is torn down.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class WorldGenCommand : LocalizedEntityCommands
{
    [Dependency] private readonly WorldSystem _world = default!;

    public override string Command => "worldgen";
    public override string Description => "Builds a world location (replacing the old one): worldgen <Village|SnowCity> [seed]";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError("worldgen <location> [seed]");
            return;
        }

        int? seed = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : null;
        if (!_world.TryBuild(args[0], seed, out var error))
        {
            shell.WriteError($"Failed: {error}");
            return;
        }

        var loc = _world.Locations[args[0]];
        shell.WriteLine($"Built {args[0]} on map {loc.MapId}: seed {loc.Seed}, {loc.Structures} structures, {loc.Entities} entities, {loc.BuildMilliseconds:0} ms.");
    }
}

/// <summary>Tears a location down: <c>worldclear &lt;location|all&gt;</c>.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class WorldClearCommand : LocalizedEntityCommands
{
    [Dependency] private readonly WorldSystem _world = default!;

    public override string Command => "worldclear";
    public override string Description => "Tears down a world location: worldclear <location|all>";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError("worldclear <location|all>");
            return;
        }

        foreach (var id in args[0] == "all" ? _world.Locations.Keys.ToList() : new List<string> { args[0] })
        {
            _world.Teardown(id);
            shell.WriteLine($"Torn down {id}.");
        }
    }
}

/// <summary>What is built.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class WorldInfoCommand : LocalizedEntityCommands
{
    [Dependency] private readonly WorldSystem _world = default!;

    public override string Command => "worldinfo";
    public override string Description => "Lists the built world locations.";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (_world.Locations.Count == 0)
        {
            shell.WriteLine("No location is built.");
            return;
        }

        foreach (var (id, loc) in _world.Locations)
        {
            shell.WriteLine($"{id}: map {loc.MapId}, tier {loc.Proto.Tier}, seed {loc.Seed}, {loc.Structures} structures, {loc.Entities} entities, built in {loc.BuildMilliseconds:0} ms.");
        }
    }
}
