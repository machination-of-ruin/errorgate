using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server._ERRORGATE.Anomalies;
using Content.Server._ERRORGATE.LootManager;
using Content.Server.Decals;
using Content.Server.Gravity;
using Content.Shared._ERRORGATE.World;
using Content.Shared.Atmos;
using Content.Shared.Buckle.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Wall;
using Content.Shared.Weather;
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
public sealed partial class WorldSystem : EntitySystem
{
    private const string StructureRoot = "/Maps/_ERRORGATE/Structures/";

    /// <summary>One day, in seconds, the same in every location (Kuznetsk used 1200).</summary>
    public const int DayCycleSeconds = 1200;

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
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly SharedWeatherSystem _weather = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly Content.Server.Chat.Systems.ChatSystem _chat = default!;

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

        /// <summary>Every structure built and its grid, for the tools and tests.</summary>
        public List<(EntityUid Grid, StructurePlacement Placement)> Built = new();

        public LocationLayout? Layout;

        /// <summary>The ground grid (the one that holds the terrain), for the round system.</summary>
        public EntityUid Ground;

        public TunnelLayout? Tunnel;

        /// <summary>The airlock of a laboratory and where people arrive in it.</summary>
        public EntityUid? Vestibule;

        public EntityCoordinates? VestibuleArrival;

