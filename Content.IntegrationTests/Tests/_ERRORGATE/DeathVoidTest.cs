#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Ghost;
using Content.Shared._ERRORGATE.DeathVoid;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Console;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class DeathVoidTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: DeathVoidTestMortal
  components:
  - type: MindContainer
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      200: Dead
";

    private static async Task<(TestPair Pair, EntityUid Body, EntityUid MindId, ICommonSession Session)> Setup()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSystem = entMan.System<SharedMindSystem>();
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();

        EntityUid body = default;
        EntityUid mindId = default;
        await server.WaitAssertion(() =>
        {
            body = entMan.SpawnEntity("DeathVoidTestMortal", MapCoordinates.Nullspace);
            mindId = mindSystem.CreateMind(session.UserId);
            mindSystem.TransferTo(mindId, body);
            Assert.That(session.AttachedEntity, Is.EqualTo(body));
        });

        return (pair, body, mindId, session);
    }

    private static async Task SetDamage(TestPair pair, EntityUid body, int amount)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.EntMan;
            var proto = pair.Server.ProtoMan.Index<DamageTypePrototype>("Blunt");
            var damageable = entMan.GetComponent<DamageableComponent>(body);
            entMan.System<DamageableSystem>().SetDamage(body, damageable,
                new DamageSpecifier(proto, FixedPoint2.New(amount)));
        });
        await pair.RunTicksSync(5);
    }

    [Test]
    public async Task DeadPlayerIsDisconnectedFromTheWorldAndReturnsOnRevive()
    {
        var (pair, body, mindId, session) = await Setup();
        var server = pair.Server;
        var entMan = server.EntMan;

        await SetDamage(pair, body, 200);

        EntityUid voidEnt = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<MobStateComponent>(body).CurrentState, Is.EqualTo(MobState.Dead));

            // The player is parked in the void, not on a ghost and not on the corpse.
            Assert.That(session.AttachedEntity, Is.Not.Null.And.Not.EqualTo(body));
            voidEnt = session.AttachedEntity!.Value;
            Assert.That(entMan.HasComponent<DeathVoidComponent>(voidEnt));
            Assert.That(entMan.HasComponent<Content.Shared.Ghost.GhostComponent>(voidEnt), Is.False);

            // The mind still belongs to the corpse and is on another map than it.
            var mind = entMan.GetComponent<MindComponent>(mindId);
            Assert.That(mind.OwnedEntity, Is.EqualTo(body));
            Assert.That(mind.VisitingEntity, Is.EqualTo(voidEnt));
            Assert.That(entMan.GetComponent<TransformComponent>(voidEnt).MapID,
                Is.Not.EqualTo(entMan.GetComponent<TransformComponent>(body).MapID));
        });

        // Ghosting is refused and keeps the player in the void.
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.System<GhostSystem>().OnGhostAttempt(mindId, true), Is.False);
            Assert.That(session.AttachedEntity, Is.EqualTo(voidEnt));
        });

        // Reviving the body brings the player back and cleans the void up.
        await server.WaitPost(() => entMan.System<MobStateSystem>().ChangeMobState(body, MobState.Alive));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<MobStateComponent>(body).CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(session.AttachedEntity, Is.EqualTo(body));
            Assert.That(entMan.EntityExists(voidEnt), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeletedBodyLeadsToTheVoidNotAGhost()
    {
        var (pair, body, mindId, session) = await Setup();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => entMan.DeleteEntity(body));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.Not.Null);
            var attached = session.AttachedEntity!.Value;
            Assert.That(entMan.HasComponent<DeathVoidComponent>(attached));
            Assert.That(entMan.HasComponent<Content.Shared.Ghost.GhostComponent>(attached), Is.False);
            Assert.That(entMan.GetComponent<MindComponent>(mindId).OwnedEntity, Is.EqualTo(attached));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlayerPushedOutOfTheirBodyGoesToTheVoid()
    {
        var (pair, body, mindId, session) = await Setup();
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid otherMind = default;
        await server.WaitPost(() =>
        {
            var mind = entMan.System<SharedMindSystem>();
            otherMind = mind.CreateMind(null);
            mind.TransferTo(otherMind, body);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var attached = session.AttachedEntity!.Value;
            Assert.That(entMan.HasComponent<DeathVoidComponent>(attached));
            Assert.That(entMan.GetComponent<MindComponent>(otherMind).OwnedEntity, Is.EqualTo(body));
            Assert.That(entMan.GetComponent<MindComponent>(mindId).OwnedEntity, Is.Not.EqualTo(body));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeadPlayerCanRespawn()
    {
        var (pair, body, mindId, session) = await Setup();
        var server = pair.Server;
        var entMan = server.EntMan;
        var console = server.ResolveDependency<IServerConsoleHost>();

        await SetDamage(pair, body, 200);
        await server.WaitPost(() => console.ExecuteCommand(session, "rise"));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.Not.EqualTo(body));
            Assert.That(entMan.GetComponent<MindComponent>(mindId).OwnedEntity, Is.Null.Or.Not.EqualTo(body));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RespawnWaitsForTheCooldown()
    {
        var (pair, body, mindId, session) = await Setup();
        var server = pair.Server;
        var entMan = server.EntMan;
        var console = server.ResolveDependency<IServerConsoleHost>();
        var cfg = server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>();

        await server.WaitPost(() => cfg.SetCVar(Content.Shared._ERRORGATE.CCVar.ErrorgateCVars.RespawnCooldown, 3600f));
        await SetDamage(pair, body, 200);
        var voidEnt = session.AttachedEntity;
        await server.WaitPost(() => console.ExecuteCommand(session, "rise"));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(voidEnt), "The cooldown should keep the player in the void.");
            Assert.That(entMan.GetComponent<DeathVoidComponent>(voidEnt!.Value).RespawnAt, Is.Not.Null);
        });

        await server.WaitPost(() => cfg.SetCVar(Content.Shared._ERRORGATE.CCVar.ErrorgateCVars.RespawnCooldown, 0f));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GibbedPlayerGoesToTheVoidNotToABrain()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();

        var oldBrains = new System.Collections.Generic.HashSet<EntityUid>();
        var oldParts = new System.Collections.Generic.HashSet<EntityUid>();
        await server.WaitPost(() =>
        {
            foreach (var (uid, _) in entMan.EntityQuery<Content.Server.Body.Components.BrainComponent>(true).Select(x => (x.Owner, x)))
                oldBrains.Add(uid);
            foreach (var (uid, _) in entMan.EntityQuery<Content.Shared.Body.Part.BodyPartComponent>(true).Select(x => (x.Owner, x)))
                oldParts.Add(uid);
        });

        await server.WaitAssertion(() =>
        {
            var body = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var mindSystem = entMan.System<SharedMindSystem>();
            var mindId = mindSystem.CreateMind(session.UserId);
            mindSystem.TransferTo(mindId, body);
            // what an explosion does: gib the body together with its organs
            entMan.System<Content.Server.Body.Systems.BodySystem>().GibBody(body, gibOrgans: true);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<Content.Server.Body.Components.BrainComponent>().Any(b => !oldBrains.Contains(b.Owner)), Is.False,
                "Gibbing should not leave a brain lying around.");
            Assert.That(entMan.EntityQuery<Content.Shared.Body.Part.BodyPartComponent>().Any(p => !oldParts.Contains(p.Owner)), Is.False,
                "Gibbing should not leave hands, feet or other limbs lying around.");
            Assert.That(session.AttachedEntity, Is.Not.Null);
            Assert.That(entMan.HasComponent<DeathVoidComponent>(session.AttachedEntity), "The mind should be in the void.");
        });

        await pair.CleanReturnAsync();
    }
}
