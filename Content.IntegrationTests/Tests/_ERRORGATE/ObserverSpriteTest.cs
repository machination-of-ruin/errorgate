#nullable enable
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class ObserverSpriteTest
{
    [TestCase("MobObserver")]
    [TestCase("AdminObserver")]
    public async Task ObserversHaveNoVisibleSprite(string proto)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        EntityUid observer = default;
        await server.WaitPost(() => observer = server.EntMan.SpawnEntity(proto, testMap.GridCoords));
        await pair.RunSeconds(1);

        var net = server.EntMan.GetNetEntity(observer);
        await pair.Client.WaitAssertion(() =>
        {
            var cEnt = pair.Client.EntMan;
            Assert.That(cEnt.TryGetEntity(net, out var clientObserver), Is.True);
            var sprite = cEnt.GetComponent<SpriteComponent>(clientObserver!.Value);
            // Layer 0 is the ghost; later layers are worn items (the admin observer wears a bag)
            Assert.That(sprite[0].Visible, Is.False, $"{proto} shows the ghost sprite");
        });

        await pair.CleanReturnAsync();
    }
}
