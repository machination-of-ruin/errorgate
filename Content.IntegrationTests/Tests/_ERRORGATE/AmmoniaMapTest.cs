#nullable enable
using System.Linq;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class AmmoniaMapTest
{
    [TestCase("Kuznetsk")]
    [TestCase("EdgeOfEntropy")]
    public async Task PoisonGasOnTheMapHurts(string map)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            var ticker = entMan.System<Content.Server.GameTicking.GameTicker>();
            var opts = Robust.Shared.EntitySerialization.DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(server.ProtoMan.Index<Content.Server.Maps.GameMapPrototype>(map), out _, opts);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Content.Server.Atmos.TileAtmosphere? best = null;
            EntityUid bestGrid = default;
            foreach (var (uid, grid) in entMan.EntityQuery<GridAtmosphereComponent>().Select(g => (g.Owner, g)))
            {
                foreach (var tile in grid.Tiles.Values)
                {
                    if (tile.Air == null) continue;
                    if (best == null || tile.Air[(int) Gas.Ammonia] > best.Air![(int) Gas.Ammonia])
                    {
                        best = tile;
                        bestGrid = uid;
                    }
                }
            }

            Assert.That(best, Is.Not.Null);
            System.Console.WriteLine($"AMMONIA {map}: max {best!.Air![(int) Gas.Ammonia]} mol at {best.GridIndices} pressure {best.Air.Pressure}");
            Assert.That(best.Air[(int) Gas.Ammonia], Is.GreaterThan(1f), "The map should have ammonia somewhere.");
            human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(bestGrid, best.GridIndices + new System.Numerics.Vector2(0.5f, 0.5f)));
        });
        await pair.RunSeconds(25);

        await server.WaitAssertion(() =>
        {
            var damage = entMan.GetComponent<DamageableComponent>(human).Damage.DamageDict;
            System.Console.WriteLine("AMMONIA damage: " + string.Join(", ", damage.Where(d => d.Value > 0).Select(d => d.Key + "=" + d.Value)));
            Assert.That(damage.TryGetValue("Poison", out var poison) ? poison.Float() : 0f, Is.GreaterThan(0f), "The poison gas should poison.");
        });

        await pair.CleanReturnAsync();
    }
}
