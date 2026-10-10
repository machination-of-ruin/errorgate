#nullable enable
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Jittering;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Tiring out shakes the character, but gently.
/// </summary>
[TestFixture]
public sealed class StaminaShakeTest
{
    [Test]
    public async Task LowStaminaShakesTheCharacterGently()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid human = default;
        await server.WaitPost(() => human = entMan.SpawnEntity("MobHuman", testMap.GridCoords));
        await pair.RunSeconds(1);

        await server.WaitPost(() =>
        {
            var crit = entMan.GetComponent<StaminaComponent>(human).CritThreshold;
            entMan.System<StaminaSystem>().TakeStaminaDamage(human, crit * 0.7f, visual: false);
        });
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            var jitter = entMan.GetComponent<JitteringComponent>(human);
            Assert.That(jitter.Scale, Is.InRange(0.01f, 0.5f), "A gentle shake.");
        });

        await pair.CleanReturnAsync();
    }
}
