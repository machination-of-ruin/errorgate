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

                var parentSlots = entMan.GetComponent<ItemSlotsComponent>(parent).Slots;
                var gunSlots = entMan.GetComponent<ItemSlotsComponent>(gun).Slots;

                Assert.That(gunSlots.Keys, Is.EquivalentTo(parentSlots.Keys), $"{variant.ID} lost a slot of {variant.Parents![0]}");

                foreach (var (id, parentSlot) in parentSlots)
                {
                    var slot = gunSlots[id];
                    Assert.That(slot.Whitelist?.Tags, Is.EquivalentTo(parentSlot.Whitelist?.Tags ?? new()), $"{variant.ID} slot {id} accepts other things than its parent");
                    Assert.That(slot.InsertSound?.ToString(), Is.EqualTo(parentSlot.InsertSound?.ToString()), $"{variant.ID} {id}");
                    Assert.That(slot.Priority, Is.EqualTo(parentSlot.Priority), $"{variant.ID} {id}");
                    Assert.That(slot.Locked, Is.EqualTo(parentSlot.Locked), $"{variant.ID} {id}");

                    // The chamber is loaded exactly like the parent's
                    if (id == "gun_chamber")
                        Assert.That(slots.GetItemOrNull(gun, id) != null, Is.EqualTo(slots.GetItemOrNull(parent, id) != null), $"{variant.ID} chamber");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
