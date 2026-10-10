#nullable enable
using System.Linq;
using Content.Server._ERRORGATE.LootManager;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Loot appears with a sparkle (the old build did it).
/// </summary>
[TestFixture]
public sealed class LootSparkleTest
{
    [Test]
    public async Task LootSpawnsAlongsideASparkle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var manager = entMan.SpawnEntity(null, testMap.MapCoords);
            entMan.AddComponent(manager, new LootManagerComponent { GlobalLootTablePrototype = "LootTableGlobal" });
        });
        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            var spawner = entMan.SpawnEntity("LootSpawnerMilitaryTier1", testMap.GridCoords);
            var comp = entMan.GetComponent<LootSpawnerComponent>(spawner);
            comp.SpawnRate = 100000f;
            comp.NextAttempt = TimeSpan.Zero;
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var sparkles = entMan.AllEntities<Robust.Shared.GameObjects.MetaDataComponent>().Count(e => entMan.GetComponent<MetaDataComponent>(e.Owner).EntityPrototype?.ID == "LootSpawnSparkle");
            Assert.That(sparkles, Is.GreaterThan(0), "A sparkle spawns with the item.");
        });

        await pair.CleanReturnAsync();
    }
}
