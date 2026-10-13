using System.Linq;
using Content.Server.Administration;
using Content.Shared._ERRORGATE.World;
using Robust.Shared.Configuration;
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

/// <summary>Builds the whole web of locations: <c>worldbuild [graph] [seed]</c>. Anything built before is torn down.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class WorldBuildCommand : LocalizedEntityCommands
{
    [Dependency] private readonly WorldSystem _world = default!;

    public override string Command => "worldbuild";
    public override string Description => "Builds every location of a world graph and starts their storm timers: worldbuild [graph] [seed]";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var graph = args.Length > 0 ? args[0] : "Default";
        int? seed = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : null;
        if (!_world.TryBuildGraph(graph, seed, out var error))
        {
            shell.WriteError($"Failed: {error}");
            return;
        }

        shell.WriteLine($"Built the world '{graph}': {string.Join(", ", _world.Locations.Keys)}.");
    }
}

/// <summary>Starts the storm for a location now: <c>worldreset &lt;location&gt; [now]</c>. With <c>now</c> there is no warning.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class WorldResetCommand : LocalizedEntityCommands
{
    [Dependency] private readonly WorldSystem _world = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override string Command => "worldreset";
    public override string Description => "Wipes a world location with the storm and builds it again: worldreset <location> [now]. Without 'now' the warning runs first.";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1 || !_world.Locations.ContainsKey(args[0]))
        {
            shell.WriteError("worldreset <location> [now]");
            return;
        }

        if (args.Length > 1 && args[1] == "now")
        {
            _world.Strike(args[0]);
            shell.WriteLine($"{args[0]} was wiped and built again.");
            return;
        }

        var warning = TimeSpan.FromSeconds(_cfg.GetCVar(WorldCVars.WarningSeconds));
        _world.StartWarning(args[0], _world.Now + warning);
        shell.WriteLine($"The storm is coming for {args[0]} in {warning.TotalSeconds:0} seconds.");
    }
}

/// <summary>When each location is wiped.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class WorldStatusCommand : LocalizedEntityCommands
{
    [Dependency] private readonly WorldSystem _world = default!;

    public override string Command => "worldstatus";
    public override string Description => "Shows the state and the next storm of every world location, and the gates.";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        foreach (var (id, loc) in _world.Locations)
        {
            var (state, strike) = _world.StormOf(id);
            var when = strike is { } at ? $"storm in {(at - _world.Now).TotalMinutes:0.0} min" : "no storm";
            shell.WriteLine($"{id}: {state}, {when}, map {loc.MapId}");
        }

        shell.WriteLine("Gates: " + string.Join(", ", _world.GateNames.Select(g => $"{g.Location}.{g.Gate}")));
    }
}

/// <summary>Takes the admin to a gate: <c>worldgo &lt;location&gt; &lt;gate&gt;</c>.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class WorldGoCommand : LocalizedEntityCommands
{
    [Dependency] private readonly WorldSystem _world = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override string Command => "worldgo";
    public override string Description => "Moves you to a gate of a world location: worldgo <location> <gate>";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || !_world.TryGetArrival(args[0], args[1], out var arrival))
        {
            shell.WriteError("worldgo <location> <gate>; worldstatus lists the gates.");
            return;
        }

        if (shell.Player?.AttachedEntity is not { } player)
        {
            shell.WriteError("You have no body.");
            return;
        }

        _transform.SetCoordinates(player, arrival);
        _transform.AttachToGridOrMap(player);
    }
}