        /// <summary>The bunker hatch at the end of a tunnel that leads to a laboratory.</summary>
        public EntityUid? Hatch;
    }

    private readonly Dictionary<string, Location> _locations = new();
    private readonly Dictionary<string, List<StructureInfo>> _pools = new();

    public IReadOnlyDictionary<string, Location> Locations => _locations;

    public bool IsBuilt(string id) => _locations.TryGetValue(id, out var loc) && !TerminatingOrDeleted(loc.Map);

    /// <summary>The state and the result of a build that runs in steps.</summary>
    private sealed class BuildContext
    {
        public bool Ok;
        public string Error = string.Empty;

        /// <summary>The map the build has made so far, which goes if the build is cancelled.</summary>
        public EntityUid? Map;
    }

    private sealed record BuildJob(string Id, IEnumerator<bool> Steps, BuildContext Context);

    private readonly List<BuildJob> _jobs = new();
    private readonly HashSet<string> _building = new();

    /// <summary>The time per tick a build that runs over ticks may take.</summary>
    private const int JobBudgetMilliseconds = 6;

    /// <summary>Builds a location now, in one go. Anything built under the same name is torn down first.</summary>
    public bool TryBuild(string id, int? seed, out string error)
    {
        var context = new BuildContext();
        // A build running over ticks is finished first, so nothing is left half built
        if (_jobs.Find(j => j.Id == id) is { } running)
        {
            while (running.Steps.MoveNext())
            {
            }

            _jobs.Remove(running);
            _building.Remove(id);
        }

        var steps = BuildSteps(id, seed, context);
        while (steps.MoveNext())
        {
        }

        error = context.Error;
        return context.Ok;
    }

    /// <summary>
    ///     Builds a location over the next ticks, so the server does not stall (the storm does this: the old location is gone at once,
    ///     the new one appears when it is finished and nobody can come into it before).
    /// </summary>
    public void QueueBuild(string id, int? seed)
    {
        if (!_building.Add(id))
            return;

        TeardownBuilt(id);
        var context = new BuildContext();
        _jobs.Add(new BuildJob(id, BuildSteps(id, seed, context), context));
    }

    public bool IsBuilding(string id) => _building.Contains(id);

    private void RunJobs()
    {
        if (_jobs.Count == 0)
            return;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (_jobs.Count > 0 && watch.ElapsedMilliseconds < JobBudgetMilliseconds)
        {
            var job = _jobs[0];
            if (job.Steps.MoveNext())
                continue;

            _jobs.RemoveAt(0);
            _building.Remove(job.Id);
            if (!job.Context.Ok)
                Log.Error($"World: could not build {job.Id} again: {job.Context.Error}");
        }
    }

    private IEnumerator<bool> BuildSteps(string id, int? seed, BuildContext context)
    {
        if (!_proto.TryIndex<WorldLocationPrototype>(id, out var proto))
        {
            context.Error = $"no location '{id}'";
            yield break;
        }

        TeardownBuilt(id);

        var gates = GatesOf(id);
        if (proto.MapPath is { } mapPath && _res.ContentFileExists(new ResPath(mapPath)))
        {
            context.Ok = BuildFromMap(proto, seed, new ResPath(mapPath), out var mapError);
            context.Error = mapError;
            yield break;
        }

        if (proto.Style == "tunnel")
        {
            context.Ok = BuildTunnel(proto, seed, gates, out var tunnelError);
            context.Error = tunnelError;
            yield break;
        }

        if (proto.Style == "laboratory")
        {
            context.Ok = BuildLaboratory(proto, seed, gates, out var labError);
            context.Error = labError;
            yield break;
        }

        var pool = GetPool(proto.Theme);
        if (pool.Count == 0)
        {
            context.Error = $"no structures in {StructureRoot}{proto.Theme}";
            yield break;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var entitiesBefore = EntityManager.EntityCount;
        var useSeed = seed ?? _random.Next();
        var options = new LocationLayout.Options(proto.Style, proto.Shape, proto.Rocks, proto.TreeClumps, proto.TreeSingles, proto.Shore,
            proto.Props.Select(e => (e.Count, e.Clump)).ToList(), proto.Decals.Select(e => (e.Count, e.Clump)).ToList(), gates, proto.Mobs.Count > 0 ? proto.MobCount : 0, proto.SpawnZone, proto.RoadblockProtos.Count > 0 ? proto.Roadblocks : 0);
        var layout = LocationLayout.Generate(useSeed, proto.Size, pool, proto.Density, options);

        yield return true;

        var mapUid = CreateLocationMap(proto, out var mapId);
        context.Map = mapUid;

        // The ground: terrain, roads and the border
        var ground = _mapManager.CreateGridEntity(mapId);
        _meta.SetEntityName(ground.Owner, $"{id} ground");
        // No air of its own: outdoors the air is the map's (like on a planet), at once and everywhere, nothing to leak or to wait for
        var groundDef = (ContentTileDefinition) _tileDefs[proto.Ground];
        var roadDef = (ContentTileDefinition) _tileDefs[proto.Road];
        var sandDef = (ContentTileDefinition) _tileDefs[proto.Sand];
        var iceDef = (ContentTileDefinition) _tileDefs[proto.IceTile];
        var walkDef = (ContentTileDefinition) _tileDefs[proto.Walkway];
        var tiles = new List<(Vector2i, Tile)>(proto.Size.X * proto.Size.Y);
        foreach (var pos in layout.ShapeTiles())
        {
            // Every tile picks its own variant, so the ground does not look like a pattern
            var def = layout.Terrain.TryGetValue(pos, out var shore)
                ? (shore == LocationLayout.ShoreTile.Ice ? iceDef : sandDef)
                : layout.Roads.Contains(pos) ? roadDef : layout.Walks.Contains(pos) ? walkDef : groundDef;
            tiles.Add((pos, new Tile(def.TileId, 0, _tile.PickVariant(def))));
        }

        _map.SetTiles(ground.Owner, ground.Comp, tiles);
        _physics.SetBodyType(ground.Owner, BodyType.Static);
        var offset = CentreOnOrigin(ground);
        yield return true;

        // The border is the two outermost tiles of the shape
        var spawned = 0;
        foreach (var pos in layout.ShapeTiles())
        {
            if (layout.Dist(pos) > 2)
                continue;

            Spawn(proto.Border, _map.GridTileToLocal(ground.Owner, ground.Comp, pos));
            if (++spawned % 96 == 0)
                yield return true;
        }

        // The structures, each on a grid of its own
        var placed = 0;
        var built = new List<(EntityUid, StructurePlacement)>();
        foreach (var p in layout.Structures)
        {
            if (!_loader.TryLoadGrid(mapId, new ResPath(p.Info.Path), out var grid))
            {
                Log.Error($"World: could not load structure {p.Info.Path}");
                continue;
            }

            // The grid itself stays upright, so a camera aligned with the grid matches the ground. A turned structure has its content
            // turned: tiles, positions and the way entities face. The layout says where the grid origin goes.
            _meta.SetEntityName(grid.Value.Owner, $"{id} {p.Info.Path.Split('/')[^1].Replace(".yml", string.Empty)}");
            RotateContent(grid.Value, p.Turns);
            RemComp<GridAtmosphereComponent>(grid.Value.Owner); // the air is the map's, a file may have saved one
            RetierLoot(grid.Value, proto.Tier);
            var origin = p.Origin;
            _transform.SetWorldPositionRotation(grid.Value.Owner, origin + offset, Angle.Zero);

            // A grid is a dynamic body by default and would be pushed around by the ground grid and the others it overlaps
            _physics.SetBodyType(grid.Value.Owner, BodyType.Static);

            // The ground under the building is cut away, so the two grids never overlap
            var holes = new List<(Vector2i, Tile)>();
            foreach (var t in _map.GetAllTiles(grid.Value.Owner, grid.Value.Comp))
            {
                var worldCentre = new Vector2(t.GridIndices.X + 0.5f, t.GridIndices.Y + 0.5f) + origin;
                holes.Add((new Vector2i((int) MathF.Floor(worldCentre.X), (int) MathF.Floor(worldCentre.Y)), Tile.Empty));
            }

            _map.SetTiles(ground.Owner, ground.Comp, holes);
            placed++;
            built.Add((grid.Value.Owner, p));
            yield return true;
        }

        // The water lies on the shallow part of the shore
        spawned = 0;
        foreach (var (pos, kind) in layout.Terrain)
        {
            if (kind != LocationLayout.ShoreTile.Water)
                continue;

            Spawn(proto.Water, _map.GridTileToLocal(ground.Owner, ground.Comp, pos));
            if (++spawned % 96 == 0)
                yield return true;
        }

        spawned = 0;
        // The fences around the lots; the rotation says which way each piece connects (see LocationLayout.AddFence)
        foreach (var piece in layout.Fences)
        {
            var fenceProto = piece.Kind switch
            {
                FenceKind.Corner => proto.FenceCorner,
                FenceKind.Gate => proto.FenceGate,
                _ => proto.FenceStraight,
            };

            var fence = Spawn(fenceProto, _map.GridTileToLocal(ground.Owner, ground.Comp, piece.Tile));
            if (piece.Quarter != 0)
                _transform.SetLocalRotation(fence, Angle.FromDegrees(90 * piece.Quarter));

            if (++spawned % 96 == 0)
                yield return true;
        }

        yield return true;

        // Rock formations and trees in the free ground
        spawned = 0;
        foreach (var rock in layout.Rocks)
        {
            Spawn(proto.Border, _map.GridTileToLocal(ground.Owner, ground.Comp, rock));
        }

        if (proto.Trees.Count > 0)
        {
            var treeRng = new Random(useSeed ^ 0x5EED);
            foreach (var tree in layout.Trees.Concat(layout.StreetTrees))
            {
                var spot = _map.GridTileToLocal(ground.Owner, ground.Comp, tree);
                Spawn(proto.Trees[treeRng.Next(proto.Trees.Count)], spot);
                if (++spawned % 96 == 0)
                    yield return true;
            }
        }

        if (proto.Post is { } post)
        {
            foreach (var tile in layout.Posts)
            {
                Spawn(post, _map.GridTileToLocal(ground.Owner, ground.Comp, tile));
            }
        }

        // Roadblocks across the wide avenues
        var blockRng = new Random(useSeed ^ 0x40AD);
        foreach (var tile in layout.RoadblockTiles)
        {
            var kind = proto.RoadblockProtos[blockRng.Next(proto.RoadblockProtos.Count)];
            TurnBarricade(Spawn(kind, _map.GridTileToLocal(ground.Owner, ground.Comp, tile)), kind, layout.RoadblockVertical.Contains(tile));
        }

        // Boulders, barricades and the like, and decals of grass and brushwood
        var propRng = new Random(useSeed ^ 0x9209);
        for (var i = 0; i < proto.Props.Count && i < layout.PropTiles.Count; i++)
        {
            foreach (var pos in layout.PropTiles[i])
            {
                Spawn(proto.Props[i].Protos[propRng.Next(proto.Props[i].Protos.Count)], _map.GridTileToLocal(ground.Owner, ground.Comp, pos));
            }
        }

        yield return true;

        for (var i = 0; i < proto.Decals.Count && i < layout.DecalTiles.Count; i++)
        {
            foreach (var pos in layout.DecalTiles[i])
            {
                var coords = _map.GridTileToLocal(ground.Owner, ground.Comp, pos);
                _decals.TryAddDecal(proto.Decals[i].Decals[propRng.Next(proto.Decals[i].Decals.Count)], coords, out _);
            }
        }

        yield return true;

        // Mobs on the open ground, and the spawn points of the clearing where players come into the world
        var mobRng = new Random(useSeed ^ 0x3B0B);
        foreach (var tile in layout.MobTiles)
        {
            Spawn(proto.Mobs[mobRng.Next(proto.Mobs.Count)], _map.GridTileToLocal(ground.Owner, ground.Comp, tile));
        }

        for (var i = 0; i < layout.SpawnTiles.Count; i++)
        {
            Spawn(i < 6 ? "SpawnPointHumanError" : "SpawnPointLatejoin", _map.GridTileToLocal(ground.Owner, ground.Comp, layout.SpawnTiles[i]));
        }

        // The gates, each on a plaza of its own
        var plazas = new List<EntityUid>();
        foreach (var (gateId, place) in layout.GatePlaces)
        {
            plazas.Add(PlaceGatePlaza(id, gateId, ground, mapId, place));
        }

        // The faults go in last, when the ground, the buildings and the loot are all there: the field places them on the next update
        if (proto.Faults > 0)
            EnsureComp<AnomalyFieldComponent>(mapUid).Count = proto.Faults;

        // The air is the map's. Grids that grow get an atmosphere of their own, empty at first and slow to fill: take it off
        RemComp<GridAtmosphereComponent>(ground.Owner);
        foreach (var (structure, _) in built)
        {
            RemComp<GridAtmosphereComponent>(structure);
        }

        foreach (var plaza in plazas)
        {
            RemComp<GridAtmosphereComponent>(plaza);
        }

        watch.Stop();
        _locations[id] = new Location
        {
            Proto = proto,
            Map = mapUid,
            MapId = mapId,
            Seed = useSeed,
            Structures = placed,
            Built = built,
            Layout = layout,
            Ground = ground.Owner,
            BuildMilliseconds = watch.Elapsed.TotalMilliseconds,
            Entities = EntityManager.EntityCount - entitiesBefore,
        };

        Log.Info($"World: built {id} (seed {useSeed}): {placed} structures, {_locations[id].Entities} entities, {watch.Elapsed.TotalMilliseconds:0} ms");
        context.Ok = true;
    }


    /// <summary>
    ///     Moves a ground grid so the middle of its tiles lies at the origin of the map (tile numbers stay as they are). Everything built on
    ///     the grid, the plazas and the structures included, follows the same offset. Returns the offset.
    /// </summary>
    private Vector2 CentreOnOrigin(Entity<MapGridComponent> ground)
    {
        var centre = ground.Comp.LocalAABB.Center;
        var offset = new Vector2(-MathF.Round(centre.X), -MathF.Round(centre.Y));
        _transform.SetWorldPosition(ground.Owner, offset);
        return offset;
    }

    /// <summary>Removes a location, and stops its build if it is still running.</summary>
    public void Teardown(string id)
    {
        if (_jobs.Find(j => j.Id == id) is { } job)
        {
            _jobs.Remove(job);
            _building.Remove(id);
            if (job.Context.Map is { } partial && !TerminatingOrDeleted(partial))
                QueueDel(partial);
        }

        TeardownBuilt(id);
    }

    private void TeardownBuilt(string id)
    {
        ForgetGates(id);
        if (!_locations.Remove(id, out var loc))
            return;

        if (!TerminatingOrDeleted(loc.Map))
            QueueDel(loc.Map);
    }

    /// <summary>The loot spawners of a structure are tier 1 in the file; here they take the tier of the location.</summary>
    private void RetierLoot(Entity<MapGridComponent> grid, int tier)
    {
        if (tier <= 1)
            return;

        var enumerator = Transform(grid.Owner).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            if (TryComp<LootSpawnerComponent>(child, out var spawner))
                spawner.Rarity = tier;
        }
    }

    /// <summary>
    ///     Turns what is on a grid by quarter turns around its origin, leaving the grid as it is: the tiles move, and every entity
    ///     moves and faces the new way. Anchored things are let go and anchored again once the tiles are in place.
    /// </summary>
    private void RotateContent(Entity<MapGridComponent> grid, int turns)
    {
        if (turns % 4 == 0)
            return;

        var angle = Angle.FromDegrees(90 * turns);
        var old = _map.GetAllTiles(grid.Owner, grid.Comp).ToList();

        var children = new List<(EntityUid Uid, bool Anchored, Vector2 Pos, Angle Rot)>();
        var lost = new Dictionary<EntityUid, string>();
        var xform = Transform(grid.Owner);
        var enumerator = xform.ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            var childXform = Transform(child);
            children.Add((child, childXform.Anchored, childXform.LocalPosition, childXform.LocalRotation));
            lost[child] = MetaData(child).EntityPrototype?.ID ?? "?";
        }

        // Taking the tiles away must not take the things on them with it, so they wait on the map meanwhile
        var mapUid = xform.MapUid!.Value;
        foreach (var (child, anchored, _, _) in children)
        {
            var childXform = Transform(child);
            if (anchored)
                _transform.Unanchor(child, childXform);

            _transform.SetParent(child, childXform, mapUid);
        }

        // One change for all the tiles: a grid without any tile is deleted
        var moved = old.Select(t =>
        {
            var c = RotateVector(new Vector2(t.GridIndices.X + 0.5f, t.GridIndices.Y + 0.5f), turns);
            return (Index: new Vector2i((int) MathF.Floor(c.X), (int) MathF.Floor(c.Y)), t.Tile);
        }).ToList();

        var taken = moved.Select(m => m.Index).ToHashSet();
        var change = old.Where(t => !taken.Contains(t.GridIndices)).Select(t => (t.GridIndices, Tile.Empty)).ToList();
        change.AddRange(moved.Select(m => (m.Index, m.Tile)));
        _map.SetTiles(grid.Owner, grid.Comp, change);

        foreach (var (uid, anchored, pos, rot) in children)
        {
            if (!Exists(uid))
            {
                Log.Warning($"World: an entity was lost while turning a structure ({lost.GetValueOrDefault(uid)})");
                continue;
            }

            var childXform = Transform(uid);
            // Things whose sprite or use depends on the way they face keep their place in the room (seats, wall mounts). Everything else
            // keeps the way it was built, so its sprite stays upright on screen (a fireplace, a spawner, a table).
            var faces = HasComp<StrapComponent>(uid) || HasComp<WallMountComponent>(uid);
            _transform.SetCoordinates(uid, childXform, new EntityCoordinates(grid.Owner, RotateVector(pos, turns)), faces ? rot + angle : rot);
            if (anchored)
                _transform.AnchorEntity(uid, childXform);
        }
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
        UpdateStorm();
        RunJobs();

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
    /// <summary>
    ///     The front of a structure is the side with the most doors on its outer edge (the south side on a tie, which is the way
    ///     structures are built); its front door is the one nearest the middle of that side. The role comes from the structure's
    ///     prototype, or from its size.
    /// </summary>
    private StructureInfo Measure(string theme, ResPath path, Entity<MapGridComponent> grid, Vector2i min, Vector2i max)
    {
        var width = max.X - min.X + 1;
        var height = max.Y - min.Y + 1;
        var doors = new List<Vector2i>[4] { new(), new(), new(), new() };
        var allDoors = new List<Vector2i>();
        var enumerator = Transform(grid.Owner).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            if (!HasComp<DoorComponent>(child) || !Transform(child).Anchored)
                continue;

            var tile = _map.TileIndicesFor(grid.Owner, grid.Comp, Transform(child).Coordinates);
            allDoors.Add(tile);
            if (tile.Y == min.Y)
                doors[0].Add(tile);

            if (tile.X == max.X)
                doors[1].Add(tile);

            if (tile.Y == max.Y)
                doors[2].Add(tile);

            if (tile.X == min.X)
                doors[3].Add(tile);
        }

        var front = 0;
        for (var side = 1; side < 4; side++)
        {
            if (doors[side].Count > doors[front].Count)
                front = side;
        }

        // A building that is not a plain box has no door on the edge of its footprint: then the front is the side that the nearest
        // door is closest to
        if (doors[front].Count == 0 && allDoors.Count > 0)
        {
            var best = (Distance: int.MaxValue, Side: 0, Door: allDoors[0]);
            foreach (var d in allDoors)
            {
                var distances = new[] { d.Y - min.Y, max.X - d.X, max.Y - d.Y, d.X - min.X };
                for (var side = 0; side < 4; side++)
                {
                    if (distances[side] < best.Distance)
                        best = (distances[side], side, d);
                }
            }

            front = best.Side;
            doors[front].Add(best.Door);
        }

        // The door nearest the middle of the side; without any door, the middle of the south side
        var middle = front % 2 == 0 ? min.X + width / 2 : min.Y + height / 2;
        Vector2i door;
        if (doors[front].Count > 0)
            door = doors[front].OrderBy(d => Math.Abs((front % 2 == 0 ? d.X : d.Y) - middle)).First();
        else
            door = new Vector2i(min.X + width / 2, min.Y);

        var fileName = path.Filename;
        var id = $"{theme}-{(fileName.EndsWith(".yml") ? fileName[..^4] : fileName)}";
        var role = width * height <= 48 ? "outbuilding" : "house";
        var weight = 1f;
        if (_proto.TryIndex<WorldStructurePrototype>(id, out var structure))
        {
            role = structure.Role;
            weight = structure.Weight;
        }

        return new StructureInfo(path.ToString(), width, height, min, front, door, role, weight);
    }

    private List<StructureInfo> GetPool(string theme)
    {
        if (_pools.TryGetValue(theme, out var cached))
            return cached;

        var list = new List<StructureInfo>();
        var scratch = _map.CreateMap(out var scratchId, runMapInit: false);
        _meta.SetEntityName(scratch, "World structure measuring (scratch)");
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
            {
                var info = Measure(theme, path, grid.Value, min, max);
                list.Add(info);
            }

            QueueDel(grid.Value.Owner);
        }

        QueueDel(scratch);
        _pools[theme] = list;
        return list;
    }
}
