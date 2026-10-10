using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._ERRORGATE.LootManager;
using Content.Server.Spawners.Components;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Places the world faults of an <see cref="AnomalyFieldComponent"/>.
/// </summary>
/// <remarks>
///     The faults come in packs of one type. Pack centers are picked along the likely routes between points of
///     interest (loot spawners) and spawn points, so a pack is something people run into, but never closer than a safe
///     distance to a spawn point or to loot. Reachability and routes are worked out on a coarse copy of the grid
///     (<see cref="AnomalyCoarseGrid"/>), because the grids are huge. Only the primary grid of the field is used:
///     the grid the component is on, or for a map the grid with the most chunks.
/// </remarks>
public sealed class AnomalyFieldSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    ///     Cells of the coarse grid that are at least this free count as walkable.
    /// </summary>
    private const float WalkableFraction = 0.5f;

    private const float PoiBucketSize = 16f;
    private const int MaxRouteNodes = 600;
    private const int MaxRoutes = 600;

    private EntityQuery<DoorComponent> _doorQuery;

    public override void Initialize()
    {
        base.Initialize();

        _doorQuery = GetEntityQuery<DoorComponent>();

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
        field.Packs.Clear();
    }

    /// <summary>
    ///     Clears and places the faults again.
    /// </summary>
    /// <returns>How many faults were placed.</returns>
    public int SpawnAnomalies(EntityUid uid, AnomalyFieldComponent field)
    {
        var watch = Stopwatch.StartNew();
        ClearAnomalies(field);
        field.Pending = false;

        var mapId = Transform(uid).MapID;
        if (mapId == MapId.Nullspace)
        {
            Log.Warning($"Anomaly field {ToPrettyString(uid)} is in nullspace, no anomalies placed.");
            return 0;
        }

        // Prototypes that exist, with the size of their danger zone (how far they can hurt)
        var types = new List<(EntProtoId Proto, float Radius)>();
        foreach (var id in field.Prototypes)
        {
            if (!_proto.TryIndex<EntityPrototype>(id.Id, out var proto))
            {
                Log.Warning($"Anomaly field {ToPrettyString(uid)} lists an unknown prototype {id}.");
                continue;
            }

            var radius = proto.TryGetComponent<ErrorgateAnomalyComponent>(out var anomaly, _componentFactory)
                ? anomaly.EffectiveDanger
                : 3f;
            types.Add((id, radius));
        }

        if (types.Count == 0 || field.Count <= 0)
        {
            Log.Warning($"Anomaly field {ToPrettyString(uid)} has nothing to place.");
            return 0;
        }

        if (!TryGetPrimaryGrid(uid, mapId, out var gridUid, out var grid))
        {
            Log.Warning($"Anomaly field {ToPrettyString(uid)} found no grid to place anomalies on.");
            return 0;
        }

        var state = BuildState(field, mapId, gridUid, grid);
        if (state == null)
        {
            Log.Warning($"Anomaly field {ToPrettyString(uid)} found no floor to place anomalies on.");
            return 0;
        }

        var centers = BuildCenterPool(field, state);
        PlacePacks(uid, field, state, types, centers);

        watch.Stop();
        Log.Info($"Anomaly field {ToPrettyString(uid)} placed {field.Spawned.Count} of {field.Count} anomalies in " +
                 $"{field.Packs.Count} packs in {watch.ElapsedMilliseconds} ms ({state.PoiPoints.Count} points of interest in " +
                 $"{state.Clusters.Count} clusters, {state.RouteCount} routes using {state.RouteCells.Count} cells, " +
                 $"{state.ReachableCells} of {state.Coarse.Count} cells reachable, {state.Attempts} attempts).");

        return field.Spawned.Count;
    }

    #region Layout

    /// <summary>
    ///     Everything about the grid the placement needs, in grid tile coordinates.
    /// </summary>
    private sealed class PlacementState
    {
        public EntityUid GridUid;
        public MapGridComponent Grid = default!;
        public AnomalyCoarseGrid Coarse = default!;
        public HashSet<Vector2i> Blocked = new();

        /// <summary>
        ///     Tiles a walker can get to from the start, null when that could not be worked out. Exact, unlike the
        ///     coarse cells: a one tile wall ring does not stop a cell flood fill.
        /// </summary>
        public HashSet<Vector2i>? ReachableTiles;
        public Dictionary<Vector2i, bool> FreeCache = new();

        public List<Vector2> Spawns = new();
        public List<Vector2> PoiPoints = new();
        public Dictionary<Vector2i, List<Vector2>> PoiBuckets = new();
        public List<(Vector2 Center, int Cell, int Weight)> Clusters = new();

        public HashSet<int> RouteCells = new();
        public int RouteCount;
        public int ReachableCells;
        public int Attempts;
    }

    private bool TryGetPrimaryGrid(EntityUid uid, MapId mapId, out EntityUid gridUid, [NotNullWhen(true)] out MapGridComponent? grid)
    {
        if (TryComp(uid, out grid))
        {
            gridUid = uid;
            return true;
        }

        gridUid = default;

        if (HasComp<MapComponent>(uid))
        {
            var best = -1;
            var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (query.MoveNext(out var candidate, out var candidateGrid, out var xform))
            {
                if (xform.MapID != mapId || candidateGrid.ChunkCount <= best)
                    continue;

                best = candidateGrid.ChunkCount;
                gridUid = candidate;
                grid = candidateGrid;
            }

            if (best >= 0 && grid != null)
                return true;
        }

        // Some other entity: use the grid it stands on
        if (Transform(uid).GridUid is { } parent && TryComp(parent, out grid))
        {
            gridUid = parent;
            return true;
        }

        grid = null;
        return false;
    }

    private PlacementState? BuildState(AnomalyFieldComponent field, MapId mapId, EntityUid gridUid, MapGridComponent grid)
    {
        // Floor
        var tiles = new List<Vector2i>();
        var enumerator = _map.GetAllTilesEnumerator(gridUid, grid);
        while (enumerator.MoveNext(out var tile))
        {
            tiles.Add(tile.Value.GridIndices);
        }

        if (tiles.Count == 0)
            return null;

        var min = tiles[0];
        var max = tiles[0];
        foreach (var tile in tiles)
        {
            min = new Vector2i(Math.Min(min.X, tile.X), Math.Min(min.Y, tile.Y));
            max = new Vector2i(Math.Max(max.X, tile.X), Math.Max(max.Y, tile.Y));
        }

        var state = new PlacementState
        {
            GridUid = gridUid,
            Grid = grid,
            Coarse = new AnomalyCoarseGrid(field.CellSize, min, max),
        };
        var coarse = state.Coarse;

        foreach (var tile in tiles)
        {
            if (coarse.TryCellOf(tile, out var cell))
                coarse.Floor[cell]++;
        }

        // Solid things standing on the floor
        var query = EntityQueryEnumerator<FixturesComponent, TransformComponent>();
        while (query.MoveNext(out var ent, out var fixtures, out var xform))
        {
            // Doors can be opened
            if (!xform.Anchored || xform.GridUid != gridUid || _doorQuery.HasComp(ent))
                continue;

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard || (fixture.CollisionLayer & (int) CollisionGroup.MobMask) == 0)
                    continue;

                state.Blocked.Add(_map.TileIndicesFor(gridUid, grid, xform.Coordinates));
                break;
            }
        }

        foreach (var blocked in state.Blocked)
        {
            if (coarse.TryCellOf(blocked, out var cell) && IsFloor(state, blocked))
                coarse.Blocked[cell]++;
        }

        coarse.Finish(WalkableFraction);

        // Spawn points and points of interest, in grid tile coordinates
        var toLocal = _transform.GetInvWorldMatrix(gridUid);

        var spawnQuery = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (spawnQuery.MoveNext(out _, out _, out var spawnXform))
        {
            if (spawnXform.MapID == mapId)
                state.Spawns.Add(Vector2.Transform(_transform.GetWorldPosition(spawnXform), toLocal));
        }

        var lootQuery = EntityQueryEnumerator<LootSpawnerComponent, TransformComponent>();
        while (lootQuery.MoveNext(out _, out _, out var lootXform))
        {
            if (lootXform.MapID == mapId)
                state.PoiPoints.Add(Vector2.Transform(_transform.GetWorldPosition(lootXform), toLocal));
        }

        // No loot: the spawn points are what is interesting. No spawn points either: the middle of the grid.
        if (state.PoiPoints.Count == 0)
        {
            state.PoiPoints.AddRange(state.Spawns);
            if (state.PoiPoints.Count == 0)
                state.PoiPoints.Add(new Vector2((min.X + max.X) / 2f, (min.Y + max.Y) / 2f));
        }

        foreach (var point in state.PoiPoints)
        {
            var bucket = BucketOf(point);
            if (!state.PoiBuckets.TryGetValue(bucket, out var list))
                state.PoiBuckets[bucket] = list = new List<Vector2>();

            list.Add(point);
        }

        BuildClusters(field, state);
        FindReachable(state);
        BuildRoutes(field, state);

        return state;
    }

    private static Vector2i BucketOf(Vector2 point)
    {
        return new Vector2i((int) MathF.Floor(point.X / PoiBucketSize), (int) MathF.Floor(point.Y / PoiBucketSize));
    }

    /// <summary>
    ///     Groups the points of interest into clusters (cells of <see cref="AnomalyFieldComponent.PoiClusterSize"/>
    ///     tiles) and puts each cluster on the nearest walkable cell.
    /// </summary>
    private void BuildClusters(AnomalyFieldComponent field, PlacementState state)
    {
        var size = Math.Max(1f, field.PoiClusterSize);
        var sums = new Dictionary<Vector2i, (Vector2 Sum, int Count)>();

        foreach (var point in state.PoiPoints)
        {
            var key = new Vector2i((int) MathF.Floor(point.X / size), (int) MathF.Floor(point.Y / size));
            sums.TryGetValue(key, out var sum);
            sums[key] = (sum.Sum + point, sum.Count + 1);
        }

        foreach (var (sum, count) in sums.Values)
        {
            var center = sum / count;
            var cell = -1;
            if (state.Coarse.TryCellOf(center, out var raw))
                state.Coarse.TryNearestWalkable(raw, 8, out cell);

            state.Clusters.Add((center, cell, count));
        }
    }

    /// <summary>
    ///     Flood fills the floor tile by tile (four directions, around solid things) from the spawn points, or from the
    ///     biggest cluster if there are none. Whatever lies behind a wall, like the ring of mountains around the playable
    ///     area, is never reached.
    /// </summary>
    private bool FindReachableTiles(PlacementState state)
    {
        var starts = new List<Vector2>(state.Spawns);
        if (starts.Count == 0 && state.Clusters.Count > 0)
            starts.Add(state.Clusters.OrderByDescending(c => c.Weight).First().Center);

        var reached = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();

        foreach (var start in starts)
        {
            var tile = new Vector2i((int) MathF.Floor(start.X), (int) MathF.Floor(start.Y));

            // The spawn may stand next to something solid: take the closest free tile
            for (var ring = 0; ring <= 6 && !reached.Contains(tile); ring++)
            {
                var found = false;
                for (var dx = -ring; dx <= ring && !found; dx++)
                {
                    for (var dy = -ring; dy <= ring && !found; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                            continue;

                        var candidate = tile + new Vector2i(dx, dy);
                        if (!IsFreeTile(state, candidate))
                            continue;

                        reached.Add(candidate);
                        queue.Enqueue(candidate);
                        found = true;
                    }
                }

                if (found)
                    break;
            }
        }

        var directions = new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) };
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var direction in directions)
            {
                var next = current + direction;
                if (reached.Contains(next) || !IsFreeTile(state, next))
                    continue;

                reached.Add(next);
                queue.Enqueue(next);
            }
        }

        if (reached.Count < 200)
        {
            Log.Warning($"Anomaly placement: only {reached.Count} floor tiles are reachable from the start, falling back to coarse reachability.");
            return false;
        }

        state.ReachableTiles = reached;
        return true;
    }

    /// <summary>
    ///     Flood fills the coarse grid from the spawn points, or from the biggest cluster if there are none.
    /// </summary>
    private void FindReachable(PlacementState state)
    {
        var coarse = state.Coarse;
        var walkable = coarse.Walkable.Count(w => w);

        if (FindReachableTiles(state))
        {
            // A cell is reachable when a reachable tile lies in it
            coarse.Reachable = new bool[coarse.Walkable.Length];
            var cells = 0;
            foreach (var tile in state.ReachableTiles!)
            {
                if (coarse.TryCellOf(tile, out var tileCell) && coarse.Walkable[tileCell] && !coarse.Reachable[tileCell])
                {
                    coarse.Reachable[tileCell] = true;
                    cells++;
                }
            }

            state.ReachableCells = cells;
            return;
        }

        var starts = new List<int>();
        foreach (var spawn in state.Spawns)
        {
            if (coarse.TryCellOf(spawn, out var raw) && coarse.TryNearestWalkable(raw, 8, out var cell))
                starts.Add(cell);
        }

        if (starts.Count == 0)
        {
            var biggest = state.Clusters.Where(c => c.Cell >= 0).OrderByDescending(c => c.Weight).Select(c => (int?) c.Cell).FirstOrDefault();
            if (biggest != null)
                starts.Add(biggest.Value);
        }

        var reached = coarse.FloodFill(starts);

        // Something is walled in (closed rooms look like walls here): do not leave the map empty because of it
        if (reached < Math.Min(50, walkable))
        {
            Log.Warning($"Anomaly placement: only {reached} of {walkable} walkable cells are reachable from the start, using every cluster as a start.");
            reached = coarse.FloodFill(starts.Concat(state.Clusters.Where(c => c.Cell >= 0).Select(c => c.Cell)));
        }

        if (reached < Math.Min(50, walkable))
        {
            Log.Warning("Anomaly placement: reachability is unusable, treating every walkable cell as reachable.");
            coarse.Reachable = (bool[]) coarse.Walkable.Clone();
            reached = walkable;
        }

        state.ReachableCells = reached;
    }

    /// <summary>
    ///     Traces routes between each cluster and its nearest neighbours, and from the spawn points to the clusters
    ///     around them.
    /// </summary>
    private void BuildRoutes(AnomalyFieldComponent field, PlacementState state)
    {
        if (!field.UseRoutes)
            return;

        var coarse = state.Coarse;

        // Nodes of the route graph: clusters and spawn points
        var nodes = new List<(Vector2 Position, int Cell, bool Spawn)>();
        foreach (var cluster in state.Clusters.OrderByDescending(c => c.Weight).Take(MaxRouteNodes))
        {
            if (cluster.Cell >= 0 && coarse.Reachable[cluster.Cell])
                nodes.Add((cluster.Center, cluster.Cell, false));
        }

        foreach (var spawn in state.Spawns)
        {
            if (coarse.TryCellOf(spawn, out var raw) && coarse.TryNearestWalkable(raw, 8, out var cell) && coarse.Reachable[cell])
                nodes.Add((spawn, cell, true));
        }

        var neighbours = Math.Max(1, field.RoutesPerCluster);
        var pairs = new HashSet<(int, int)>();

        for (var i = 0; i < nodes.Count; i++)
        {
            // A spawn point is joined to clusters only, a cluster to anything nearby
            var nearest = Enumerable.Range(0, nodes.Count)
                .Where(j => j != i && !(nodes[i].Spawn && nodes[j].Spawn))
                .OrderBy(j => (nodes[j].Position - nodes[i].Position).LengthSquared())
                .Take(neighbours);

            foreach (var j in nearest)
            {
                pairs.Add((Math.Min(i, j), Math.Max(i, j)));
            }
        }

        foreach (var (a, b) in pairs.OrderBy(_ => _random.Next()).Take(MaxRoutes))
        {
            var path = coarse.FindPath(nodes[a].Cell, nodes[b].Cell);
            if (path == null)
                continue;

            state.RouteCount++;
            foreach (var cell in path)
            {
                state.RouteCells.Add(cell);
            }
        }
    }

    /// <summary>
    ///     Possible pack centers, best first: the cells on the routes, then the other reachable cells near loot.
    /// </summary>
    private List<Vector2> BuildCenterPool(AnomalyFieldComponent field, PlacementState state)
    {
        var coarse = state.Coarse;
        var half = coarse.CellSize / 2f;

        Vector2 Jitter(int cell) => coarse.CellCenter(cell) + new Vector2(_random.NextFloat(-half, half), _random.NextFloat(-half, half));

        var onRoutes = state.RouteCells.Select(Jitter).ToList();
        _random.Shuffle(onRoutes);

        var elsewhere = new List<Vector2>();
        for (var cell = 0; cell < coarse.Count; cell++)
        {
            if (!coarse.Reachable[cell] || state.RouteCells.Contains(cell))
                continue;

            var center = Jitter(cell);
            if (NearPoi(state, center, field.MaxDistanceFromPoi))
                elsewhere.Add(center);
        }

        _random.Shuffle(elsewhere);

        onRoutes.AddRange(elsewhere);
        return onRoutes;
    }

    private static bool NearPoi(PlacementState state, Vector2 position, float distance)
    {
        var reach = (int) MathF.Ceiling(distance / PoiBucketSize);
        var center = BucketOf(position);
        var distanceSquared = distance * distance;

        for (var x = -reach; x <= reach; x++)
        {
            for (var y = -reach; y <= reach; y++)
            {
                if (!state.PoiBuckets.TryGetValue(center + new Vector2i(x, y), out var points))
                    continue;

                foreach (var point in points)
                {
                    if ((point - position).LengthSquared() <= distanceSquared)
                        return true;
                }
            }
        }

        return false;
    }

    private static bool NearAny(List<Vector2> points, Vector2 position, float distance)
    {
        var distanceSquared = distance * distance;
        foreach (var point in points)
        {
            if ((point - position).LengthSquared() < distanceSquared)
                return true;
        }

        return false;
    }

    #endregion

    #region Packs

    private void PlacePacks(
        EntityUid fieldUid,
        AnomalyFieldComponent field,
        PlacementState state,
        List<(EntProtoId Proto, float Radius)> types,
        List<Vector2> centers)
    {
        var placed = new List<(Vector2 Position, float Radius, AnomalyPack Pack)>();
        var packCenters = new List<Vector2>();
        var packs = new List<(AnomalyPack Pack, List<Vector2> Positions)>();
        var order = new List<int>();
        var remaining = field.Count;
        var average = Math.Max(1f, (field.PackSizeMin + field.PackSizeMax) / 2f);
        var maxPacks = (int) MathF.Ceiling(field.Count / average) * 3 + 10;

        int NextType()
        {
            if (order.Count == 0)
            {
                for (var i = 0; i < types.Count; i++)
                {
                    order.Add(i);
                }

                _random.Shuffle(order);
            }

            var type = order[^1];
            order.RemoveAt(order.Count - 1);
            return type;
        }

        for (var pass = 0; pass < 2 && remaining > 0; pass++)
        {
            var packDistance = pass == 0 ? field.MinDistanceBetweenPacks : field.MinDistanceBetweenPacks / 2f;
            if (pass == 1)
            {
                Log.Warning($"Anomaly field {ToPrettyString(fieldUid)}: {remaining} anomalies are still missing, " +
                            $"packs may now be {packDistance} tiles apart instead of {field.MinDistanceBetweenPacks}.");
            }

            foreach (var center in centers)
            {
                if (remaining <= 0 || state.Attempts >= field.MaxAttempts || packs.Count >= maxPacks)
                    break;

                state.Attempts++;

                if (!state.Coarse.TryCellOf(center, out var cell) || !state.Coarse.Reachable[cell]
                    || NearAny(state.Spawns, center, field.MinDistanceFromSpawns)
                    || NearPoi(state, center, field.MinDistanceFromPoi)
                    || !NearPoi(state, center, field.MaxDistanceFromPoi)
                    || NearAny(packCenters, center, packDistance))
                {
                    continue;
                }

                var type = types[NextType()];
                var size = Math.Clamp(_random.Next(field.PackSizeMin, Math.Max(field.PackSizeMin, field.PackSizeMax) + 1), 1, remaining);

                // Bigger danger zones need more room: the blob grows with the number of faults and their size,
                // so that there are gaps to walk through (about two tiles between the danger zones on average)
                var spacing = MathF.Max(field.MinSpacingInPack, type.Radius * 2f + 2f);
                var pack = new AnomalyPack
                {
                    Proto = type.Proto,
                    Radius = MathF.Max(field.PackRadius, 0.9f * spacing * MathF.Sqrt(size)),
                    Spacing = spacing,
                };

                var positions = ScatterPack(field, state, center, type.Radius, pack, size, placed);
                if (positions.Count == 0)
                    continue;

                pack.Passable = IsPassable(center, pack.Radius, positions, type.Radius);
                packCenters.Add(center);
                packs.Add((pack, positions));
                pack.Center = ToWorld(state, center);
                remaining -= positions.Count;
            }
        }

        // Make the entities
        foreach (var (pack, positions) in packs)
        {
            foreach (var position in positions)
            {
                var anomaly = Spawn(pack.Proto, new EntityCoordinates(state.GridUid, position));
                pack.Members.Add(anomaly);
                field.Spawned.Add(anomaly);
            }

            field.Packs.Add(pack);
            Log.Debug($"Anomaly pack of {pack.Members.Count} {pack.Proto} around {pack.Center}, blob radius {pack.Radius}, spacing {pack.Spacing}.");
        }

        Log.Info($"Anomaly field {ToPrettyString(fieldUid)}: {field.Packs.Count(p => p.Passable)} of {field.Packs.Count} packs can be walked through.");

        if (remaining > 0)
        {
            Log.Warning($"Anomaly field {ToPrettyString(fieldUid)} placed {field.Spawned.Count} of {field.Count} anomalies " +
                        $"({state.Attempts} attempts of {field.MaxAttempts}, {centers.Count} possible pack centers).");
        }
    }

    /// <summary>
    ///     Finds the positions (tile centers) of one pack inside its blob.
    /// </summary>
    private List<Vector2> ScatterPack(
        AnomalyFieldComponent field,
        PlacementState state,
        Vector2 center,
        float radius,
        AnomalyPack pack,
        int size,
        List<(Vector2 Position, float Radius, AnomalyPack Pack)> placed)
    {
        var positions = new List<Vector2>();
        var freeRadius = MathF.Max(field.FreeRadius, radius);
        var tries = size * 30;

        for (var i = 0; i < tries && positions.Count < size && state.Attempts < field.MaxAttempts; i++)
        {
            state.Attempts++;

            // The first fault goes at the center, the rest around it
            var offset = i == 0
                ? Vector2.Zero
                : _random.NextAngle().ToVec() * pack.Radius * MathF.Sqrt(_random.NextFloat());

            var tile = new Vector2i((int) MathF.Floor(center.X + offset.X), (int) MathF.Floor(center.Y + offset.Y));
            var position = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);

            if (!state.Coarse.TryCellOf(tile, out var cell) || !state.Coarse.Reachable[cell]
                || state.ReachableTiles != null && !state.ReachableTiles.Contains(tile)
                || NearAny(state.Spawns, position, field.MinDistanceFromSpawns)
                || NearPoi(state, position, field.MinDistanceFromPoi))
            {
                continue;
            }

            // Most of the time there is room to walk between two danger zones, now and then there is not
            var gap = _random.Prob(0.7f) ? _random.NextFloat(1.5f, 3f) : _random.NextFloat(0f, 1f);

            var crowded = false;
            foreach (var (other, otherRadius, otherPack) in placed)
            {
                var required = radius + otherRadius + gap;
                if (otherPack == pack)
                    required = MathF.Max(required, field.MinSpacingInPack);

                if ((other - position).LengthSquared() < required * required)
                {
                    crowded = true;
                    break;
                }
            }

            if (crowded || !HasFreeArea(state, tile, freeRadius))
                continue;

            positions.Add(position);
            placed.Add((position, radius, pack));
        }

        return positions;
    }

    /// <summary>
    ///     Whether a person (about a tile wide) can get through the blob of a pack from one side to the opposite one
    ///     without entering a danger zone. Worked out on a one tile raster of the blob.
    /// </summary>
    private static bool IsPassable(Vector2 center, float blobRadius, List<Vector2> positions, float radius)
    {
        var reach = (int) MathF.Ceiling(blobRadius);
        var size = reach * 2 + 1;
        var centerTile = new Vector2i((int) MathF.Floor(center.X), (int) MathF.Floor(center.Y));
        var clear = radius + 0.5f;
        var clearSquared = clear * clear;
        var edge = blobRadius * 0.85f;

        var free = new bool[size * size];
        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                var point = new Vector2(centerTile.X + (x - reach) + 0.5f, centerTile.Y + (y - reach) + 0.5f);
                if ((point - center).LengthSquared() > blobRadius * blobRadius)
                    continue;

                var open = true;
                foreach (var position in positions)
                {
                    if ((position - point).LengthSquared() < clearSquared)
                    {
                        open = false;
                        break;
                    }
                }

                free[y * size + x] = open;
            }
        }

        // Offset of a raster cell from the center of the blob along the axis that is crossed
        float Offset(int cell, bool alongX)
        {
            return alongX
                ? centerTile.X + (cell % size - reach) + 0.5f - center.X
                : centerTile.Y + (cell / size - reach) + 0.5f - center.Y;
        }

        bool Crosses(bool alongX)
        {
            var seen = new bool[free.Length];
            var queue = new Queue<int>();

            for (var i = 0; i < free.Length; i++)
            {
                if (free[i] && Offset(i, alongX) <= -edge)
                {
                    seen[i] = true;
                    queue.Enqueue(i);
                }
            }

            while (queue.TryDequeue(out var current))
            {
                if (Offset(current, alongX) >= edge)
                    return true;

                var cx = current % size;
                var cy = current / size;

                for (var i = 0; i < 4; i++)
                {
                    var nx = cx + (i == 0 ? 1 : i == 1 ? -1 : 0);
                    var ny = cy + (i == 2 ? 1 : i == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size)
                        continue;

                    var next = ny * size + nx;
                    if (!free[next] || seen[next])
                        continue;

                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }

            return false;
        }

        return Crosses(true) || Crosses(false);
    }

    private Vector2 ToWorld(PlacementState state, Vector2 gridPosition)
    {
        return _transform.ToMapCoordinates(new EntityCoordinates(state.GridUid, gridPosition)).Position;
    }

    #endregion

    #region Tiles

    private bool IsFloor(PlacementState state, Vector2i tile)
    {
        return _map.TryGetTileRef(state.GridUid, state.Grid, tile, out var tileRef) && !tileRef.Tile.IsEmpty;
    }

    /// <summary>
    ///     Floor with nothing solid on it, in a circle of <paramref name="radius"/> tiles around <paramref name="center"/>.
    /// </summary>
    private bool HasFreeArea(PlacementState state, Vector2i center, float radius)
    {
        var reach = (int) MathF.Ceiling(radius);
        var radiusSquared = radius * radius;

        for (var dx = -reach; dx <= reach; dx++)
        {
            for (var dy = -reach; dy <= reach; dy++)
            {
                if (dx * dx + dy * dy > radiusSquared)
                    continue;

                if (!IsFreeTile(state, center + new Vector2i(dx, dy)))
                    return false;
            }
        }

        return true;
    }

    private bool IsFreeTile(PlacementState state, Vector2i tile)
    {
        if (state.FreeCache.TryGetValue(tile, out var cached))
            return cached;

        var free = !state.Blocked.Contains(tile) && IsFloor(state, tile);
        state.FreeCache[tile] = free;
        return free;
    }

    #endregion
}
