using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Gravity;
using Content.Shared._ERRORGATE.World;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._ERRORGATE.World;

/// <summary>
///     Builds and tears down the locations of the world (docs/design/world.md). A location is a map of its own: a ground grid with
///     the terrain, roads and border, and one grid per structure of the theme's pool, placed by <see cref="LocationLayout"/>.
///     This is the first slice: build, tear down and measure. The storm timer, transitions and reputation come later.
/// </summary>
public sealed class WorldSystem : EntitySystem
{
    private const string StructureRoot = "/Maps/_ERRORGATE/Structures/";

    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly GravitySystem _gravity = default!;

    /// <summary>A built location and what it cost.</summary>
    public sealed class Location
    {
        public WorldLocationPrototype Proto = default!;
        public EntityUid Map;
        public MapId MapId;
        public int Seed;
        public int Structures;
        public double BuildMilliseconds;
        public int Entities;
    }

    private readonly Dictionary<string, Location> _locations = new();
    private readonly Dictionary<string, List<StructureInfo>> _pools = new();

    public IReadOnlyDictionary<string, Location> Locations => _locations;

    public bool IsBuilt(string id) => _locations.TryGetValue(id, out var loc) && !TerminatingOrDeleted(loc.Map);

    public bool TryBuild(string id, int? seed, out string error)
    {
        error = string.Empty;
        if (!_proto.TryIndex<WorldLocationPrototype>(id, out var proto))
        {
            error = $"no location '{id}'";
            return false;
        }

        Teardown(id);

        var pool = GetPool(proto.Theme);
        if (pool.Count == 0)
        {
            error = $"no structures in {StructureRoot}{proto.Theme}";
            return false;
        }

        var watch = Stopwatch.StartNew();
        var entitiesBefore = EntityManager.EntityCount;
        var useSeed = seed ?? _random.Next();
        var layout = LocationLayout.Generate(useSeed, proto.Size, pool, proto.Density);

        var mapUid = _map.CreateMap(out var mapId, runMapInit: true);
        _map.SetAmbientLight(mapId, proto.AmbientLight);

        // The air of the map reaches every grid on it that has no air of its own, like on a planet
        var gas = new GasMixture(Atmospherics.CellVolume) { Temperature = 293.15f };
        gas.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        gas.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        _atmos.SetMapAtmosphere(mapUid, false, gas);
        EnsureComp<GravityComponent>(mapUid);
        _gravity.EnableGravity(mapUid);

        // The ground: terrain, roads and the border
        var ground = _mapManager.CreateGridEntity(mapId);
        var groundTile = new Tile(_tileDefs[proto.Ground].TileId);
        var roadTile = new Tile(_tileDefs[proto.Road].TileId);
        var tiles = new List<(Vector2i, Tile)>(proto.Size.X * proto.Size.Y);
        for (var x = 0; x < proto.Size.X; x++)
        {
            for (var y = 0; y < proto.Size.Y; y++)
            {
                var pos = new Vector2i(x, y);
                tiles.Add((pos, layout.Roads.Contains(pos) ? roadTile : groundTile));
            }
        }

        _map.SetTiles(ground.Owner, ground.Comp, tiles);

        const int borderWidth = 2;
        for (var x = 0; x < proto.Size.X; x++)
        {
            for (var y = 0; y < proto.Size.Y; y++)
            {
                if (x >= borderWidth && y >= borderWidth && x < proto.Size.X - borderWidth && y < proto.Size.Y - borderWidth)
                    continue;

                Spawn(proto.Border, _map.GridTileToLocal(ground.Owner, ground.Comp, new Vector2i(x, y)));
            }
        }

        // The structures, each on a grid of its own
        var placed = 0;
        foreach (var p in layout.Structures)
        {
            if (!_loader.TryLoadGrid(mapId, new ResPath(p.Info.Path), out var grid))
            {
                Log.Error($"World: could not load structure {p.Info.Path}");
                continue;
            }

            // Where the grid origin goes so that the turned footprint starts at p.Min
            var rotation = Angle.FromDegrees(90 * p.Turns);
            var a = Rotate(p.Info.LocalMin, p.Turns);
            var b = Rotate(p.Info.LocalMin + new Vector2i(p.Info.Width, p.Info.Height), p.Turns);
            var rotatedMin = new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
            var origin = new Vector2(p.Min.X, p.Min.Y) - rotatedMin;
            _transform.SetWorldPositionRotation(grid.Value.Owner, origin, rotation);
            placed++;
        }

        watch.Stop();
        _locations[id] = new Location
        {
            Proto = proto,
            Map = mapUid,
            MapId = mapId,
            Seed = useSeed,
            Structures = placed,
            BuildMilliseconds = watch.Elapsed.TotalMilliseconds,
            Entities = EntityManager.EntityCount - entitiesBefore,
        };

        Log.Info($"World: built {id} (seed {useSeed}): {placed} structures, {_locations[id].Entities} entities, {watch.Elapsed.TotalMilliseconds:0} ms");
        return true;
    }

    public void Teardown(string id)
    {
        if (!_locations.Remove(id, out var loc))
            return;

        if (!TerminatingOrDeleted(loc.Map))
            QueueDel(loc.Map);
    }

    private static Vector2 Rotate(Vector2i v, int turns)
    {
        return (turns % 4) switch
        {
            1 => new Vector2(-v.Y, v.X),
            2 => new Vector2(-v.X, -v.Y),
            3 => new Vector2(v.Y, -v.X),
            _ => new Vector2(v.X, v.Y),
        };
    }

    /// <summary>The structure files of a theme with their measured footprints. Measured once, then kept.</summary>
    private List<StructureInfo> GetPool(string theme)
    {
        if (_pools.TryGetValue(theme, out var cached))
            return cached;

        var list = new List<StructureInfo>();
        var scratch = _map.CreateMap(out var scratchId, runMapInit: false);
        foreach (var path in _res.ContentFindFiles(new ResPath(StructureRoot + theme)).Where(p => p.Extension == "yml").OrderBy(p => p.ToString()))
        {
            if (!_loader.TryLoadGrid(scratchId, path, out var grid))
            {
                Log.Error($"World: could not measure structure {path}");
                continue;
            }

            var min = new Vector2i(int.MaxValue, int.MaxValue);
            var max = new Vector2i(int.MinValue, int.MinValue);
            foreach (var tile in _map.GetAllTiles(grid.Value.Owner, grid.Value.Comp))
            {
                min = new Vector2i(Math.Min(min.X, tile.GridIndices.X), Math.Min(min.Y, tile.GridIndices.Y));
                max = new Vector2i(Math.Max(max.X, tile.GridIndices.X), Math.Max(max.Y, tile.GridIndices.Y));
            }

            if (min.X <= max.X)
                list.Add(new StructureInfo(path.ToString(), max.X - min.X + 1, max.Y - min.Y + 1, min));

            QueueDel(grid.Value.Owner);
        }

        QueueDel(scratch);
        _pools[theme] = list;
        return list;
    }
}
