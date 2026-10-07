#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class CritDeathSoundsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: CritDeathSoundsTestMortal
  components:
  - type: MindContainer
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: CritDeathSounds
";

    private static List<string> PlayingSounds(TestPair pair) =>
        pair.Server.EntMan.EntityQuery<AudioComponent>()
            .Select(a => a.FileName)
            .Where(f => f.Contains("_ERRORGATE"))
            .ToList();

    [Test]
    public async Task HeartbeatInCritAndDeathSoundOnDeath()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();

        EntityUid body = default;
        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            body = entMan.SpawnEntity("CritDeathSoundsTestMortal", new MapCoordinates(0, 0, mapId));
            var mind = entMan.System<SharedMindSystem>();
            mind.TransferTo(mind.CreateMind(session.UserId), body);
        });

        Assert.That(PlayingSounds(pair), Is.Empty);

        await server.WaitPost(() => entMan.System<MobStateSystem>().ChangeMobState(body, MobState.Critical));
        await pair.RunTicksSync(5);
        Assert.That(PlayingSounds(pair), Has.Some.Contain("Heart"), "No heartbeat while critical.");

        await server.WaitPost(() => entMan.System<MobStateSystem>().ChangeMobState(body, MobState.Dead));
        await pair.RunTicksSync(5);
        var sounds = PlayingSounds(pair);
        Assert.That(sounds, Has.None.Contain("Heart"), "The heartbeat must stop on death.");
        Assert.That(sounds, Has.Some.Contain("Death"), "No death sound.");

        await pair.CleanReturnAsync();
    }
}
