#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Playtest: ammo boxes in the world were always full. The loot tables use the random-load variants.
/// </summary>
[TestFixture]
public sealed class RandomAmmoBoxTest
{
    [Test]
    public async Task LootAmmoBoxesAreFilledAtRandom()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var boxes = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && (p.ID.StartsWith("AmmoBox") || p.ID.StartsWith("BoxShotgun")) && p.ID.EndsWith("Random"))
                .ToList();

            Assert.That(boxes.Count, Is.GreaterThanOrEqualTo(13));

            foreach (var box in boxes)
            {
                var counts = new HashSet<int>();
                for (var i = 0; i < 40; i++)
                {
                    var ent = entMan.SpawnEntity(box.ID, testMap.GridCoords);
                    var ev = new GetAmmoCountEvent();
                    entMan.EventBus.RaiseLocalEvent(ent, ref ev);
                    counts.Add(ev.Count);
                    entMan.DeleteEntity(ent);
                }

                Assert.That(counts.Count, Is.GreaterThan(3), $"{box.ID} is filled the same every time: {string.Join(",", counts)}");
            }
        });

        await pair.CleanReturnAsync();
    }
}
