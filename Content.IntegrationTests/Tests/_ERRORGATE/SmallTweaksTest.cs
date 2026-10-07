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
}
