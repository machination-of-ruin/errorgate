using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.LootManager;

public sealed class LootSpawnerSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookup = default!;
    [Dependency] private readonly LootManagerSystem _lootManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LootSpawnerComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, LootSpawnerComponent component, MapInitEvent args)
    {
        TrySpawnLoot(uid, component);
        ScheduleNext(component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<LootSpawnerComponent>();
        while (query.MoveNext(out var uid, out var spawner))
        {
            if (spawner.NextAttempt > curTime)
                continue;

            ScheduleNext(spawner);
            TrySpawnLoot(uid, spawner);
        }
    }

    private void ScheduleNext(LootSpawnerComponent component)
    {
        // Randomize the interval a bit
        var duration = _random.NextFloat(component.IntervalSeconds * 0.9f, component.IntervalSeconds * 1.1f);
        component.NextAttempt = _timing.CurTime + TimeSpan.FromSeconds(duration);
    }

    private void TrySpawnLoot(EntityUid uid, LootSpawnerComponent component)
    {
        // Calculate spawn chance based on interval and spawn rate per hour (3600 seconds)
        var chance = Math.Min(1, component.SpawnRate * component.IntervalSeconds / 3600f);

        if (!_random.Prob(chance))
            return;

        var table = _lootManager.GetLootTable(component.Rarity, component.Location);

        if (!CheckSpawnPointCondition(uid, table))
            return;

        if (!_lootManager.TryPickLoot(table, out var entityProto) || entityProto == null)
        {
            Log.Debug($"Loot table for {ToPrettyString(uid)} is empty");
            return;
        }

        _lootManager.RegisterSpawn(entityProto);

        var coordinates = Transform(uid).Coordinates;
        Spawn(entityProto, coordinates);
        Spawn("LootSpawnSparkle", coordinates); // Cool effect
    }

    /// <summary>
    /// Nothing is spawned on a tile where one of the table's items already lies.
    /// </summary>
    private bool CheckSpawnPointCondition(EntityUid uid, Dictionary<string, int> table)
    {
        var xform = Transform(uid);

        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var spawnPos = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var coords = _map.ToCenterCoordinates(grid, spawnPos);

        foreach (var intersect in _entityLookup.GetEntitiesIntersecting(coords, LookupFlags.All))
        {
            var entityPrototype = MetaData(intersect).EntityPrototype;
            if (entityPrototype != null && table.ContainsKey(entityPrototype.ID))
                return false;
        }

        return true;
    }
}
