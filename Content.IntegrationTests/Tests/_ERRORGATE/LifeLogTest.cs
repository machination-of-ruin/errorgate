#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._ERRORGATE.LifeLog;
using Content.Server.Chat.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Language;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     The log shown at death: who hurt this character, who they hurt, who they talked to, in fixed cold wording.
/// </summary>
[TestFixture]
public sealed class LifeLogTest
{
    private sealed class World
    {
        public Content.IntegrationTests.Pair.TestPair Pair = default!;
        public EntityUid Ivan;
        public EntityUid IvanMind;
        public EntityUid Boris;
        public EntityUid Rat;
    }

    /// <summary>
    ///     Ivan is the connected player, Boris a second player (a mind with an account but no connection), the rat a mob.
    /// </summary>
    private static async Task<World> Setup()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        var world = new World { Pair = pair };

        await server.WaitAssertion(() =>
        {
            entMan.System<MapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var meta = entMan.System<MetaDataSystem>();

            world.Ivan = entMan.SpawnEntity("MobHuman", coords);
            world.Boris = entMan.SpawnEntity("MobHuman", coords);
            world.Rat = entMan.SpawnEntity("MobHuman", coords);
            meta.SetEntityName(world.Ivan, "Ivan Petrov");
            meta.SetEntityName(world.Boris, "Boris");
            meta.SetEntityName(world.Rat, "sewer rat");

            var mind = entMan.System<SharedMindSystem>();
            world.IvanMind = mind.CreateMind(session.UserId);
            mind.TransferTo(world.IvanMind, world.Ivan);

            // Boris is a second player. There is no second connection in the test, an account on the mind is all the
            // log looks at.
            var borisMind = mind.CreateMind(null);
            // (reflection: the field is write protected, and the mind system refuses an account that is not connected)
            typeof(MindComponent).GetProperty(nameof(MindComponent.UserId))!.SetValue(entMan.GetComponent<MindComponent>(borisMind), (NetUserId?) new NetUserId(Guid.NewGuid()));
            mind.TransferTo(borisMind, world.Boris);
        });

        return world;
    }

    private static void Hurt(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid victim, EntityUid? origin, string type, int amount)
    {
        var spec = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>(type), FixedPoint2.New(amount));
        server.EntMan.System<DamageableSystem>().TryChangeDamage(victim, spec, origin: origin);
    }

    private static string Log(World world)
    {
        var lines = world.Pair.Server.EntMan.System<LifeLogSystem>().BuildDeathLog(world.IvanMind);
        Assert.That(lines, Is.Not.Null, "A character with a history should have a log.");
        return string.Join("\n", lines!);
    }

    [Test]
    public async Task PlayerAttackerSpeechAndKillsAreWrittenDown()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var entMan = server.EntMan;
        var universal = server.ProtoMan.Index<LanguagePrototype>("Universal");

        await server.WaitPost(() =>
        {
            // Boris hits Ivan twice, Ivan hits back, Ivan and Boris talk, Ivan kills the rat
            Hurt(server, world.Ivan, world.Boris, "Blunt", 12);
            Hurt(server, world.Ivan, world.Boris, "Blunt", 8);
            Hurt(server, world.Boris, world.Ivan, "Blunt", 5);

            entMan.EventBus.RaiseLocalEvent(world.Ivan, new EntitySpokeEvent(world.Ivan, "stay back", null, false, universal), true);
            entMan.EventBus.RaiseLocalEvent(world.Boris, new EntitySpokeEvent(world.Boris, "give me the rifle", null, false, universal), true);
            entMan.EventBus.RaiseLocalEvent(world.Ivan, new EntitySpokeEvent(world.Ivan, "wait", null, false, universal), true);

            Hurt(server, world.Rat, world.Ivan, "Blunt", 30);
            entMan.System<MobStateSystem>().ChangeMobState(world.Rat, MobState.Dead);
        });

        await server.WaitAssertion(() =>
        {
            var log = Log(world);
            Assert.That(log, Does.Contain(">>> LOG OF IVAN PETROV <<<"));
            Assert.That(log, Does.Contain("LAST HARMED BY BORIS: 20 DAMAGE."), "Two hits in a row add up.");
            Assert.That(log, Does.Contain("IT DELETED: SEWER RAT."));
            Assert.That(log, Does.Contain("IT SPOKE WITH BORIS: 2 LINES."));
            Assert.That(log, Does.Contain("LAST WORDS RECORDED: \"wait\""));
            Assert.That(log, Does.Contain("LESS THAN A MINUTE"));
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task HurtingAPlayerIsNotKillingOne()
    {
        var world = await Setup();
        var server = world.Pair.Server;

        await server.WaitPost(() => Hurt(server, world.Boris, world.Ivan, "Blunt", 7));

        await server.WaitAssertion(() =>
        {
            var log = Log(world);
            Assert.That(log, Does.Contain("IT HURT: BORIS."));
            Assert.That(log, Does.Not.Contain("DELETED"));
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task MobsFaultsAndTheEnvironmentAreNamed()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var entMan = server.EntMan;
        EntityUid heat = default;

        // A mob
        await server.WaitPost(() => Hurt(server, world.Ivan, world.Rat, "Piercing", 9));
        await server.WaitAssertion(() => Assert.That(Log(world), Does.Contain("LAST HARMED BY SEWER RAT: 9 DAMAGE.")));

        // A fault
        await server.WaitPost(() =>
        {
            heat = entMan.SpawnEntity("ErrorgateAnomalyHeat", new MapCoordinates(30, 30, entMan.GetComponent<TransformComponent>(world.Ivan).MapID));
            Hurt(server, world.Ivan, heat, "Heat", 6);
        });
        await server.WaitAssertion(() => Assert.That(Log(world), Does.Contain("LAST HARMED BY A HEAT FAULT: 6 DAMAGE.")));

        await world.Pair.CleanReturnAsync();
    }

    [TestCase("Bloodloss", "BLOOD LOSS")]
    [TestCase("Heat", "FIRE")]
    [TestCase("Cold", "COLD")]
    [TestCase("Poison", "POISON")]
    public async Task DamageNobodyDealtNamesItsCause(string type, string cause)
    {
        var world = await Setup();
        var server = world.Pair.Server;

        // No attacker, so it is the environment. Checked at once: the airless test floor soon adds damage of its own.
        await server.WaitAssertion(() =>
        {
            Hurt(server, world.Ivan, null, type, 2);
            var text = Log(world);
            Assert.That(text, Does.Contain("NO HAND WAS RAISED AGAINST IT. CAUSE: " + cause + "."), text);
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task NobodyIsRecordedForMobsWithoutAPlayer()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var lifeLog = server.EntMan.System<LifeLogSystem>();

        await server.WaitAssertion(() =>
        {
            // The rat is no player: it has no log
            Assert.That(server.EntMan.TryGetComponent(world.Rat, out Content.Shared.Mind.Components.MindContainerComponent? container) && container.Mind != null, Is.False);
            Assert.That(lifeLog.BuildDeathLog(world.Rat), Is.Null);
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task DyingWithALogDoesNotBreakTheDeathVoid()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var entMan = server.EntMan;
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();

        await server.WaitPost(() =>
        {
            Hurt(server, world.Ivan, world.Boris, "Blunt", 50);
            entMan.System<MobStateSystem>().ChangeMobState(world.Ivan, MobState.Dead);
        });
        await world.Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.Not.EqualTo(world.Ivan), "The dead player should be in the death void.");
        });

        await world.Pair.CleanReturnAsync();
    }
}
