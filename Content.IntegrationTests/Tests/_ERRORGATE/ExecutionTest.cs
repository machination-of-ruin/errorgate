#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class ExecutionTest
{
    private static async Task<(TestPair Pair, EntityUid Attacker, EntityUid Victim, EntityUid Weapon)> Setup(string weaponId)
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid attacker = default, victim = default, weapon = default;
        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            attacker = entMan.SpawnEntity("MobHuman", coords);
            victim = entMan.SpawnEntity("MobHuman", coords);
            weapon = entMan.SpawnEntity(weaponId, coords);
            Assert.That(entMan.System<SharedHandsSystem>().TryPickup(attacker, weapon), "The attacker could not pick the weapon up.");
        });
        await pair.RunTicksSync(5);
        return (pair, attacker, victim, weapon);
    }

    private static async Task Execute(TestPair pair, EntityUid attacker, EntityUid victim)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var verbs = pair.Server.EntMan.System<SharedVerbSystem>().GetLocalVerbs(victim, attacker, typeof(UtilityVerb));
            var execute = verbs.FirstOrDefault(v => v.Text == "Execute");
            Assert.That(execute, Is.Not.Null, "There is no execute verb: " + string.Join(", ", verbs.Select(v => v.Text)));
            execute!.Act!.Invoke();
        });

        // The do-after takes a few seconds.
        await pair.RunTicksSync(300);
    }

    private static MobState State(TestPair pair, EntityUid uid) =>
        pair.Server.EntMan.GetComponent<MobStateComponent>(uid).CurrentState;

    [Test]
    public async Task GunExecutionShootsAConsciousVictim()
    {
        var (pair, attacker, victim, weapon) = await Setup("WeaponPistolMk58");

        // A pistol has to be racked before it fires, executions are no different.
        await pair.Server.WaitPost(() =>
        {
            var em = pair.Server.EntMan;
            em.System<SharedGunSystem>().SetBoltClosed(weapon, em.GetComponent<ChamberMagazineAmmoProviderComponent>(weapon), true);
        });
        await pair.RunTicksSync(2);

        Assert.That(State(pair, victim), Is.EqualTo(MobState.Alive), "The victim starts awake.");
        await Execute(pair, attacker, victim);

        Assert.That(State(pair, victim), Is.EqualTo(MobState.Dead), "The victim should have been shot dead.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MeleeExecutionKillsAConsciousVictim()
    {
        var (pair, attacker, victim, weapon) = await Setup("CombatKnife");

        await Execute(pair, attacker, victim);

        Assert.That(State(pair, victim), Is.EqualTo(MobState.Dead), "The victim should have been killed with the knife.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryGunCanExecute()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var victim = entMan.SpawnEntity("MobHuman", coords);

            foreach (var id in new[] { "WeaponRifleAk", "WeaponShotgunKammerer", "WeaponLightMachineGunL6", "WeaponPistolMk58" })
            {
                var attacker = entMan.SpawnEntity("MobHuman", coords);
                var gun = entMan.SpawnEntity(id, coords);
                Assert.That(entMan.System<SharedHandsSystem>().TryPickup(attacker, gun), $"Could not pick up {id}.");

                var verbs = entMan.System<SharedVerbSystem>().GetLocalVerbs(victim, attacker, typeof(UtilityVerb));
                Assert.That(verbs.Any(v => v.Text == "Execute"), $"{id} cannot be used to execute.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
