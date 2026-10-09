using System.Numerics;
using Content.Server.Spawners.Components;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Places the world faults of an <see cref="AnomalyFieldComponent"/> over its grid(s).
/// </summary>
public sealed class AnomalyFieldSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<FixturesComponent> _fixturesQuery;

    public override void Initialize()
    {
        base.Initialize();

        _fixturesQuery = GetEntityQuery<FixturesComponent>();

        SubscribeLocalEvent<AnomalyFieldComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AnomalyFieldComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AnomalyFieldComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<AnomalyFieldComponent> ent, ref MapInitEvent args)
    {
        // Also raised when the component is added to an entity whose map is already running.
        // The faults are placed on the next update, once the whole map is loaded and anchored.
        ent.Comp.Pending = true;
    }

    private void OnStartup(Entity<AnomalyFieldComponent> ent, ref ComponentStartup args)
    {
        // A field added to a running map (admin, test) places its faults on the next update. Paused maps are skipped
        // by the update query, a map that is initialized later raises MapInit too.
        ent.Comp.Pending = true;
    }

    private void OnShutdown(Entity<AnomalyFieldComponent> ent, ref ComponentShutdown args)
    {
        ClearAnomalies(ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<AnomalyFieldComponent>();
        while (query.MoveNext(out var uid, out var field))
        {
            if (!field.Pending)
                continue;

            field.Pending = false;
            SpawnAnomalies(uid, field);
        }
    }

    /// <summary>
    ///     Removes the faults this field placed.
    /// </summary>
    public void ClearAnomalies(AnomalyFieldComponent field)
    {
        foreach (var anomaly in field.Spawned)
        {
            if (!TerminatingOrDeleted(anomaly))
                QueueDel(anomaly);
        }

        field.Spawned.Clear();
    }

    /// <summary>
    ///     Clears and places the faults again.
    /// </summary>
    /// <returns>How many faults were placed.</returns>
    public int SpawnAnomalies(EntityUid uid, AnomalyFieldComponent field)
    {
        ClearAnomalies(field);
        field.Pending = false;

        var mapId = Transform(uid).MapID;
        if (mapId == MapId.Nullspace)
        {
            Log.Warning($"Anomaly field {ToPrettyString(uid)} is in nullspace, no anomalies placed.");
            return 0;
        }

        // Prototypes that exist
        var pool = new List<EntProtoId>();
        foreach (var id in field.Prototypes)
        {
            if (_proto.HasIndex<EntityPrototype>(id.Id))
                pool.Add(id);
            else
                Log.Warning($"Anomaly field {ToPrettyString(uid)} lists an unknown prototype {id}.");
        }

        if (pool.Count == 0 || field.Count <= 0)
        {
            Log.Warning($"Anomaly field {ToPrettyString(uid)} has nothing to place.");
            return 0;
        }

        if (field.Unique)
            _random.Shuffle(pool);

        // Every walkable tile of every grid the field covers
        var candidates = new List<(EntityUid Grid, MapGridComponent Comp, Vector2i Tile)>();
        foreach (var (gridUid, grid) in GetGrids(uid, mapId))
        {
            foreach (var tile in _map.GetAllTiles(gridUid, grid))
            {
                candidates.Add((gridUid, grid, tile.GridIndices));
            }
        }

        if (candidates.Count == 0)
        {
            Log.Warning($"Anomaly field {ToPrettyString(uid)} found no floor to place anomalies on.");
            return 0;
        }

        _random.Shuffle(candidates);

        // People must not start inside a fault
        var spawnPositions = new List<Vector2>();
        var spawnQuery = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (spawnQuery.MoveNext(out _, out _, out var spawnXform))
        {
            if (spawnXform.MapID == mapId)
                spawnPositions.Add(_transform.GetWorldPosition(spawnXform));
        }

        var placed = new List<Vector2>();
        var walkableCache = new Dictionary<(EntityUid, Vector2i), bool>();
        var attempts = 0;

        foreach (var (gridUid, grid, tile) in candidates)
        {
            if (field.Spawned.Count >= field.Count)
                break;

            if (field.Unique && pool.Count == 0)
                break;

            if (attempts++ >= field.MaxAttempts)
                break;

            var coords = _map.GridTileToLocal(gridUid, grid, tile);
            var position = _transform.ToMapCoordinates(coords).Position;

            if (TooClose(position, placed, field.MinDistanceBetween)
                || TooClose(position, spawnPositions, field.MinDistanceFromSpawns))
            {
                continue;
            }

            if (!HasFreeArea(gridUid, grid, tile, field.FreeRadius, walkableCache))
                continue;

            EntProtoId proto;
            if (field.Unique)
            {
                proto = pool[^1];
                pool.RemoveAt(pool.Count - 1);
            }
            else
            {
                proto = _random.Pick(pool);
            }

            var anomaly = Spawn(proto, coords);
            field.Spawned.Add(anomaly);
            placed.Add(position);

            Log.Info($"Anomaly field {ToPrettyString(uid)} placed {proto} ({ToPrettyString(anomaly)}) at {position} on grid {gridUid} tile {tile}.");
        }

        if (field.Spawned.Count < field.Count)
        {
            Log.Warning($"Anomaly field {ToPrettyString(uid)} placed {field.Spawned.Count} of {field.Count} anomalies " +
                        $"({attempts} tiles tried of {candidates.Count}, prototypes left: {pool.Count}).");
        }

        return field.Spawned.Count;
    }

    private List<(EntityUid, MapGridComponent)> GetGrids(EntityUid uid, MapId mapId)
    {
        var grids = new List<(EntityUid, MapGridComponent)>();

        if (TryComp<MapGridComponent>(uid, out var ownGrid))
        {
            grids.Add((uid, ownGrid));
            return grids;
        }

        if (HasComp<MapComponent>(uid))
        {
            var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (query.MoveNext(out var gridUid, out var grid, out var xform))
            {
                if (xform.MapID == mapId)
                    grids.Add((gridUid, grid));
            }

            return grids;
        }

        // Some other entity: use the grid it stands on
        if (Transform(uid).GridUid is { } parentGrid && TryComp<MapGridComponent>(parentGrid, out var parentComp))
            grids.Add((parentGrid, parentComp));

        return grids;
    }

    private static bool TooClose(Vector2 position, List<Vector2> others, float distance)
    {
        if (distance <= 0f)
            return false;

        var distanceSquared = distance * distance;
        foreach (var other in others)
        {
            if ((other - position).LengthSquared() < distanceSquared)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Floor with nothing solid on it, in a circle of <paramref name="radius"/> tiles around <paramref name="center"/>.
    /// </summary>
    private bool HasFreeArea(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i center,
        float radius,
        Dictionary<(EntityUid, Vector2i), bool> cache)
    {
        var reach = (int) MathF.Ceiling(radius);
        var radiusSquared = radius * radius;

        for (var dx = -reach; dx <= reach; dx++)
        {
            for (var dy = -reach; dy <= reach; dy++)
            {
                if (dx * dx + dy * dy > radiusSquared)
                    continue;

                if (!IsWalkable(gridUid, grid, center + new Vector2i(dx, dy), cache))
                    return false;
            }
        }

        return true;
    }

    private bool IsWalkable(EntityUid gridUid, MapGridComponent grid, Vector2i tile, Dictionary<(EntityUid, Vector2i), bool> cache)
    {
        if (cache.TryGetValue((gridUid, tile), out var cached))
            return cached;

        var walkable = _map.TryGetTileRef(gridUid, grid, tile, out var tileRef) && !tileRef.Tile.IsEmpty;

        if (walkable)
        {
            foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile))
            {
                if (!_fixturesQuery.TryComp(anchored, out var fixtures))
                    continue;

                foreach (var fixture in fixtures.Fixtures.Values)
                {
                    if (fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.MobMask) != 0)
                    {
                        walkable = false;
                        break;
                    }
                }

                if (!walkable)
                    break;
            }
        }

        cache[(gridUid, tile)] = walkable;
        return walkable;
    }
}
