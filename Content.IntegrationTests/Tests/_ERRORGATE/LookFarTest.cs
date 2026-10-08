#nullable enable
using Content.Server.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class LookFarTest
{
    [Test]
    public async Task HumansCanLookIntoTheDistance()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            var human = entMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            Assert.That(entMan.HasComponent<EyeCursorOffsetComponent>(human), "Humans should be able to look far.");
        });

        await pair.CleanReturnAsync();
    }
}
