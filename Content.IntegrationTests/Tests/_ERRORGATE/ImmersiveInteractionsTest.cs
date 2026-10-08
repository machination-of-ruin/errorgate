#nullable enable
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.ActionBlocker;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class ImmersiveInteractionsTest
{
    [Test]
    public async Task WornItemsCannotBeUsedUnlessTheSettingIsOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.CfgMan;

        EntityUid human = default;
        EntityUid crowbar = default;
        EntityUid flashlight = default;

        await server.WaitAssertion(() =>
        {
            var map = entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = entMan.SpawnEntity("MobHuman", coords);
            crowbar = entMan.SpawnEntity("Crowbar", coords);
            flashlight = entMan.SpawnEntity("FlashlightLantern", coords);

            var inventory = entMan.System<InventorySystem>();
            Assert.That(inventory.TryEquip(human, crowbar, "belt", force: true));
            Assert.That(inventory.TryEquip(human, flashlight, "id", force: true));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var blocker = entMan.System<ActionBlockerSystem>();

            Assert.That(cfg.GetCVar(ErrorgateCVars.ImmersiveInteractions), Is.True, "The setting must default to on.");
            Assert.That(blocker.CanInteract(human, crowbar), Is.False, "A worn crowbar must not be usable.");

            // Items that opt in stay usable while worn.
            Assert.That(blocker.CanInteract(human, flashlight), Is.True, "A worn flashlight stays usable.");

            // Held items are always fine.
            var held = entMan.SpawnEntity("Crowbar", entMan.GetComponent<TransformComponent>(human).MapPosition);
            Assert.That(entMan.System<SharedHandsSystem>().TryPickup(human, held), "Could not pick the crowbar up.");
            Assert.That(blocker.CanInteract(human, held), Is.True, "A held crowbar must be usable.");

            cfg.SetCVar(ErrorgateCVars.ImmersiveInteractions, false);
            Assert.That(blocker.CanInteract(human, crowbar), Is.True, "With the setting off a worn crowbar is usable.");
            cfg.SetCVar(ErrorgateCVars.ImmersiveInteractions, true);
        });

        await pair.CleanReturnAsync();
    }
}
