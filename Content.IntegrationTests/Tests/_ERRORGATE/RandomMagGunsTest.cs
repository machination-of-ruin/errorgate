#nullable enable
using System.Linq;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Playtest: ammo boxes (and anything else) fitted into the magazine slot of loot guns. A slot written in a child prototype
///     replaces the parent's whole slot, so the random magazine guns lost their whitelist.
/// </summary>
[TestFixture]
public sealed class RandomMagGunsTest
{
    [Test]
    public async Task RandomMagazineGunsKeepTheMagazineSlotOfTheirParent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var slots = entMan.System<ItemSlotsSystem>();
            var variants = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && p.ID.EndsWith("RandomMag") && p.Parents is { Length: > 0 } && p.Components.ContainsKey("ItemSlots"))
                .ToList();

            Assert.That(variants, Is.Not.Empty);

            foreach (var variant in variants)
            {
                var gun = entMan.SpawnEntity(variant.ID, testMap.GridCoords);
                var parent = entMan.SpawnEntity(variant.Parents![0], testMap.GridCoords);

                Assert.That(slots.TryGetSlot(gun, "gun_magazine", out var slot) && slot != null, Is.True, variant.ID);
                Assert.That(slots.TryGetSlot(parent, "gun_magazine", out var parentSlot) && parentSlot != null, Is.True, variant.ID);

                Assert.That(slot!.Whitelist, Is.Not.Null, $"{variant.ID} takes anything in its magazine slot");
                Assert.That(slot.Whitelist!.Tags, Is.EquivalentTo(parentSlot!.Whitelist!.Tags!), $"{variant.ID} accepts other magazines than {variant.Parents![0]}");
                Assert.That(slot.InsertSound?.ToString(), Is.EqualTo(parentSlot.InsertSound?.ToString()), variant.ID);
                Assert.That(slot.Priority, Is.EqualTo(parentSlot.Priority), variant.ID);
            }
        });

        await pair.CleanReturnAsync();
    }
}
