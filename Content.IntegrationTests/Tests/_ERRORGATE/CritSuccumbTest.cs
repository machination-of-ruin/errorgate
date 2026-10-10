#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Succumbing in crit kills for good, even while damage keeps arriving (burning, no air).
/// </summary>
[TestFixture]
public sealed class CritSuccumbTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SuccumbingKillsAndStaysDead(bool damageContinues)
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        var mobState = entMan.System<MobStateSystem>();
        var thresholds = entMan.System<MobThresholdSystem>();
        var damageable = entMan.System<DamageableSystem>();
        var blunt = server.ProtoMan.Index<DamageTypePrototype>("Blunt");
        var bloodloss = server.ProtoMan.Index<DamageTypePrototype>("Bloodloss");

        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            entMan.System<MapSystem>().CreateMap(out var mapId);
            human = entMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            // No ambient damage of the test world (vacuum, cold, no air), so the only damage there is, is what the test deals
            entMan.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(human);
            entMan.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(human);
            entMan.RemoveComponent<Content.Server.Temperature.Components.TemperatureComponent>(human);

            var mind = entMan.System<SharedMindSystem>();
            mind.TransferTo(mind.CreateMind(session.UserId), human);

            // Into crit with damage, as in a fight: just over the crit threshold, well short of the death one
            Assert.That(thresholds.TryGetThresholdForState(human, MobState.Critical, out var crit), Is.True);
            damageable.TryChangeDamage(human, new DamageSpecifier(blunt, crit!.Value + 1), true);
            Assert.That(mobState.IsCritical(human), Is.True, "The test character should be in crit.");
        });

        // The action
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(human, new Content.Shared.Mobs.CritSuccumbEvent { Performer = human }));
        await pair.RunTicksSync(5);

        // Damage keeps coming, like burning or no air
        for (var i = 0; i < (damageContinues ? 20 : 4); i++)
        {
            if (!damageContinues)
            {
                await pair.RunTicksSync(5);
                continue;
            }

            await server.WaitPost(() =>
            {
                if (entMan.EntityExists(human))
                    damageable.TryChangeDamage(human, new DamageSpecifier(bloodloss, 1), true, origin: null);
            });
            await pair.RunTicksSync(5);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(human) && mobState.IsDead(human) || !entMan.EntityExists(human), Is.True, "The character should be dead.");
            Assert.That(session.AttachedEntity, Is.Not.EqualTo(human), "The player should be in the death void, not back in the body.");
        });

        await pair.CleanReturnAsync();
    }
}
