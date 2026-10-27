using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._ERRORGATE.Reputation;

/// <summary>Shows the standing of the player with every faction: <c>reputation</c>.</summary>
public sealed class ReputationCommand : LocalizedEntityCommands
{
    [Dependency] private readonly ReputationSystem _reputation = default!;

    public override string Command => "reputation";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        foreach (var line in _reputation.GetLines(player.UserId))
            shell.WriteLine(line);
    }
}

/// <summary>Sets (or with <c>add</c> moves) the standing of a player: <c>setreputation &lt;player&gt; &lt;faction&gt; &lt;value&gt; [add]</c>.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class SetReputationCommand : LocalizedEntityCommands
{
    [Dependency] private readonly ReputationSystem _reputation = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override string Command => "setreputation";
    public override string Description => "Sets the standing of a player with a faction (Military, Bandit) from -1000 to 1000. With 'add' the value is added instead.";
    public override string Help => "setreputation <player> <faction> <value> [add]";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[2], out var value))
        {
            shell.WriteError(Help);
            return;
        }

        if (!_players.TryGetSessionByUsername(args[0], out var session))
        {
            shell.WriteError($"No player named {args[0]} is connected.");
            return;
        }

        var faction = Array.Find(ReputationSystem.Factions, f => f.Equals(args[1], StringComparison.OrdinalIgnoreCase));
        if (faction == null)
        {
            shell.WriteError($"Unknown faction {args[1]}. Known: {string.Join(", ", ReputationSystem.Factions)}.");
            return;
        }

        var add = args.Length > 3 && args[3].Equals("add", StringComparison.OrdinalIgnoreCase);
        var result = add ? _reputation.Adjust(session.UserId, faction, value) : _reputation.Set(session.UserId, faction, value);
        shell.WriteLine($"{session.Name}: {faction} {result}.");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "<player>"),
            2 => CompletionResult.FromOptions(ReputationSystem.Factions),
            3 => CompletionResult.FromHint("<value>"),
            4 => CompletionResult.FromOptions(["add"]),
            _ => CompletionResult.Empty,
        };
    }
}
