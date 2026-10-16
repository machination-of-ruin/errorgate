using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._ERRORGATE.World;
using NUnit.Framework;

namespace Content.IntegrationTests.Tmp;

// Throwaway soak of the whole web: builds and storms, entity counts and memory (not committed).
[TestFixture]
public sealed class WorldGraphSoak
{
    [Test]
    public async Task Soak()
    {
        var rounds = int.Parse(Environment.GetEnvironmentVariable("SOAK_ROUNDS") ?? "4");
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var world = server.EntMan.System<WorldSystem>();
        await server.WaitRunTicks(5);
        int baseline = 0;
        await server.WaitPost(() => baseline = server.EntMan.EntityCount);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var mem0 = GC.GetTotalMemory(true) / 1048576;
        TestContext.Out.WriteLine($"WSOAK baseline {baseline} entities, {mem0} MB");
        for (var r = 0; r < rounds; r++)
        {
            var sw = Stopwatch.StartNew();
            await server.WaitPost(() => Assert.That(world.TryBuildGraph("Default", r * 13 + 5, out var e), e));
            var buildMs = sw.Elapsed.TotalMilliseconds;
            await server.WaitRunTicks(10);
            int built = 0;
            await server.WaitPost(() => built = server.EntMan.EntityCount - baseline);

            // storms: wipe every location that resets, twice
            double worst = 0;
            foreach (var id in new[] { "Village", "SnowCity", "Laboratory", "Village", "SnowCity", "Laboratory" })
            {
                sw.Restart();
                await server.WaitPost(() => world.Strike(id));
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
                await server.WaitRunTicks(5);
            }

            await server.WaitRunTicks(1500);
            int afterStorms = 0;
            await server.WaitPost(() => afterStorms = server.EntMan.EntityCount - baseline);
            foreach (var id in world.Locations.Keys.ToList())
                await server.WaitPost(() => world.Teardown(id));
            await server.WaitRunTicks(10);
            int left = 0;
            await server.WaitPost(() => left = server.EntMan.EntityCount - baseline);
            var mem = GC.GetTotalMemory(true) / 1048576;
            TestContext.Out.WriteLine($"WSOAK round {r}: whole web built in {buildMs:0} ms with {built} entities, after 6 storms {afterStorms} entities (worst storm {worst:0} ms), after tearing everything down {left} left, memory {mem} MB");
        }
        await pair.CleanReturnAsync();
    }
}
