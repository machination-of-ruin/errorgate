#nullable enable
using Content.Server.Light.Components;
using Robust.Shared.GameObjects;
using Robust.Server.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class EmergencyLightTest
{
    [Test]
    public async Task UnpoweredEmergencyLightIsLitAtRoundStart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid lamp = default;
        await server.WaitPost(() => lamp = entMan.SpawnEntity("EmergencyLight", testMap.GridCoords));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var light = entMan.GetComponent<PointLightComponent>(lamp);
            Assert.That(light.Enabled, "The unpowered emergency light should be lit.");
            Assert.That(light.Radius, Is.EqualTo(10f));
            Assert.That(entMan.GetComponent<EmergencyLightComponent>(lamp).State, Is.EqualTo(EmergencyLightState.On));
        });

        await pair.CleanReturnAsync();
    }
}
