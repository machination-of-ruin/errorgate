#nullable enable
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class PillCanisterTest
{
    [Test]
    public async Task PillCanisterOnlyTakesPills()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid canister = default, pill = default, steak = default;
        await server.WaitPost(() =>
        {
            canister = entMan.SpawnEntity("PillCanister", testMap.GridCoords);
            pill = entMan.SpawnEntity("Pill", testMap.GridCoords);
            steak = entMan.SpawnEntity("FoodMeatCooked", testMap.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var storage = entMan.System<SharedStorageSystem>();
            Assert.That(storage.CanInsert(canister, pill, out var pillReason), $"A pill should fit: {pillReason}");
            Assert.That(storage.CanInsert(canister, steak, out _), Is.False, "A steak must not fit into a pill canister.");
        });

        await pair.CleanReturnAsync();
    }
}
