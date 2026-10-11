using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Gravity;
using Content.Shared.Gravity;
using Content.Server.Maps;
using Content.Server._ERRORGATE.LootManager;
using Content.Server._ERRORGATE.SmartMobSpawner;
using Content.Shared._ERRORGATE.Bunker;
using Content.Shared.Atmos;
using Content.Shared.Doors.Components;
using Content.Shared.GameTicking;
using Content.Server.Spawners.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Atmos.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._ERRORGATE.Bunker;

/// <summary>
///     The hidden bunker. At round start a hatch is placed on the open ground far from everything, and a bunker is built on a
///     map of its own. The surface map is never edited: the hatch is spawned at run time.
/// </summary>
public sealed class BunkerSystem : EntitySystem
{
    private static readonly EntProtoId HatchProto = "BunkerHatch";
    private static readonly EntProtoId LadderProto = "BunkerLadder";
    private static readonly EntProtoId DoorProto = "BunkerVestibuleDoor";
    private static readonly EntProtoId ControllerProto = "BunkerVestibule";
    private static readonly EntProtoId LightProto = "BunkerWarningLight";
    // Indestructible: concrete for the rooms, grey brick for the technical tunnels
    private static readonly EntProtoId RoomWallProto = "BunkerWallConcrete";
    private static readonly EntProtoId TunnelWallProto = "N14WallBrickGraySlantedIndestructible";
    private static readonly EntProtoId LootProto = "LootSpawnerMilitaryTier3";

    // Open ground needed around the hatch: a square of this radius is free floor
    private const int OpenRadius = 3;

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameMapManager _gameMaps = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly GravitySystem _gravity = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>The hatch of this round, if there is one.</summary>
    public EntityUid? Hatch { get; private set; }

    /// <summary>The map of the bunker.</summary>
    public EntityUid? BunkerMap { get; private set; }

