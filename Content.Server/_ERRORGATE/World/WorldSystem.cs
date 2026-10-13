using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Gravity;
using Content.Shared._ERRORGATE.World;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
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
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly TileSystem _tile = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

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

        var watch = System.Diagnostics.Stopwatch.StartNew();
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
        EnsureComp<GridAtmosphereComponent>(ground.Owner); // the map air only reaches a grid that has an atmosphere
        var groundDef = (ContentTileDefinition) _tileDefs[proto.Ground];
        var roadDef = (ContentTileDefinition) _tileDefs[proto.Road];
        var tiles = new List<(Vector2i, Tile)>(proto.Size.X * proto.Size.Y);
        for (var x = 0; x < proto.Size.X; x++)
        {
            for (var y = 0; y < proto.Size.Y; y++)
            {
                // Every tile picks its own variant, so the ground does not look like a pattern
                var pos = new Vector2i(x, y);
                var def = layout.Roads.Contains(pos) ? roadDef : groundDef;
                tiles.Add((pos, new Tile(def.TileId, 0, _tile.PickVariant(def))));
            }
        }

        _map.SetTiles(ground.Owner, ground.Comp, tiles);
        _physics.SetBodyType(ground.Owner, BodyType.Static);

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

            // A grid is a dynamic body by default and would be pushed around by the ground grid and the others it overlaps
            _physics.SetBodyType(grid.Value.Owner, BodyType.Static);

            // The ground under the building is cut away, so the two grids never overlap
            var structureTiles = new List<Vector2i>();
            var holes = new List<(Vector2i, Tile)>();
            foreach (var t in _map.GetAllTiles(grid.Value.Owner, grid.Value.Comp))
            {
                structureTiles.Add(t.GridIndices);
                var worldCentre = RotateVector(new Vector2(t.GridIndices.X + 0.5f, t.GridIndices.Y + 0.5f), p.Turns) + origin;
                holes.Add((new Vector2i((int) MathF.Floor(worldCentre.X), (int) MathF.Floor(worldCentre.Y)), Tile.Empty));
            }

            _map.SetTiles(ground.Owner, ground.Comp, holes);
            EnsureComp<GridAtmosphereComponent>(grid.Value.Owner);
            _pendingAir.Add(new PendingAir(grid.Value.Owner, structureTiles, _timing.CurTime + TimeSpan.FromSeconds(120)));
            placed++;
        }

        var groundTiles = _map.GetAllTiles(ground.Owner, ground.Comp).Select(t => t.GridIndices).ToList();
        _pendingAir.Add(new PendingAir(ground.Owner, groundTiles, _timing.CurTime + TimeSpan.FromSeconds(120)));

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

    private static Vector2 Rotate(Vector2i v, int turns) => RotateVector(new Vector2(v.X, v.Y), turns);

    private static Vector2 RotateVector(Vector2 v, int turns)
    {
        return (turns % 4) switch
        {
            1 => new Vector2(-v.Y, v.X),
            2 => new Vector2(-v.X, -v.Y),
            3 => new Vector2(v.Y, -v.X),
            _ => v,
        };
    }

    // -- Air ------------------------------------------------------------------------------------------------------

    private sealed record PendingAir(EntityUid Grid, List<Vector2i> Tiles, TimeSpan GiveUp);

    private readonly List<PendingAir> _pendingAir = new();

    /// <summary>
    ///     A new grid's tiles get their air from the atmosphere system a little after the grid exists, so the breathable air is put
    ///     into them as they appear, a few thousand tiles a tick at most.
    /// </summary>
    public override void Update(float frameTime)
    {
        if (_pendingAir.Count == 0)
            return;

        var budget = 6000;
        for (var g = _pendingAir.Count - 1; g >= 0 && budget > 0; g--)
        {
            var pending = _pendingAir[g];
            if (TerminatingOrDeleted(pending.Grid) || _timing.CurTime > pending.GiveUp)
            {
                if (!TerminatingOrDeleted(pending.Grid) && pending.Tiles.Count > 0)
                    Log.Warning($"World: the air was not put into {pending.Tiles.Count} tiles of {ToPrettyString(pending.Grid)} in time");

                _pendingAir.RemoveAt(g);
                continue;
            }

            for (var i = pending.Tiles.Count - 1; i >= 0 && budget > 0; i--)
            {
                budget--;
                if (_atmos.GetTileMixture((pending.Grid, null, null), null, pending.Tiles[i]) is not { Immutable: false } air)
                    continue;

                air.Clear();
                air.Temperature = 293.15f;
                air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
                pending.Tiles.RemoveAt(i);
            }

            if (pending.Tiles.Count == 0)
                _pendingAir.RemoveAt(g);
        }
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
