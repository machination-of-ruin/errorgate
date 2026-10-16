#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tmp;

// Throwaway: why does a melee execution not kill? (not committed)
[TestFixture]
public sealed class DeathProbe
{
    [Test]
    public async Task Dies()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        EntityUid attacker = default, victim = default, weapon = default;
        await server.WaitAssertion(() =>
        {
            em.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            attacker = em.SpawnEntity("MobHuman", coords);
            victim = em.SpawnEntity("MobHuman", new MapCoordinates(1.2f, 0, mapId));
            weapon = em.SpawnEntity("CombatKnife", coords);
            foreach (var m in new[] { attacker, victim })
            {
                em.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(m);
            }
            Assert.That(em.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickup(attacker, weapon));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var verbs = em.System<SharedVerbSystem>().GetLocalVerbs(victim, attacker, typeof(UtilityVerb));
            var execute = verbs.FirstOrDefault(v => v.Text == "Execute");
            TestContext.Out.WriteLine($"PROBE verbs: {string.Join(", ", verbs.Select(v => v.Text))}");
            execute!.Act!.Invoke();
            var d = em.GetComponentOrNull<DoAfterComponent>(attacker);
            TestContext.Out.WriteLine($"PROBE doafters after invoke: {d?.DoAfters.Count}");
        });

        for (var i = 0; i < 8; i++)
        {
            await pair.RunTicksSync(30);
            await server.WaitPost(() =>
            {
                var d = em.GetComponentOrNull<DoAfterComponent>(attacker);
                var dmg = em.GetComponent<DamageableComponent>(victim).TotalDamage;
                var state = em.GetComponent<MobStateComponent>(victim).CurrentState;
                TestContext.Out.WriteLine($"PROBE t={(i + 1) * 30} doafters={d?.DoAfters.Count} dmg={dmg} state={state} types={string.Join(",", em.GetComponent<DamageableComponent>(victim).Damage.DamageDict.Where(p => p.Value > 0).Select(p => p.Key + "=" + p.Value))}");
            });
        }

        await pair.CleanReturnAsync();
    }
}
