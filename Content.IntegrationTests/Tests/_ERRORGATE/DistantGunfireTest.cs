#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._ERRORGATE.DistantGunfire;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class DistantGunfireTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: DistantGunfireTestGun
  components:
  - type: Gun

- type: entity
  id: DistantGunfireTestSilencedGun
  components:
  - type: Gun
    muzzleEffectRadius: 0

- type: entity
  id: DistantGunfireTestListener
";

    [Test]
    public async Task DistantGunfireReachesOnlyTheRightListeners()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var system = entMan.System<DistantGunfireSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();

        EntityUid listener = default;
        EntityUid gun = default;
        EntityUid silenced = default;
        EntityUid cartridge = default;
        EntityUid otherGrid = default;
        await server.WaitPost(() =>
        {
            listener = entMan.SpawnEntity("DistantGunfireTestListener", testMap.GridCoords);
            server.PlayerMan.SetAttachedEntity(session, listener);
            gun = entMan.SpawnEntity("DistantGunfireTestGun", testMap.GridCoords);
            silenced = entMan.SpawnEntity("DistantGunfireTestSilencedGun", testMap.GridCoords);
            cartridge = entMan.SpawnEntity("Cartridge556x45", testMap.GridCoords);
            var other = server.MapMan.CreateGridEntity(testMap.MapId);
            otherGrid = other;
            // a second grid 20 tiles off to the side (about 58 tiles from the shot at x = 55), with floor to stand on
            xform.SetWorldPosition(other, new System.Numerics.Vector2(0, 20));
            var mapSys = entMan.System<Robust.Server.GameObjects.MapSystem>();
            for (var x = -2; x <= 80; x++)
                mapSys.SetTile(other, new Robust.Shared.Maths.Vector2i(x, 0), new Robust.Shared.Map.Tile(1));
        });

        await server.WaitPost(() =>
        {
            // PVS is off in the test config, which makes everyone hear the real shot. Turn it on and give the test grid
            // a long strip of floor, the listeners are placed up to 70 tiles away.
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetPVS, true);
            var maps = entMan.System<Robust.Server.GameObjects.MapSystem>();
            for (var x = -2; x <= 80; x++)
                maps.SetTile(testMap.Grid, new Robust.Shared.Maths.Vector2i(x, 0), new Robust.Shared.Map.Tile(1));
        });

        var ammo = new List<(EntityUid? Uid, IShootable Shootable)>
        {
            (cartridge, entMan.GetComponent<CartridgeAmmoComponent>(cartridge)),
        };

        await server.WaitAssertion(() =>
        {
            var origin = xform.GetMapCoordinates(gun);
            var range = system.GetRange(gun, entMan.GetComponent<GunComponent>(gun), ammo);
            Assert.That(range, Is.EqualTo(65f), "5.56 should carry 65 tiles.");
            Assert.That(system.GetRange(silenced, entMan.GetComponent<GunComponent>(silenced), ammo), Is.EqualTo(0f),
                "A silenced gun should never be heard from afar.");

            // 60 tiles away on the same grid, in range.
            xform.SetCoordinates(listener, new EntityCoordinates(testMap.Grid, 60, 0));
            var audience = system.GetAudience(origin, testMap.Grid, range);
            Assert.That(audience.Exists(a => a.Session == session), "A listener 60 tiles away should hear the shot.");

            // Beyond the range.
            xform.SetCoordinates(listener, new EntityCoordinates(testMap.Grid, 70, 0));
            Assert.That(system.GetAudience(origin, testMap.Grid, range), Is.Empty, "Beyond the range nothing is heard.");

            // Inside the normal hearing range the real shot is heard instead.
            xform.SetCoordinates(listener, new EntityCoordinates(testMap.Grid, 10, 0));
            Assert.That(system.GetAudience(origin, testMap.Grid, range), Is.Empty, "No second copy for those in PVS range.");

            // Another grid in range is not on the shooter's grid.
            xform.SetCoordinates(listener, new EntityCoordinates(otherGrid, 55, 0));
            Assert.That(system.GetAudience(origin, testMap.Grid, range), Is.Empty, "Other grids do not hear the shot.");

            // A shooter outside any grid is heard anywhere on the map in range.
            Assert.That(system.GetAudience(origin, null, range).Exists(a => a.Session == session),
                "Without a grid every listener on the map in range hears the shot.");

            // The shooter never gets the distant copy.
            xform.SetCoordinates(listener, new EntityCoordinates(testMap.Grid, 60, 0));
            Assert.That(system.GetAudience(origin, testMap.Grid, range, listener), Is.Empty);
        });

        await pair.CleanReturnAsync();
    }
}