    // The floor of the new grid has no air until the atmosphere system has made its tiles, then it is filled once
    private EntityUid? _airGrid;
    private List<Vector2i> _airTiles = new();
    private TimeSpan _airGiveUp;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            Hatch = null;
            BunkerMap = null;
            _airGrid = null;
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_airGrid is not { } grid)
            return;

        if (TerminatingOrDeleted(grid) || _timing.CurTime > _airGiveUp)
        {
            if (_airTiles.Count > 0)
                Log.Warning($"The bunker air was not put into {_airTiles.Count} tiles in time");

            _airGrid = null;
            return;
        }

        // Fill the tiles that have their atmosphere by now, the rest next frame
        for (var i = _airTiles.Count - 1; i >= 0; i--)
        {
            if (_atmos.GetTileMixture((grid, null, null), null, _airTiles[i]) is not { Immutable: false } air)
                continue;

            air.Clear();
            air.Temperature = 293.15f;
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            _airTiles.RemoveAt(i);
        }

        if (_airTiles.Count == 0)
            _airGrid = null;
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        if (!_cfg.GetCVar(BunkerCVars.Enabled))
            return;

        var selected = _gameMaps.GetSelectedMap()?.ID;
        var maps = _cfg.GetCVar(BunkerCVars.Maps).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (selected == null || !maps.Contains(selected))
            return;

        if (!TryBuild(out var error))
            Log.Error($"The bunker was not built: {error}");
    }

    /// <summary>
    ///     Builds the bunker and its hatch for this round, replacing the old ones. Also used by the admin command.
    /// </summary>
    public bool TryBuild(out string error, MapId? surfaceOverride = null)
    {
        if (Hatch != null && !TerminatingOrDeleted(Hatch.Value))
            QueueDel(Hatch);

        if (BunkerMap != null && !TerminatingOrDeleted(BunkerMap.Value))
            QueueDel(BunkerMap);

        Hatch = null;
        BunkerMap = null;

        var surfaceMap = surfaceOverride ?? _ticker.DefaultMap;
        if (!TryGetPrimaryGrid(surfaceMap, out var surfaceGrid, out var surface))
        {
            error = "no grid on the main map";
            return false;
        }

        if (!TryFindHatchSpot(surfaceMap, surfaceGrid, surface, out var spot))
        {
            error = "no place for the hatch";
            return false;
        }

        // The bunker, on a map of its own
        var mapUid = _map.CreateMap(out var mapId);
        var gas = new GasMixture(Atmospherics.CellVolume) { Temperature = 293.15f };
        gas.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        gas.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        _atmos.SetMapAtmosphere(mapUid, false, gas);

        // Dim light so the layout can be read, and gravity (a new map has neither)
        _map.SetAmbientLight(mapId, Color.FromHex("#2A2A2A"));

        var bunkerGrid = _mapManager.CreateGridEntity(mapId);
        _airGrid = bunkerGrid.Owner;
        _airGiveUp = _timing.CurTime + TimeSpan.FromSeconds(60);
        EnsureComp<GravityComponent>(bunkerGrid.Owner);
        EnsureComp<GridAtmosphereComponent>(bunkerGrid.Owner); // the map air only reaches a grid that has an atmosphere
        _gravity.EnableGravity(bunkerGrid.Owner);
        BuildTestBunker(mapId, bunkerGrid.Owner, bunkerGrid.Comp, out var vestibule, out var arrival);

        // The hatch on the surface: at run time, the map file is left alone
        var hatchCoords = _map.GridTileToLocal(surfaceGrid, surface, spot);
        var hatch = Spawn(HatchProto, hatchCoords);
        var hatchComp = Comp<BunkerHatchComponent>(hatch);
        hatchComp.Arrival = arrival;
        hatchComp.Vestibule = vestibule;
        hatchComp.OpenSeconds = _cfg.GetCVar(BunkerCVars.HatchOpenSeconds);

        // The way up comes out on the tile south of the hatch
        var vestibuleComp = Comp<BunkerVestibuleComponent>(vestibule);
        if (vestibuleComp.Ladder is { } ladder)
            Comp<BunkerLadderComponent>(ladder).Surface = _map.GridTileToLocal(surfaceGrid, surface, spot + new Vector2i(0, -1));

        Hatch = hatch;
        BunkerMap = mapUid;
        Log.Info($"The bunker hatch is at {spot} on {ToPrettyString(surfaceGrid)}, the bunker is on map {mapId}");

        error = string.Empty;
        return true;
    }

    private bool TryGetPrimaryGrid(MapId mapId, out EntityUid gridUid, out MapGridComponent grid)
    {
        gridUid = default;
        grid = default!;
        var best = -1;
        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var candidate, out var xform))
        {
            if (xform.MapID != mapId || candidate.ChunkCount <= best)
                continue;

            best = candidate.ChunkCount;
            gridUid = uid;
            grid = candidate;
        }

        return best >= 0;
    }

    // -- Hatch placement ------------------------------------------------------------------------------------------

    private bool TryFindHatchSpot(MapId mapId, EntityUid gridUid, MapGridComponent grid, out Vector2i spot)
    {
        spot = default;

        var floor = new HashSet<Vector2i>();
        var enumerator = _map.GetAllTilesEnumerator(gridUid, grid);
        while (enumerator.MoveNext(out var tile))
        {
            if (!tile.Value.Tile.IsEmpty)
                floor.Add(tile.Value.GridIndices);
        }

        // Solid things. Doors are solid for the hatch, but a walker can go through them.
        var solid = new HashSet<Vector2i>();
        var passable = new HashSet<Vector2i>();
        var doorQuery = GetEntityQuery<DoorComponent>();
        var fixtures = EntityQueryEnumerator<FixturesComponent, TransformComponent>();
        while (fixtures.MoveNext(out var ent, out var fix, out var xform))
        {
            if (!xform.Anchored || xform.GridUid != gridUid)
                continue;

            var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            var hard = false;
            foreach (var fixture in fix.Fixtures.Values)
            {
                if (fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.MobMask) != 0)
                {
                    hard = true;
                    break;
                }
            }

            if (!hard)
                continue;

            solid.Add(tile);
            if (!doorQuery.HasComp(ent))
                passable.Add(tile);
        }

        // Places that must be far away: loot, spawn points and mob spawners
        var toLocal = _transform.GetInvWorldMatrix(gridUid);
        var points = new List<Vector2>();
        var startTiles = new List<Vector2i>();

        var spawns = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (spawns.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            var local = Vector2.Transform(_transform.GetWorldPosition(xform), toLocal);
            points.Add(local);
            startTiles.Add(new Vector2i((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y)));
        }

        var loot = EntityQueryEnumerator<LootSpawnerComponent, TransformComponent>();
        while (loot.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID == mapId)
                points.Add(Vector2.Transform(_transform.GetWorldPosition(xform), toLocal));
        }

        var mobs = EntityQueryEnumerator<SmartMobSpawnerComponent, TransformComponent>();
        while (mobs.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID == mapId)
                points.Add(Vector2.Transform(_transform.GetWorldPosition(xform), toLocal));
        }

        // What a person can walk to from where the round starts
        var reachable = FindReachable(startTiles, floor, solid, passable);
        if (reachable.Count == 0)
            reachable = floor;

        var candidates = reachable.Where(t => IsOpenGround(t, floor, solid)).ToList();
        _random.Shuffle(candidates);

        var distance = _cfg.GetCVar(BunkerCVars.MinPoiDistance);
        foreach (var step in new[] { distance, distance * 2f / 3f, distance / 2f, distance / 4f, 0f })
        {
            var minSquared = step * step;
            foreach (var tile in candidates)
            {
                var center = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
                if (points.Any(p => Vector2.DistanceSquared(p, center) < minSquared))
                    continue;

                spot = tile;
                Log.Info($"Bunker hatch: {candidates.Count} open tiles, nearest point of interest at least {step:0} tiles away");
                return true;
            }
        }

        return false;
    }

    private static bool IsOpenGround(Vector2i tile, HashSet<Vector2i> floor, HashSet<Vector2i> solid)
    {
        for (var dx = -OpenRadius; dx <= OpenRadius; dx++)
        {
            for (var dy = -OpenRadius; dy <= OpenRadius; dy++)
            {
                var t = new Vector2i(tile.X + dx, tile.Y + dy);
                if (!floor.Contains(t) || solid.Contains(t))
                    return false;
            }
        }

        return true;
    }

    private static HashSet<Vector2i> FindReachable(List<Vector2i> starts, HashSet<Vector2i> floor, HashSet<Vector2i> solid, HashSet<Vector2i> passable)
    {
        var reached = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();

        foreach (var start in starts)
        {
            if (floor.Contains(start) && !passable.Contains(start) && reached.Add(start))
                queue.Enqueue(start);
        }

        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            foreach (var next in new[] { tile + new Vector2i(1, 0), tile + new Vector2i(-1, 0), tile + new Vector2i(0, 1), tile + new Vector2i(0, -1) })
            {
                // Walls and furniture stop a walker, doors do not
                if (!floor.Contains(next) || passable.Contains(next) || reached.Contains(next))
                    continue;

                reached.Add(next);
                queue.Enqueue(next);
            }
        }

        return reached;
    }

    // -- The bunker -----------------------------------------------------------------------------------------------

    /// <summary>
    ///     A small fixed bunker to prove the way in: the vestibule, a corridor and three rooms. The generated layout comes later.
    /// </summary>
    private void BuildTestBunker(MapId mapId, EntityUid gridUid, MapGridComponent grid, out EntityUid vestibule, out EntityCoordinates arrival)
    {
        var floors = new Dictionary<Vector2i, string>();
        var roomTiles = new HashSet<Vector2i>();

        void Fill(int x0, int y0, int x1, int y1, string tile, bool room = false)
        {
            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    var pos = new Vector2i(x, y);
                    floors[pos] = tile;
                    if (room)
                        roomTiles.Add(pos);
                }
            }
        }

        // The vestibule is 5 by 3 (0..4, 0..2) and its door into the bunker is on the east wall at (5, 1)
        Fill(0, 0, 4, 2, "FloorDark", true);
        Fill(5, 1, 30, 1, "FloorSteel");
        Fill(8, 3, 14, 9, "FloorSteel", true);
        Fill(11, 2, 11, 2, "FloorSteel");
        Fill(17, -6, 23, -1, "FloorSteel", true);
        Fill(20, 0, 20, 0, "FloorSteel");
        Fill(26, 3, 32, 8, "FloorSteel", true);
        Fill(29, 2, 29, 2, "FloorSteel");

        foreach (var (pos, tile) in floors)
        {
            _map.SetTile(gridUid, grid, pos, new Tile(_tileDefs[tile].TileId));
        }

        // Walls around every floor tile: concrete next to a room, grey brick along the tunnels
        var walls = new Dictionary<Vector2i, bool>();
        foreach (var pos in floors.Keys)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var next = new Vector2i(pos.X + dx, pos.Y + dy);
                    if (floors.ContainsKey(next))
                        continue;

                    walls[next] = walls.GetValueOrDefault(next) || roomTiles.Contains(pos);
                }
            }
        }

        foreach (var (pos, nextToRoom) in walls)
        {
            _map.SetTile(gridUid, grid, pos, new Tile(_tileDefs["FloorDark"].TileId));
            Spawn(nextToRoom ? RoomWallProto : TunnelWallProto, _map.GridTileToLocal(gridUid, grid, pos));
        }

        EntityCoordinates At(int x, int y) => _map.GridTileToLocal(gridUid, grid, new Vector2i(x, y));

        // The vestibule: controller, door, way up and the warning lights
        vestibule = Spawn(ControllerProto, At(2, 1));
        var door = Spawn(DoorProto, At(5, 1));
        // The hatch up is at the other end of the airlock from the door into the bunker
        var ladder = Spawn(LadderProto, At(0, 1));
        Comp<BunkerLadderComponent>(ladder).Vestibule = vestibule;
        _appearance.SetData(ladder, BunkerExitVisuals.Locked, true);
        var vestibuleComp = Comp<BunkerVestibuleComponent>(vestibule);
        vestibuleComp.Door = door;
        vestibuleComp.Ladder = ladder;
        vestibuleComp.Room = new Box2(0f, 0f, 5f, 3f);
        vestibuleComp.Lights.Add(Spawn(LightProto, At(0, 2)));
        vestibuleComp.Lights.Add(Spawn(LightProto, At(4, 0)));
        Comp<BunkerVestibuleDoorComponent>(door).Vestibule = vestibule;

        // Something worth coming for
        Spawn(LootProto, At(11, 6));
        Spawn(LootProto, At(20, -4));
        Spawn(LootProto, At(29, 5));

        _airTiles = floors.Keys.ToList();
        arrival = At(1, 1);
    }
}
