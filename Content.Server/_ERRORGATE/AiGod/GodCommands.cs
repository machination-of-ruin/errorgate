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
    [Dependency] private readonly GodDirectorSystem _director = default!;

    public override string Command => "godstatus";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var subjects = _ledger.Ledger.All.OrderBy(s => s.Number).ToList();
        shell.WriteLine($"{_director.StateLine()} | {_director.Pending.Count()} waiting for approval");
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

/// <summary>
///     Shows the newest decisions: what she wanted to do, and what became of it.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class GodLogCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GodDirectorSystem _director = default!;

    public override string Command => "godlog";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var count = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 15;
        shell.WriteLine(_director.StateLine());

        var shown = _director.Log.TakeLast(Math.Max(1, count)).ToList();
        if (shown.Count == 0)
            shell.WriteLine("No decisions yet.");

        foreach (var decision in shown)
        {
            shell.WriteLine(decision.ToString());
        }
    }
}

/// <summary>
///     Says yes to a decision that waits for approval (a number, or "all").
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class GodApproveCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GodDirectorSystem _director = default!;

    public override string Command => "godapprove";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("godapprove <id|all>");
            return;
        }

        foreach (var id in GodCommandHelpers.Ids(_director, args[0]))
        {
            _director.Approve(id, out var message);
            shell.WriteLine(message);
        }
    }
}

/// <summary>
///     Says no to a decision that waits for approval (a number, or "all").
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class GodDenyCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GodDirectorSystem _director = default!;

    public override string Command => "goddeny";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("goddeny <id|all>");
            return;
        }

        foreach (var id in GodCommandHelpers.Ids(_director, args[0]))
        {
            _director.Deny(id, out var message);
            shell.WriteLine(message);
        }
    }
}

/// <summary>
///     Stops her from acting (godpause) and lets her act again (godresume), without touching the valves.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class GodPauseCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GodDirectorSystem _director = default!;

    public override string Command => "godpause";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _director.Paused = true;
        shell.WriteLine("She is paused.");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class GodResumeCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GodDirectorSystem _director = default!;

    public override string Command => "godresume";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _director.Paused = false;
        shell.WriteLine("She acts again.");
    }
}

/// <summary>
///     Makes her do something now, through the same checks (but without approval and without points):
///     <c>godforce subtle S4 text</c>, <c>godforce announce text</c>, <c>godforce glitch S4</c> or <c>godforce glitch C4</c>.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class GodForceCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GodDirectorSystem _director = default!;

    public override string Command => "godforce";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("godforce subtle <S#> <text> | announce <text> | glitch <S#|sector>");
            return;
        }

        var action = new GodAction();
        switch (args[0].ToLowerInvariant())
        {
            case "subtle":
                if (args.Length < 3 || !GodCommandHelpers.TryNumber(args[1], out var number))
                {
                    shell.WriteError("godforce subtle <S#> <text>");
                    return;
                }

                action.Type = GodActionType.Subtle;
                action.Targets.Add(number);
                action.Text = string.Join(' ', args.Skip(2));
                break;

            case "announce":
                action.Type = GodActionType.Announce;
                action.Text = string.Join(' ', args.Skip(1));
                break;

            case "glitch":
                action.Type = GodActionType.Glitch;
                if (GodCommandHelpers.TryNumber(args[1], out var target))
                    action.Targets.Add(target);
                else
                    action.Sector = args[1].ToUpperInvariant();

                break;

            default:
                shell.WriteError("Unknown action.");
                return;
        }

        shell.WriteLine(_director.Submit(action, GodSource.Admin).ToString());
    }
}

public static class GodCommandHelpers
{
    /// <summary>"S17" or "17".</summary>
    public static bool TryNumber(string text, out int number)
    {
        if (text.Length > 1 && (text[0] == 'S' || text[0] == 's'))
            text = text[1..];

        return int.TryParse(text, out number);
    }

    public static IEnumerable<int> Ids(GodDirectorSystem director, string arg)
    {
        if (string.Equals(arg, "all", StringComparison.OrdinalIgnoreCase))
            return director.Pending.Select(d => d.Id).ToList();

        return int.TryParse(arg, out var id) ? new[] { id } : Array.Empty<int>();
    }
}

/// <summary>
///     Shows the last digest sent to the model, the last reply, and what was wrong with it.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class GodPromptCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GodLlmDirectorSystem _llm = default!;
    [Dependency] private readonly GodDirectorSystem _director = default!;

    public override string Command => "godprompt";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine($"Calls {_llm.Calls}, failures {_llm.Failures}, model {(_director.LlmFailing ? "not answering" : "ok")}.");
        shell.WriteLine("--- last digest ---");
        shell.WriteLine(_llm.LastDigest == string.Empty ? "(none yet)" : _llm.LastDigest);
        shell.WriteLine("--- last reply ---");
        shell.WriteLine(_llm.LastReply == string.Empty ? "(none yet)" : _llm.LastReply);

        foreach (var problem in _llm.LastProblems)
        {
            shell.WriteLine($"problem: {problem}");
        }
    }
}

/// <summary>
///     Asks the model now (as soon as the minimum gap allows) instead of waiting for the next turn.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class GodCallCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GodLlmDirectorSystem _llm = default!;

    public override string Command => "godcall";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _llm.RequestSoon();
        shell.WriteLine("The model will be asked at the next opportunity (godlog and godprompt show what comes back).");
    }
}
