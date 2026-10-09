#nullable enable
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class MobCollisionTest
{
    [Test]
    public async Task OverlappingMobsPushEachOtherApart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var xform = entMan.System<SharedTransformSystem>();

        EntityUid a = default;
        EntityUid b = default;
        await server.WaitPost(() =>
        {
            a = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            b = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
        });
        await pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            var distance = (xform.GetWorldPosition(a) - xform.GetWorldPosition(b)).Length();
            Assert.That(distance, Is.GreaterThan(0.15f), "Two mobs standing in each other should be pushed apart.");
        });

        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task PushingCanBeSwitchedOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var xform = entMan.System<SharedTransformSystem>();

        EntityUid a = default;
        EntityUid b = default;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.MovementMobPushing, false);
            a = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            b = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
        });
        await pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            var distance = (xform.GetWorldPosition(a) - xform.GetWorldPosition(b)).Length();
            Assert.That(distance, Is.LessThan(0.15f), "With mob pushing off the mobs stay where they are.");
        });

        await pair.CleanReturnAsync();
    }
}
