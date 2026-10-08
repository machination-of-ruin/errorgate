#nullable enable
using System.Linq;
using Content.Server.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Body.Organ;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class SmallTweaksTest
{
    [Test]
    public async Task GibbingDoesNotSpawnOrgans()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            var human = entMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            Assert.That(entMan.EntityQuery<OrganComponent>().Count(), Is.GreaterThan(0), "The human should have organs before gibbing.");

            entMan.System<BodySystem>().GibBody(human);
        });
        await pair.RunTicksSync(3);

        Assert.That(entMan.EntityQuery<OrganComponent>().Count(), Is.EqualTo(0), "Gibbing must not leave organs behind.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BloodPuddlesEvaporate()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var testMap = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var coords = testMap.GridCoords;
            var blood = new Solution("Blood", FixedPoint2.New(10));
            Assert.That(entMan.System<PuddleSystem>().TrySpillAt(coords, blood, out var puddle, sound: false));
            Assert.That(entMan.HasComponent<EvaporationComponent>(puddle), "A blood puddle should evaporate.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StaminaCritLeavesYouExhausted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            human = entMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            entMan.System<Content.Shared.Damage.Systems.StaminaSystem>().TakeStaminaDamage(human, 500f, immediate: true);
            Assert.That(entMan.GetComponent<Content.Shared.Damage.Components.StaminaComponent>(human).Critical, "The human should be in stamina crit.");
        });

        // Stamina crit lasts a few seconds. Stop as soon as it ends, the stamina starts recovering right after.
        for (var i = 0; i < 600; i++)
        {
            await pair.RunTicksSync(1);
            var critical = false;
            await server.WaitPost(() => critical = entMan.GetComponent<Content.Shared.Damage.Components.StaminaComponent>(human).Critical);
            if (!critical)
                break;
        }

        await server.WaitAssertion(() =>
        {
            var stamina = entMan.GetComponent<Content.Shared.Damage.Components.StaminaComponent>(human);
            Assert.That(stamina.Critical, Is.False, "The human should be out of stamina crit.");
            Assert.That(stamina.StaminaDamage, Is.GreaterThan(stamina.CritThreshold * 0.5f), "Coming out of crit must leave the human exhausted.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SprintingDrainsStaminaUntilTired()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            human = entMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            var stamina = entMan.GetComponent<Content.Shared.Damage.Components.StaminaComponent>(human);
            Assert.That(stamina.SprintingStaminaDrainRate, Is.GreaterThan(0f));
            // The mover toggles the drain of the entity itself every tick, so use another source to keep it going without input
            var source = entMan.SpawnEntity(null, new MapCoordinates(0, 0, mapId));
            entMan.System<Content.Shared.Damage.Systems.StaminaSystem>().ToggleStaminaDrain(human, stamina.SprintingStaminaDrainRate, true, false, source);
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            var stamina = entMan.GetComponent<Content.Shared.Damage.Components.StaminaComponent>(human);
            Assert.That(stamina.StaminaDamage, Is.GreaterThan(5f), $"Sprinting should cost stamina. drains={stamina.ActiveDrains.Count} active={entMan.HasComponent<Content.Shared.Damage.Components.ActiveStaminaComponent>(human)} crit={stamina.Critical} dmg={stamina.StaminaDamage}");
        });

        // Keep sprinting for a long time: it stops at 80%, it never reaches a crit
        await pair.RunSeconds(25);

        await server.WaitAssertion(() =>
        {
            var stamina = entMan.GetComponent<Content.Shared.Damage.Components.StaminaComponent>(human);
            Assert.That(stamina.Critical, Is.False, "Sprinting alone must not cause a stamina crit.");
            Assert.That(stamina.StaminaDamage, Is.LessThan(stamina.CritThreshold), "Sprinting stops at the tired mark.");
        });

        await pair.CleanReturnAsync();
    }
}
