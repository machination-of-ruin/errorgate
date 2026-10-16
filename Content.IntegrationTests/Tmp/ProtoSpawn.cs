#nullable enable
using System.Threading.Tasks;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tmp;

// Throwaway: which of the feature prototypes cannot be spawned (not committed).
[TestFixture]
public sealed class ProtoSpawn
{
    [Test]
    public async Task Spawn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        foreach (var id in new[] { "N14CrateTrashcart", "N14CrateBreakPlainDamaged", "N14BurningBarrel", "BunkerRadiationBarrel", "N14BlackBarrel", "N14CrateMilitary", "N14CrateBreakPlain", "N14DecorFloorScrapwood", "N14BarricadeSandbagSingle", "N14BarricadeTanktrap", "MobSpawnerWalkerArmored", "Bonfire", "N14Bedroll", "N14CrateWooden", "N14DecorMinecart", "WallBrick", "N14WellBoarded", "N14DecorFloorBrickrubble" })
        {
            string result = "ok";
            await server.WaitPost(() =>
            {
                try { server.EntMan.SpawnEntity(id, map.GridCoords); }
                catch (System.Exception e) { result = "FAILED " + e.GetType().Name + " " + (e.InnerException?.Message ?? e.Message); }
            });
            TestContext.Out.WriteLine($"PSPAWN {id}: {result}");
        }

        await pair.CleanReturnAsync();
    }
}
