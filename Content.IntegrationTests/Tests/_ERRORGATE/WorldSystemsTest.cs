#nullable enable
using System.Linq;
using Content.Server._ERRORGATE.Despawn;
using Content.Server._ERRORGATE.SmartMobSpawner;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class WorldSystemsTest
{
    [Test]
    public async Task NewWorldPrototypesSpawn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            foreach (var proto in new[]
                     {
                         "FloorWaterEntityToxic", "SpikeTrap", "AirlockTough", "AirlockFactoryEntrance", "FactoryCard",
                         "MobSpawnerWalkerLiving", "MobSpawnerWalkerArmored", "MobSpawnerRunner", "MobSpawnerTarantula",
                         "MobSpawnerCarp", "LootSpawnerSecurityTier2", "CampfireCraftable",
                     })
            {
                entMan.SpawnEntity(proto, testMap.GridCoords);
            }
        });
        await pair.RunTicksSync(5);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpawnerSpawnsItsMob()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() => entMan.SpawnEntity("MobSpawnerWalkerLiving", testMap.GridCoords));
        await pair.RunTicksSync(5);

        Assert.That(entMan.EntityQuery<SmartMobSpawnerSpawnedComponent>().Any(), "The spawner should have spawned a mob.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeadBodiesDespawn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobWalker", testMap.GridCoords);
            entMan.System<MobStateSystem>().ChangeMobState(mob, MobState.Dead);
            Assert.That(entMan.GetComponent<DespawnDeadBodyComponent>(mob).DespawnAt, Is.Not.Null, "Dying should start the despawn timer.");
            entMan.GetComponent<DespawnDeadBodyComponent>(mob).DespawnAt = TimeSpan.Zero;
        });
        await pair.RunSeconds(4);

        await server.WaitAssertion(() =>
            Assert.That(entMan.Deleted(mob) || !entMan.EntityExists(mob) || entMan.TryGetComponent<DespawnDeadBodyComponent>(mob, out var d) && d.DespawnAt == null,
                "The corpse should be gone."));
        await pair.CleanReturnAsync();
    }
}
