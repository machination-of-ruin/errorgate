using System.Linq;
using Content.Server.Administration;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Clears and places the world faults again. Without an argument every anomaly field is redone,
///     with a grid or map uid only that one (a default field is added to it if it has none).
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class SpawnAnomaliesCommand : LocalizedEntityCommands
{
    [Dependency] private readonly AnomalyFieldSystem _field = default!;

    public override string Command => "spawnanomalies";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Loc.GetString("cmd-spawnanomalies-invalid-args"));
            return;
        }

        if (args.Length == 1)
        {
            if (!NetEntity.TryParse(args[0], out var netEntity)
                || !EntityManager.TryGetEntity(netEntity, out var target)
                || !EntityManager.EntityExists(target))
            {
                shell.WriteError(Loc.GetString("cmd-spawnanomalies-no-entity", ("entity", args[0])));
                return;
            }

            if (!EntityManager.HasComponent<MapGridComponent>(target) && !EntityManager.HasComponent<MapComponent>(target))
            {
                shell.WriteError(Loc.GetString("cmd-spawnanomalies-not-grid", ("entity", args[0])));
                return;
            }

            var field = EntityManager.EnsureComponent<AnomalyFieldComponent>(target.Value);
            Respawn(shell, target.Value, field);
            return;
        }

        var any = false;
        var query = EntityManager.EntityQueryEnumerator<AnomalyFieldComponent>();
        var fields = new List<(EntityUid, AnomalyFieldComponent)>();
        while (query.MoveNext(out var uid, out var comp))
        {
            fields.Add((uid, comp));
        }

        foreach (var (uid, comp) in fields)
        {
            any = true;
            Respawn(shell, uid, comp);
        }

        if (!any)
            shell.WriteError(Loc.GetString("cmd-spawnanomalies-no-field"));
    }

    private void Respawn(IConsoleShell shell, EntityUid uid, AnomalyFieldComponent field)
    {
        var count = _field.SpawnAnomalies(uid, field);
        shell.WriteLine(Loc.GetString("cmd-spawnanomalies-done", ("entity", EntityManager.ToPrettyString(uid).ToString()), ("count", count)));
    }
}

/// <summary>
///     Prints where every world fault is.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ListAnomaliesCommand : LocalizedEntityCommands
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override string Command => "listanomalies";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var count = 0;
        var query = EntityManager.EntityQueryEnumerator<ErrorgateAnomalyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anomaly, out var xform))
        {
            count++;
            var position = _transform.GetMapCoordinates(uid, xform);
            shell.WriteLine(Loc.GetString("cmd-listanomalies-line",
                ("kind", anomaly.Kind.ToString()),
                ("entity", EntityManager.ToPrettyString(uid).ToString()),
                ("map", position.MapId.ToString()),
                ("x", position.X.ToString("0.0")),
                ("y", position.Y.ToString("0.0"))));
        }

        if (count == 0)
            shell.WriteLine(Loc.GetString("cmd-listanomalies-none"));
    }
}
