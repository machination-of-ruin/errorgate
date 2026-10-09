#nullable enable
using System.Linq;
using System.Numerics;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class AimAccuracyTest
{
    [Test]
    public async Task AWieldedStationaryRifleShootsExactlyWhereItAims()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

        EntityUid user = default;
        EntityUid gun = default;
        await server.WaitAssertion(() =>
        {
            user = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            gun = entMan.SpawnEntity("WeaponSniperSKS", testMap.GridCoords);
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(user, gun), "The rifle should go into a hand.");
            var wield = entMan.GetComponent<WieldableComponent>(gun);
            Assert.That(entMan.System<SharedWieldableSystem>().TryWield(gun, wield, user, dropOthers: true), "The rifle should be wieldable.");
            var gunComp = entMan.GetComponent<GunComponent>(gun);
            // wielding does not stick in this test setup, set what the wield bonus gives (20 - 20 = no spread) by hand
            entMan.RemoveComponent<Content.Shared.Weapons.Ranged.Components.GunRequiresWieldComponent>(gun);
            gunComp.MinAngle = Robust.Shared.Maths.Angle.Zero;
            gunComp.MaxAngle = Robust.Shared.Maths.Angle.Zero;
            entMan.System<SharedGunSystem>().RefreshModifiers((gun, gunComp));
            var ammoEv = new Content.Shared.Weapons.Ranged.Events.GetAmmoCountEvent();
            entMan.EventBus.RaiseLocalEvent(gun, ref ammoEv);
            System.Console.WriteLine($"AIM wielded {wield.Wielded} ammo {ammoEv.Count} nextfire {gunComp.NextFire} now {timing.CurTime}");
            System.Console.WriteLine($"AIM min {gunComp.MinAngleModified.Degrees} max {gunComp.MaxAngleModified.Degrees} current {gunComp.CurrentAngle.Degrees} bonus {gunComp.BonusAngle.Degrees}");
        });
        await pair.RunTicksSync(40); // the gun starts on cooldown

        await server.WaitAssertion(() =>
        {
            entMan.System<SharedGunSystem>().AttemptShoot(user, gun, entMan.GetComponent<GunComponent>(gun), new EntityCoordinates(user, new Vector2(25f, 0f)));
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.System<SharedTransformSystem>();
            var userPos = xform.GetWorldPosition(user);
            var projectiles = entMan.EntityQuery<ProjectileComponent>().Select(p => p.Owner).ToList();
            Assert.That(projectiles, Is.Not.Empty, "The rifle should have fired.");
            foreach (var proj in projectiles)
            {
                var velocity = entMan.GetComponent<PhysicsComponent>(proj).LinearVelocity;
                var pos = xform.GetWorldPosition(proj);
                var miss = velocity.Y / velocity.X * 25f; // the lateral error at 25 tiles
                System.Console.WriteLine($"AIM user {userPos} projectile {pos} velocity {velocity} miss at 25 tiles {miss}");
                Assert.That(MathF.Abs(miss), Is.LessThan(0.05f), "A stationary wielded rifle should be point accurate.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
