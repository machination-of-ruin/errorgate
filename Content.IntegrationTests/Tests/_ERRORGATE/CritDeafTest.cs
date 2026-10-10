#nullable enable
using Content.Server._White.Hearing;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     A character in crit cannot hear.
/// </summary>
[TestFixture]
public sealed class CritDeafTest
{
    [Test]
    public async Task ACharacterInCritIsDeafAndHearsAgainWhenHealed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid human = default;
        await server.WaitPost(() => human = entMan.SpawnEntity("MobHuman", testMap.GridCoords));
        await pair.RunSeconds(1);

        await server.WaitAssertion(() => Assert.That(entMan.HasComponent<DeafComponent>(human), Is.False, "Alive and hearing."));

        await server.WaitPost(() =>
        {
            var spec = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(110));
            entMan.System<DamageableSystem>().TryChangeDamage(human, spec);
        });
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<MobStateComponent>(human).CurrentState, Is.EqualTo(MobState.Critical));
            Assert.That(entMan.HasComponent<DeafComponent>(human), Is.True, "In crit: deaf.");
        });

        await server.WaitPost(() => entMan.System<DamageableSystem>().SetAllDamage(human, entMan.GetComponent<DamageableComponent>(human), 0));
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<MobStateComponent>(human).CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(entMan.HasComponent<DeafComponent>(human), Is.False, "Healed: hearing again.");
        });

        await pair.CleanReturnAsync();
    }
}
