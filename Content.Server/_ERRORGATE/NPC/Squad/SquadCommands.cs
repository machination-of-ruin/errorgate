using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._ERRORGATE.NPC.Squad;

/// <summary>Lists the squads: <c>squads</c>.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class SquadsCommand : LocalizedEntityCommands
{
    [Dependency] private readonly SquadSystem _squads = default!;

    public override string Command => "squads";
    public override string Description => "Lists the NPC squads: id, task, members, what they are doing.";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var query = EntityManager.EntityQueryEnumerator<SquadComponent>();
        while (query.MoveNext(out var uid, out var squad))
        {
            var state = squad.InCombat ? "fighting" : squad.Holding ? "holding" : squad.Goal != null ? "moving" : "idle";
            var leader = squad.Leader is { } l ? EntityManager.ToPrettyString(l).ToString() : "-";
            shell.WriteLine($"{uid} {squad.Task.Name}: {squad.Members.Count} members, {state}, leader {leader}");
        }
    }
}

/// <summary>
///     Gives a squad a task: <c>squadtask &lt;squad or member id&gt; patrol|guard|hunt|follow &lt;entity id&gt;|eliminate &lt;squad id&gt;</c>.
///     <c>guard</c> guards the place where the leader stands now.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class SquadTaskCommand : LocalizedEntityCommands
{
    [Dependency] private readonly SquadSystem _squads = default!;

    public override string Command => "squadtask";
    public override string Description => "Gives a squad a task: squadtask <squad or member id> patrol|guard|hunt|follow <entity id>|eliminate <squad id>";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || !NetEntity.TryParse(args[0], out var netEntity) || !EntityManager.TryGetEntity(netEntity, out var uid))
        {
            shell.WriteError("squadtask <squad or member id> patrol|guard|hunt|follow <entity id>|eliminate <squad id>");
            return;
        }

        // A member id works as well as the id of the squad
        if (!EntityManager.HasComponent<SquadComponent>(uid.Value) && EntityManager.TryGetComponent<NpcSquadMemberComponent>(uid.Value, out var member))
            uid = member.Squad;

        if (!EntityManager.TryGetComponent<SquadComponent>(uid.Value, out var squad))
        {
            shell.WriteError("Not a squad or a squad member.");
            return;
        }

        EntityUid? Other()
        {
            return args.Length > 2 && NetEntity.TryParse(args[2], out var other) && EntityManager.TryGetEntity(other, out var otherUid) ? otherUid : null;
        }

        SquadTask? task = args[1] switch
        {
            "patrol" => new PatrolTask(),
            "hunt" => new HuntTask(),
            "guard" => squad.Leader is { } leader ? new GuardTask(EntityManager.GetComponent<TransformComponent>(leader).Coordinates, 8f) : null,
            "follow" => Other() is { } target ? new FollowTask(target) : null,
            "eliminate" => Other() is { } enemy ? new EliminateSquadTask(enemy) : null,
            _ => null,
        };

        if (task == null)
        {
            shell.WriteError("Unknown task, or it needs an entity id.");
            return;
        }

        _squads.SetTask((uid.Value, squad), task);
        shell.WriteLine($"{uid.Value} now has the task {task.Name}.");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 2
            ? CompletionResult.FromHintOptions(new[] { "patrol", "guard", "hunt", "follow", "eliminate" }.Select(o => new CompletionOption(o)), "<task>")
            : CompletionResult.Empty;
    }
}
