#nullable enable
using System.Linq;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Content.Shared.Station;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     The inventory rework: no pocket slots, pockets live in the jumpsuit, the old ID slot carries light sources.
/// </summary>
[TestFixture]
public sealed class InventoryReworkTest
{
    [Test]
    public async Task PocketsLiveInTheJumpsuit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            var pos = new MapCoordinates(0, 0, mapId);
            var inventory = entMan.System<InventorySystem>();

            var human = entMan.SpawnEntity("MobHuman", pos);
            Assert.That(inventory.TryGetSlots(human, out var slots));
            Assert.That(slots!.Select(s => s.Name), Does.Not.Contain("pocket1"));
            Assert.That(slots!.Select(s => s.Name), Does.Not.Contain("pocket2"));

            // The ID slot takes a lighter.
            var lighter = entMan.SpawnEntity("CheapLighter", pos);
            Assert.That(inventory.TryEquip(human, lighter, "id", force: true), "A lighter should fit the light slot.");

            // Pocket gear of a starting gear ends up in the jumpsuit.
            var clown = entMan.SpawnEntity("MobHuman", pos);
            var gear = server.ProtoMan.Index<StartingGearPrototype>("ClownGear");
            entMan.System<SharedStationSpawningSystem>().EquipStartingGear(clown, gear);
            Assert.That(inventory.TryGetSlotEntity(clown, "jumpsuit", out var suit));
            Assert.That(entMan.TryGetComponent<StorageComponent>(suit, out var storage), "Jumpsuits have storage.");
            var names = storage!.Container.ContainedEntities.Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID).ToList();
            Assert.That(names, Does.Contain("BikeHorn"), "Pocket gear should be in the jumpsuit.");
        });

        await pair.CleanReturnAsync();
    }
}
