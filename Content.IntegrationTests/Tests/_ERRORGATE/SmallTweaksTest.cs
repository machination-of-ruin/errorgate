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
    [Test]
    public async Task UnpoweredDoorTakesAboutASecondToPryByHand()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var door = entMan.SpawnEntity("Airlock", testMap.GridCoords);
            var user = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.System<Content.Shared.Prying.Systems.PryingSystem>().TryPry(door, user, out var id);
            Assert.That(id, Is.Not.Null, "Prying by hand should start a do-after, not open the door instantly.");
            var doAfter = entMan.GetComponent<Content.Shared.DoAfter.DoAfterComponent>(user).DoAfters.Values.Single();
            Assert.That(doAfter.Args.Delay.TotalSeconds, Is.InRange(0.5, 2.0));
        });

        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task BreathingAmmoniaPoisonsTheLungs()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            human = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var body = entMan.System<BodySystem>();
            var lungs = body.GetBodyOrganEntityComps<Content.Server.Body.Components.LungComponent>((human, entMan.GetComponent<Content.Shared.Body.Components.BodyComponent>(human))).First();
            var solutions = entMan.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(lungs.Owner, "Lung", out var soln, out _), "The lungs should have a solution.");
            solutions.TryAddReagent(soln!.Value, new Content.Shared.Chemistry.Reagent.ReagentQuantity("Ammonia", 10), out _);
        });
        await pair.RunSeconds(6);

        await server.WaitAssertion(() =>
        {
            var damage = entMan.GetComponent<Content.Shared.Damage.DamageableComponent>(human).Damage.DamageDict;
            Assert.That(damage.TryGetValue("Poison", out var poison) ? poison.Float() : 0f, Is.GreaterThan(0f), "Ammonia in the lungs should poison.");
        });

        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task CampfireCooksHumanMeat()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            entMan.SpawnEntity("CampfireCraftable", testMap.GridCoords);
            entMan.SpawnEntity("FoodMeatHuman", testMap.GridCoords);
        });
        await pair.RunSeconds(90);

        await server.WaitAssertion(() =>
        {
            var cooked = entMan.EntityQuery<MetaDataComponent>()
                .Any(m => m.EntityPrototype?.ID == "FoodHumanMeatCooked");
            Assert.That(cooked, "Raw human meat on a campfire should cook into human steak.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TorsoSurvivesChestHits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            human = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var dmg = new Content.Shared.Damage.DamageSpecifier(
                server.ProtoMan.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Slash"), FixedPoint2.New(400));
            entMan.System<Content.Shared.Damage.DamageableSystem>().TryChangeDamage(human, dmg, true,
                targetPart: Content.Shared._Shitmed.Targeting.TargetBodyPart.Torso);
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(human), Is.False, "The body should still exist (it may be dead).");
            var body = entMan.System<BodySystem>();
            Assert.That(body.GetRootPartOrNull(human), Is.Not.Null, "The torso must never be deleted by damage.");
        });

        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task AmmoniaInTheAirPoisonsPlayers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            var mix = new Content.Shared.Atmos.GasMixture(2500f) { Temperature = 293.15f };
            // pure ammonia at the map pressure
            mix.AdjustMoles(Content.Shared.Atmos.Gas.Ammonia, 12f); mix.AdjustMoles(Content.Shared.Atmos.Gas.Oxygen, 20f);
            entMan.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, false, mix);
            human = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
        });
        await pair.RunSeconds(30);

        await server.WaitAssertion(() =>
        {
            var damage = entMan.GetComponent<Content.Shared.Damage.DamageableComponent>(human).Damage.DamageDict;
            Assert.That(damage.TryGetValue("Poison", out var poison) ? poison.Float() : 0f, Is.GreaterThan(0f),
                "Breathing ammonia from the air should poison. " + string.Join(", ", damage.Select(d => d.Key + "=" + d.Value)));
            Assert.That(entMan.EntityQuery<Content.Shared.Fluids.Components.PuddleComponent>().Any(), "Breathing ammonia from the air should make the victim vomit.");
        });

        await pair.CleanReturnAsync();
    }
}
