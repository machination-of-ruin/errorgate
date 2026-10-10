using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     Shows what MACHINATION OF RUIN knows: the subjects of the round with their numbers and records.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class GodStatusCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GodLedgerSystem _ledger = default!;
    [Dependency] private readonly GodObserverSystem _observer = default!;

    public override string Command => "godstatus";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var subjects = _ledger.Ledger.All.OrderBy(s => s.Number).ToList();
        shell.WriteLine($"Subjects: {subjects.Count}. Buffered: {_observer.Buffer.Events.Count} events, {_observer.Buffer.Prayers.Count} prayers, {_observer.Buffer.Mentions.Count} mentions, {_observer.Buffer.Speech.Count} context lines.");

        foreach (var s in subjects)
        {
            var near = s.Near.Count > 0 ? " with " + string.Join(",", s.Near.Select(n => $"S{n}")) : string.Empty;
            shell.WriteLine($"S{s.Number} {s.Name} ({(s.Alive ? "alive" : "dead")}) lives {s.Lives} errors {s.Errors} sector {s.Sector ?? "-"}{near} " +
                            $"kills {s.KillsOfPlayers}p/{s.KillsOfMobs}m dealt {s.DamageDealt:0} taken {s.DamageTaken:0} spoke {s.SpeechLines} prayed {s.Prayers} " +
                            $"visited {s.SectorsVisited.Count}");
        }
    }
}

/// <summary>
///     Prints the digest the model would get right now, without emptying the buffer.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class GodDigestCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GodDigestSystem _digest = default!;

    public override string Command => "goddigest";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var result = _digest.Preview();
        shell.WriteLine($"Digest: about {result.Tokens} tokens{(result.Empty ? ", nothing to report" : string.Empty)}");
        shell.WriteLine(result.Text);

        if (result.OverflowMentions.Count > 0)
            shell.WriteLine($"({result.OverflowMentions.Count} older mention lines would be summarised first)");
    }
}

/// <summary>
///     Lists the sectors of a grid (the main one unless a grid uid is given).
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class GodSectorsCommand : LocalizedEntityCommands
{
    [Dependency] private readonly SectorSystem _sectors = default!;

    public override string Command => "godsectors";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        EntityUid? best = null;
        var bestChunks = -1;
        var query = EntityManager.EntityQueryEnumerator<MapGridComponent>();
        while (query.MoveNext(out var uid, out var grid))
        {
            if (args.Length > 0 && !(NetEntity.TryParse(args[0], out var net) && EntityManager.TryGetEntity(net, out var wanted) && wanted == uid))
                continue;

            if (grid.ChunkCount > bestChunks)
            {
                best = uid;
                bestChunks = grid.ChunkCount;
            }
        }

        if (best is not { } gridUid || !EntityManager.TryGetComponent(gridUid, out MapGridComponent? gridComp))
        {
            shell.WriteError("No such grid.");
            return;
        }

        var names = _sectors.SectorNames(gridUid, gridComp);
        shell.WriteLine($"{names.Count} sectors: {string.Join(" ", names)}");
    }
}
