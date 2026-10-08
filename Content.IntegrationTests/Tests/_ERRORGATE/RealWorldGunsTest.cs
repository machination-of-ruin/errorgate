#nullable enable
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class RealWorldGunsTest
{
    private static readonly string[] Guns =
    {
        "WeaponPistolMakarov", "WeaponPistolBeretta", "WeaponPistolP226", "WeaponPistolM1911", "WeaponPistolStechkin",
        "WeaponPistolGlock", "WeaponPistolUSP", "WeaponRevolverSmithAndWesson", "WeaponRifleL1A1", "WeaponRifleASVal",
        "WeaponShotgunBenelli", "WeaponShotgunRemington", "WeaponSniperSKS", "WeaponSubMachineMP9",
        "WeaponSubMachineGunMP5", "WeaponSubMachineGunVector45ACP", "WeaponSubMachineGunVector9x19mm",
        "WeaponSubMachineGunThompson", "WeaponLightMachineM249",
    };

    [Test]
    public async Task RealWorldGunsSpawnAndAreLoaded()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            foreach (var proto in Guns)
            {
                var gun = entMan.SpawnEntity(proto, new MapCoordinates(0, 0, mapId));
                var ev = new GetAmmoCountEvent();
                entMan.EventBus.RaiseLocalEvent(gun, ref ev);
                Assert.That(ev.Count, Is.GreaterThan(0), $"{proto} should spawn loaded.");
                if (proto != "WeaponRevolverSmithAndWesson" && !proto.Contains("Shotgun") && proto != "WeaponSniperSKS")
                    Assert.That(ev.Count, Is.GreaterThanOrEqualTo(ev.Capacity - 1), $"{proto} should spawn with a full magazine (the chamber may be empty).");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RandomMagGunsAreNotAlwaysFull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            var partial = false;
            for (var i = 0; i < 60; i++)
            {
                var gun = entMan.SpawnEntity("WeaponPistolMakarovRandomMag", new MapCoordinates(0, 0, mapId));
                var ev = new GetAmmoCountEvent();
                entMan.EventBus.RaiseLocalEvent(gun, ref ev);
                Assert.That(ev.Count, Is.LessThanOrEqualTo(ev.Capacity));
                partial |= ev.Count < ev.Capacity - 1;
            }

            Assert.That(partial, "A random-magazine gun should sometimes spawn partly loaded.");
        });

        await pair.CleanReturnAsync();
    }
}
