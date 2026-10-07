#nullable enable
using System.Linq;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     WWDP plays a death sound to the player (MobThresholdSounds). The player is moved into the death void when they die,
///     so make sure they still hear it.
/// </summary>
[TestFixture]
public sealed class DeathSoundReachesVoidTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: DeathSoundTestMortal
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
  - type: MobThresholdSounds
";

    [Test]
    public async Task PlayerStillHearsTheirDeathSound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();

        EntityUid body = default;
        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            body = entMan.SpawnEntity("DeathSoundTestMortal", new MapCoordinates(0, 0, mapId));
            var mind = entMan.System<SharedMindSystem>();
            mind.TransferTo(mind.CreateMind(session.UserId), body);
        });

        await server.WaitPost(() => entMan.System<MobStateSystem>().ChangeMobState(body, MobState.Dead));
        await pair.RunTicksSync(10);

        var serverSounds = entMan.EntityQuery<AudioComponent>().Select(a => a.FileName).Where(f => f.Contains("Death")).ToList();
        Assert.That(serverSounds, Is.Not.Empty, "The server never played a death sound.");

        // The player has to receive a sound that is not tied to the corpse, or the client would mute it.
        var clientGlobal = client.EntMan.EntityQuery<AudioComponent>().Select(a => (a.FileName, a.Global)).Where(a => a.Global && a.FileName.Contains("Death")).ToList();
        Assert.That(clientGlobal, Is.Not.Empty, "The death sound did not reach the player in the void.");

        await pair.CleanReturnAsync();
    }
}
