#nullable enable
using System.Linq;
using Content.Server._ERRORGATE.Despawn;
using Content.Server._ERRORGATE.LootManager;
using Content.Server._ERRORGATE.MobLoot;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Loot is finite: the global table loads, spawners draw from it, items on the ground go away and free their place.
/// </summary>
[TestFixture]
public sealed class LootManagerTest
{
    [Test]
    public async Task SpawnersDrawLootAndDespawnRefunds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var testMap = await pair.CreateTestMap();
        var lootManager = entMan.System<LootManagerSystem>();

        // The global table loads without errors (an error log fails the test)
        await server.WaitPost(() =>
        {
            var manager = entMan.SpawnEntity(null, testMap.MapCoords);
            entMan.AddComponent(manager, new LootManagerComponent { GlobalLootTablePrototype = "LootTableGlobal" });
        });
        await pair.RunTicksSync(2);
        Assert.That(lootManager.LootManager.Count, Is.GreaterThan(50), "The global loot table should load.");

        // A spawner draws something and counts it
        EntityUid spawner = default;
        await server.WaitPost(() =>
        {
            spawner = entMan.SpawnEntity("LootSpawnerMilitaryTier1", testMap.GridCoords);
            var comp = entMan.GetComponent<LootSpawnerComponent>(spawner);
            comp.SpawnRate = 100000f; // always
            comp.NextAttempt = TimeSpan.Zero;
        });
        await pair.RunTicksSync(5);
        var used = lootManager.LootManager.Values.Sum(e => e.Count);
        Assert.That(used, Is.GreaterThan(0), "The spawner should have drawn from the loot table.");

        // An item lying on the grid despawns and frees its place in the cap
        var entry = lootManager.LootManager.First(kv => kv.Value.Count > 0);
        await server.WaitPost(() =>
        {
            foreach (var despawn in entMan.EntityQuery<DespawnItemComponent>())
            {
                if (entMan.GetComponent<MetaDataComponent>(despawn.Owner).EntityPrototype?.ID == entry.Key)
                    despawn.DespawnAt = TimeSpan.Zero;
            }
        });
        await pair.RunSeconds(6);
        Assert.That(entry.Value.Count, Is.LessThan(used), "A despawned item should free its place in the loot cap.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeadMobsDropLoot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var testMap = await pair.CreateTestMap();
        var lootManager = entMan.System<LootManagerSystem>();

        await server.WaitPost(() =>
        {
            var manager = entMan.SpawnEntity(null, testMap.MapCoords);
            entMan.AddComponent(manager, new LootManagerComponent { GlobalLootTablePrototype = "LootTableGlobal" });
        });
        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            var mob = entMan.SpawnEntity("MobWalker", testMap.GridCoords);
            entMan.GetComponent<MobLootComponent>(mob).DropChance = 1f;
            entMan.System<MobStateSystem>().ChangeMobState(mob, MobState.Dead);
        });
        await pair.RunTicksSync(5);

        Assert.That(lootManager.LootManager.Values.Sum(e => e.Count), Is.GreaterThan(0), "A dead walker should drop loot.");
        await pair.CleanReturnAsync();
    }
}

[TestFixture]
public sealed class EdgeOfEntropyLootTest
{
    [Test]
    public async Task TheMapFillsWithLoot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var lootManager = entMan.System<LootManagerSystem>();

        await server.WaitPost(() =>
        {
            var ticker = entMan.System<Content.Server.GameTicking.GameTicker>();
            var opts = Robust.Shared.EntitySerialization.DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(server.ProtoMan.Index<Content.Server.Maps.GameMapPrototype>("EdgeOfEntropy"), out _, opts);
        });
        await pair.RunTicksSync(10);

        Assert.That(lootManager.LootManager.Count, Is.GreaterThan(50), "The map should load the global loot table.");
        Assert.That(lootManager.LootManager.Values.Sum(e => e.Count), Is.GreaterThan(20), "The map's spawners should have placed loot.");
        Assert.That(entMan.EntityQuery<LootSpawnerComponent>().Count(), Is.GreaterThan(50), "The map should carry loot spawners.");
        await pair.CleanReturnAsync();
    }
}

[TestFixture]
public sealed class MobLootRateTest
{
    [Test]
    public async Task WalkersDropAtTheirDropChance()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var lootManager = entMan.System<LootManagerSystem>();

        await server.WaitPost(() =>
        {
            var manager = entMan.SpawnEntity(null, testMap.MapCoords);
            entMan.AddComponent(manager, new LootManagerComponent { GlobalLootTablePrototype = "LootTableGlobal" });
        });
        await pair.RunTicksSync(2);

        const int mobs = 200;
        await server.WaitPost(() =>
        {
            for (var i = 0; i < mobs; i++)
            {
                var mob = entMan.SpawnEntity("MobWalker", testMap.GridCoords);
                entMan.System<MobStateSystem>().ChangeMobState(mob, MobState.Dead);
            }
        });
        await pair.RunTicksSync(5);

        var drops = lootManager.LootManager.Values.Sum(e => e.Count);
        Assert.That(drops, Is.InRange(100, 180), $"MobWalker has a 0.7 drop chance, 200 deaths gave {drops} drops.");
        await pair.CleanReturnAsync();
    }
}
