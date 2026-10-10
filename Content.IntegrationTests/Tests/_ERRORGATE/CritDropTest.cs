#nullable enable
using System.Linq;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     A character who goes into crit lets go of what they hold, a rifle held with both hands included.
/// </summary>
[TestFixture]
public sealed class CritDropTest
{
    [Test]
    public async Task CritDropsTheTwoHandedRifle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var hands = entMan.System<SharedHandsSystem>();

        EntityUid human = default;
        EntityUid rifle = default;
        await server.WaitAssertion(() =>
        {
            entMan.System<SharedMapSystem>().CreateMap(out var mapId);
            human = entMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            rifle = entMan.SpawnEntity("WeaponRifleAk", new MapCoordinates(0, 0, mapId));

            // Whatever the character was spawned holding goes, so the rifle can take both hands
            foreach (var held in hands.EnumerateHeld(human).ToList())
            {
                entMan.DeleteEntity(held);
            }

            Assert.That(hands.TryPickupAnyHand(human, rifle), Is.True);

            // Picking it up wields it: the second hand holds a virtual item
            Assert.That(entMan.GetComponent<WieldableComponent>(rifle).Wielded, Is.True, "The rifle should be held with both hands.");
            Assert.That(hands.EnumerateHeld(human).Count(), Is.EqualTo(2));
        });

        await server.WaitPost(() => entMan.System<MobStateSystem>().ChangeMobState(human, MobState.Critical));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(hands.EnumerateHeld(human).ToList(), Is.Empty, "A character in crit should not still hold anything.");
            Assert.That(entMan.GetComponent<TransformComponent>(rifle).ParentUid, Is.Not.EqualTo(human), "The rifle should be on the ground.");
            Assert.That(entMan.GetComponent<WieldableComponent>(rifle).Wielded, Is.False, "A dropped rifle is not wielded.");
        });

        await pair.CleanReturnAsync();
    }
}
