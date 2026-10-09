#nullable enable
using Content.Server.Power.Components;
using Content.Shared.Damage;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class DoorShockTest
{
    [Test]
    public async Task PoweredFactoryDoorShocksAttacker()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            entMan.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var door = entMan.SpawnEntity("AirlockFactoryEntrance", coords);
            var human = entMan.SpawnEntity("MobHuman", coords);

            // unpowered: nothing happens
            entMan.GetComponent<ApcPowerReceiverComponent>(door).Powered = false;
            entMan.EventBus.RaiseLocalEvent(door, new AttackedEvent(human, human, new EntityCoordinates(door, 0, 0)));
            Assert.That((entMan.GetComponent<DamageableComponent>(human).Damage.DamageDict.TryGetValue("Shock", out var shock) ? shock.Int() : 0), Is.EqualTo(0));

            // powered: shock
            entMan.GetComponent<ApcPowerReceiverComponent>(door).Powered = true;
            entMan.EventBus.RaiseLocalEvent(door, new AttackedEvent(human, human, new EntityCoordinates(door, 0, 0)));
            Assert.That((entMan.GetComponent<DamageableComponent>(human).Damage.DamageDict.TryGetValue("Shock", out var shock2) ? shock2.Int() : 0), Is.GreaterThan(0));
        });

        await pair.CleanReturnAsync();
    }
}
