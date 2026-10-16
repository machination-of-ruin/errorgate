#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._ERRORGATE.World;
using NUnit.Framework;

namespace Content.IntegrationTests.Tmp;

// Throwaway: how many of the hand-cut buildings every city build puts out (not committed).
[TestFixture]
public sealed class UniqueCheck
{
    [Test]
    public async Task Count()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var world = server.EntMan.System<WorldSystem>();
        int noFactory = 0, factories = 0, residential = 0;
        var n = 30;
        for (var seed = 1; seed <= n; seed++)
        {
            await server.WaitPost(() =>
            {
                Assert.That(world.TryBuild("SnowCity", seed, out var e), e);
                var paths = world.Locations["SnowCity"].Built.Select(b => b.Placement.Info.Path).ToList();
                var f = paths.Count(p => p.Contains("factory"));
                var r = paths.Count(p => p.Contains("apartments") || p.Contains("dormitory"));
                factories += f;
                residential += r;
                if (f == 0) noFactory++;
                world.Teardown("SnowCity");
            });
        }

        TestContext.Out.WriteLine($"UNIQUE over {n} city builds: no factory in {noFactory}, factories per build {factories / (double) n:0.0}, residential per build {residential / (double) n:0.0}");
        await pair.CleanReturnAsync();
    }
}
