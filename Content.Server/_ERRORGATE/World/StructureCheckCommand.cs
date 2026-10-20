using System.Linq;
using Content.Server.Administration;
using Content.Server.Atmos.Components;
using Content.Shared._ERRORGATE.World;
using Content.Shared.Administration;
using Content.Shared.Doors.Components;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._ERRORGATE.World;

/// <summary>
///     Checks structure files against the rules of docs/design/structures.md and says what is wrong with them:
///     <c>structurecheck village/kuz-wood-XL-3</c>, or without a name for the whole pool (not the review folder).
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed class StructureCheckCommand : LocalizedEntityCommands
{
    private const string Root = "/Maps/_ERRORGATE/Structures/";

    private static readonly (string Name, int W, int H)[] Classes =
    {
        ("XS", 5, 5), ("S", 8, 6), ("M", 10, 8), ("L", 14, 10), ("XL", 18, 14), ("XXL", 24, 18), ("XXXL", 40, 30),
    };

    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    public override string Command => "structurecheck";
    public override string Description => "Checks structure files against the building rules: structurecheck [theme/name]. No name checks the whole pool.";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(_res.ContentFindFiles(new ResPath(Root)).Where(p => p.Extension == "yml").Select(p => p.ToString()[Root.Length..^4]).OrderBy(p => p), "[theme/name]")
            : CompletionResult.Empty;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var files = new List<ResPath>();
        if (args.Length > 0)
        {
            files.Add(new ResPath(Root + args[0].Trim('/') + ".yml"));
        }
        else
        {
            foreach (var theme in new[] { "village", "city", "bunker", "tunnel" })
            {
                files.AddRange(_res.ContentFindFiles(new ResPath(Root + theme)).Where(p => p.Extension == "yml").OrderBy(p => p.ToString()));
            }
        }

        var problems = 0;
        foreach (var file in files)
        {
            var report = Check(file);
            problems += report.Count(l => l.Contains("PROBLEM"));
            shell.WriteLine(string.Join("\n", report));
            foreach (var line in report)
            {
                Logger.GetSawmill("structure").Info(line); // the server log keeps the report too
            }
        }

        shell.WriteLine($"{files.Count} file(s) checked, {problems} problem(s).");
    }

    private List<string> Check(ResPath file)
    {
        var name = file.ToString()[Root.Length..^4];
        var lines = new List<string>();
        var mapUid = _map.CreateMap(out var mapId, runMapInit: false);
        try
        {
            if (!_loader.TryLoadGrid(mapId, file, out var grid))
            {
                lines.Add($"{name}: PROBLEM it does not load as a single grid");
                return lines;
            }

            var gridUid = grid.Value.Owner;
            var tiles = _map.GetAllTiles(gridUid, grid.Value.Comp).ToList();
            if (tiles.Count == 0)
            {
                lines.Add($"{name}: PROBLEM it has no tiles");
                return lines;
            }

            var min = new Vector2i(tiles.Min(t => t.GridIndices.X), tiles.Min(t => t.GridIndices.Y));
            var max = new Vector2i(tiles.Max(t => t.GridIndices.X), tiles.Max(t => t.GridIndices.Y));
            var w = max.X - min.X + 1;
            var h = max.Y - min.Y + 1;
            var cls = Classes.FirstOrDefault(c => (w <= c.W && h <= c.H) || (h <= c.W && w <= c.H)).Name ?? "LANDMARK (too big)";
            var theme = name.Split('/')[0];
            var id = $"{theme}-{name.Split('/')[^1]}";
            var role = _proto.TryIndex<WorldStructurePrototype>(id, out var structure) ? structure.Role : (w * h <= 48 ? "outbuilding (guessed)" : "house (guessed)");
            lines.Add($"{name}: {w} x {h}, class {cls}, role {role}");

            if (min != Vector2i.Zero)
                lines.Add($"  note: the footprint starts at {min}, not at (0, 0); the generator copes, but build from (0, 0)");

            var solid = new HashSet<Vector2i>();
            var doors = new List<Vector2i>();
            var tilesSet = tiles.Select(t => t.GridIndices).ToHashSet();
            int items = 0, mobs = 0, badLoot = 0, fences = 0, floating = 0, snow = 0;
            var enumerator = EntityManager.GetComponent<TransformComponent>(gridUid).ChildEnumerator;
            while (enumerator.MoveNext(out var child))
            {
                var xform = EntityManager.GetComponent<TransformComponent>(child);
                var tile = _map.TileIndicesFor(gridUid, grid.Value.Comp, xform.Coordinates);
                var proto = EntityManager.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID ?? "";

                if (EntityManager.HasComponent<DoorComponent>(child) && xform.Anchored)
                    doors.Add(tile);
                else if (EntityManager.TryGetComponent<AirtightComponent>(child, out var air) && air.AirBlocked && xform.Anchored)
                    solid.Add(tile);

                if (EntityManager.HasComponent<MobStateComponent>(child) || proto.StartsWith("MobSpawner"))
                    mobs++;
                else if (!xform.Anchored && EntityManager.HasComponent<ItemComponent>(child))
                    items++;

                if (proto.StartsWith("LootSpawner") && !proto.EndsWith("Tier1"))
                    badLoot++;

                if (proto.Contains("Fence"))
                    fences++;

                if (xform.Anchored && !tilesSet.Contains(tile))
                    floating++;
            }

            foreach (var t in tiles)
            {
                if (theme == "village" && _tileDefs[t.Tile.TypeId].ID is var tid && (tid.Contains("Snow") || tid.Contains("Ice")))
                    snow++;
            }

            // The walls: every side of the footprint should have walls or doors on it (no apron of bare floor)
            var sides = new[]
            {
                ("south", Enumerable.Range(min.X, w).Select(x => new Vector2i(x, min.Y))),
                ("east", Enumerable.Range(min.Y, h).Select(y => new Vector2i(max.X, y))),
                ("north", Enumerable.Range(min.X, w).Select(x => new Vector2i(x, max.Y))),
                ("west", Enumerable.Range(min.Y, h).Select(y => new Vector2i(min.X, y))),
            };

            var doorCounts = new int[4];
            for (var i = 0; i < 4; i++)
            {
                var edge = sides[i].Item2.ToList();
                var covered = edge.Count(t => solid.Contains(t) || doors.Contains(t));
                doorCounts[i] = edge.Count(t => doors.Contains(t));
                if (covered * 2 < edge.Count)
                    lines.Add($"  PROBLEM the {sides[i].Item1} side is mostly bare floor ({covered} of {edge.Count} tiles are wall or door): an apron around the walls?");
            }

            if (doorCounts.Sum() == 0 && structure is { OpenFront: not "" })
                lines.Add("  no door on the edge, but the structure is marked openFront: the front is the widest doorless gap on its " + structure!.OpenFront + " side");
            else if (doorCounts.Sum() == 0)
                lines.Add("  PROBLEM no door on the edge of the footprint: the generator needs a front door on the outer wall (build facing south: the door on the south side)");
            else
                lines.Add($"  doors on the edge: south {doorCounts[0]}, east {doorCounts[1]}, north {doorCounts[2]}, west {doorCounts[3]}; the front is the side with most (south on a tie)");

            if (mobs > 0)
                lines.Add($"  PROBLEM {mobs} mob(s) or mob spawner(s): remove them, the location places its own");

            if (items > 0)
                lines.Add($"  PROBLEM {items} loose item(s) lying about: remove them, loot comes from loot spawners");

            if (badLoot > 0)
                lines.Add($"  PROBLEM {badLoot} loot spawner(s) of a tier other than 1: use the tier 1 ones, the location sets the tier");

            if (fences > 0)
                lines.Add($"  PROBLEM {fences} fence piece(s): the generator builds fences around a lot, leave them out");

            if (floating > 0)
                lines.Add($"  PROBLEM {floating} anchored thing(s) with no floor under them");

            if (snow > 0)
                lines.Add($"  PROBLEM {snow} snow or ice tile(s) in a village structure");

            if (lines.Count == 1 || lines.All(l => !l.Contains("PROBLEM")))
                lines.Add("  ok");
        }
        finally
        {
            _map.DeleteMap(mapId);
        }

        return lines;
    }
}
