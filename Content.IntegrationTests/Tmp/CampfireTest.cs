#nullable enable
using System.Threading.Tasks;
using Content.Shared.Damage;
using Content.Shared.Standing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tmp;

// Throwaway: resting by a campfire heals, standing does not (not committed).
[TestFixture]
public sealed class CampfireTest
{
    [Test]
    public async Task Warmth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid standing = default, lying = default, far = default;
        await server.WaitPost(() =>
        {
            var em = server.EntMan;
            em.SpawnEntity("CampfireWorld", map.GridCoords);
            standing = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(1.5f, 0)));
            lying = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(-1.5f, 0)));
            far = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(8f, 0)));
            var proto = server.ProtoMan.Index<Content.Shared.Damage.Prototypes.DamageGroupPrototype>("Brute");
            foreach (var e in new[] { standing, lying, far })
                em.System<DamageableSystem>().TryChangeDamage(e, new DamageSpecifier(proto, 30), true);
            em.System<StandingStateSystem>().Down(lying);
            em.System<StandingStateSystem>().Down(far);
        });
        await pair.RunSeconds(21);
        await server.WaitPost(() =>
        {
            var em = server.EntMan;
            TestContext.Out.WriteLine($"CAMP standing next to it {em.GetComponent<DamageableComponent>(standing).TotalDamage}, lying next to it {em.GetComponent<DamageableComponent>(lying).TotalDamage}, lying far away {em.GetComponent<DamageableComponent>(far).TotalDamage} (all started at 30)");
        });
        await pair.CleanReturnAsync();
    }
}
