#nullable enable
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     The mobs on the ERRORGATE maps keep the stats of the old build (the upstream forks changed them).
/// </summary>
[TestFixture]
public sealed class MapMobStatsTest
{
    // proto, health at death, base walk speed, base sprint speed (null: not checked)
    [TestCase("MobGiantSpiderAngry", 50, null, null)]
    [TestCase("MobCarp", 50, null, null)]
    [TestCase("MobShark", 200, null, null)]
    [TestCase("MobRatKing", 300, null, null)]
    [TestCase("MobWatcherLavaland", 50, 5f, 7f)]
    public async Task MobsKeepTheirOldStats(string proto, int dead, float? walk, float? sprint)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mob = entMan.SpawnEntity(proto, testMap.GridCoords);
            Assert.That(entMan.System<MobThresholdSystem>().TryGetDeadThreshold(mob, out var threshold), Is.True);
            Assert.That((int) threshold!.Value, Is.EqualTo(dead), $"{proto} health");

            var speed = entMan.GetComponent<MovementSpeedModifierComponent>(mob);
            if (walk != null)
                Assert.That(speed.BaseWalkSpeed, Is.EqualTo(walk.Value), $"{proto} walk speed");
            if (sprint != null)
                Assert.That(speed.BaseSprintSpeed, Is.EqualTo(sprint.Value), $"{proto} sprint speed");
        });

        await pair.CleanReturnAsync();
    }
}
