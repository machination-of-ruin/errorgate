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
///     A number sets the total count of faults wanted.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class SpawnAnomaliesCommand : LocalizedEntityCommands
{
    [Dependency] private readonly AnomalyFieldSystem _field = default!;

    public override string Command => "spawnanomalies";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteError(Loc.GetString("cmd-spawnanomalies-invalid-args"));
            return;
        }

        EntityUid? target = null;
        int? count = null;

        // spawnanomalies [grid] [count], or spawnanomalies [count]
        foreach (var arg in args)
        {
            if (target == null && TryGetGrid(arg, out var grid))
            {
                target = grid;
                continue;
            }

            if (count == null && int.TryParse(arg, out var parsed) && parsed >= 0)
            {
                count = parsed;
                continue;
            }

            shell.WriteError(Loc.GetString("cmd-spawnanomalies-no-entity", ("entity", arg)));
            return;
        }

        if (target is { } uid)
        {
            Respawn(shell, uid, EntityManager.EnsureComponent<AnomalyFieldComponent>(uid), count);
            return;
        }

        var fields = new List<(EntityUid, AnomalyFieldComponent)>();
        var query = EntityManager.EntityQueryEnumerator<AnomalyFieldComponent>();
        while (query.MoveNext(out var fieldUid, out var comp))
        {
            fields.Add((fieldUid, comp));
        }

        if (fields.Count == 0)
        {
            shell.WriteError(Loc.GetString("cmd-spawnanomalies-no-field"));
            return;
        }

        foreach (var (fieldUid, comp) in fields)
        {
            Respawn(shell, fieldUid, comp, count);
        }
    }

    private bool TryGetGrid(string arg, out EntityUid grid)
    {
        grid = default;

        if (!NetEntity.TryParse(arg, out var netEntity)
            || !EntityManager.TryGetEntity(netEntity, out var entity)
            || !EntityManager.EntityExists(entity)
            || !EntityManager.HasComponent<MapGridComponent>(entity) && !EntityManager.HasComponent<MapComponent>(entity))
        {
            return false;
        }

        grid = entity.Value;
        return true;
    }

    private void Respawn(IConsoleShell shell, EntityUid uid, AnomalyFieldComponent field, int? count)
    {
        if (count != null)
            field.Count = count.Value;

        var placed = _field.SpawnAnomalies(uid, field);
        shell.WriteLine(Loc.GetString("cmd-spawnanomalies-done",
            ("entity", EntityManager.ToPrettyString(uid).ToString()),
            ("count", placed),
            ("packs", field.Packs.Count)));
    }
}

/// <summary>
///     Prints where every world fault is, grouped by pack.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ListAnomaliesCommand : LocalizedEntityCommands
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override string Command => "listanomalies";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var listed = new HashSet<EntityUid>();

        var fieldQuery = EntityManager.EntityQueryEnumerator<AnomalyFieldComponent>();
        while (fieldQuery.MoveNext(out var fieldUid, out var field))
        {
            for (var i = 0; i < field.Packs.Count; i++)
            {
                var pack = field.Packs[i];
                shell.WriteLine(Loc.GetString("cmd-listanomalies-pack",
                    ("field", EntityManager.ToPrettyString(fieldUid).ToString()),
                    ("index", i),
                    ("proto", pack.Proto.Id),
                    ("count", pack.Members.Count),
                    ("x", pack.Center.X.ToString("0.0")),
                    ("y", pack.Center.Y.ToString("0.0")),
                    ("radius", pack.Radius.ToString("0.0")),
                    ("passable", pack.Passable ? "PASSABLE" : "CLOSED")));

                foreach (var member in pack.Members)
                {
                    if (EntityManager.TryGetComponent(member, out ErrorgateAnomalyComponent? anomaly)
                        && listed.Add(member))
                    {
                        WriteFault(shell, member, anomaly);
                    }
                }
            }
        }

        // Faults that no field placed
        var count = listed.Count;
        var query = EntityManager.EntityQueryEnumerator<ErrorgateAnomalyComponent>();
        while (query.MoveNext(out var uid, out var anomaly))
        {
            if (!listed.Add(uid))
                continue;

            count++;
            WriteFault(shell, uid, anomaly);
        }

        if (count == 0)
            shell.WriteLine(Loc.GetString("cmd-listanomalies-none"));
    }

    private void WriteFault(IConsoleShell shell, EntityUid uid, ErrorgateAnomalyComponent anomaly)
    {
        var position = _transform.GetMapCoordinates(uid);
        shell.WriteLine(Loc.GetString("cmd-listanomalies-line",
            ("kind", anomaly.Kind.ToString()),
            ("entity", EntityManager.ToPrettyString(uid).ToString()),
            ("map", position.MapId.ToString()),
            ("x", position.X.ToString("0.0")),
            ("y", position.Y.ToString("0.0"))));
    }
}
