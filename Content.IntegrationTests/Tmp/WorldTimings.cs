using System.Threading.Tasks;
using Content.Server._ERRORGATE.World;
using NUnit.Framework;

namespace Content.IntegrationTests.Tmp;

[TestFixture]
public sealed class WorldTimings
{
    [Test]
    public async Task Timings()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var world = server.EntMan.System<WorldSystem>();
        foreach (var id in new[] { "Village", "SnowCity", "Village", "SnowCity" })
            await server.WaitPost(() => Assert.That(world.TryBuild(id, 5, out var e), e));
        await pair.CleanReturnAsync();
    }
}
