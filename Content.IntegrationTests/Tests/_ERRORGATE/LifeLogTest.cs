#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server._ERRORGATE.LifeLog;
using Content.Server.Chat.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Language;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     The log shown at death: the last things that happened to the character as a technical record, in fixed wording.
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

    private static void Say(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid who, string words, bool whisper = false)
    {
        var universal = server.ProtoMan.Index<LanguagePrototype>("Universal");
        server.EntMan.EventBus.RaiseLocalEvent(who, new EntitySpokeEvent(who, words, null, whisper, universal), true);
    }

    private static List<string> Lines(World world)
    {
        var lines = world.Pair.Server.EntMan.System<LifeLogSystem>().BuildDeathLog(world.IvanMind);
        Assert.That(lines, Is.Not.Null, "A character with a history should have a log.");
        return lines!;
    }

    [Test]
    public async Task TheLogListsWhatHappenedAsTechnicalLines()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            Say(server, world.Ivan, "stay back");
            Say(server, world.Boris, "give me the rifle");
            Hurt(server, world.Ivan, world.Boris, "Blunt", 12);
            Hurt(server, world.Ivan, world.Boris, "Blunt", 8);
            Hurt(server, world.Boris, world.Ivan, "Blunt", 5);
            Hurt(server, world.Rat, world.Ivan, "Blunt", 30);
            entMan.System<MobStateSystem>().ChangeMobState(world.Rat, MobState.Dead);
            Say(server, world.Ivan, "wait", whisper: true);
        });

        await server.WaitAssertion(() =>
        {
            var lines = Lines(world);
            var text = string.Join("\n", lines);

            Assert.That(lines[0], Is.EqualTo(">>> LOG <<<"));
            Assert.That(lines[1], Does.Match(@"^T-\d\d:\d\d  SUBJECT INSTANTIATED$"));
            Assert.That(lines[^1], Is.EqualTo("T-00:00  SUBJECT TERMINATED"));

            Assert.That(text, Does.Contain("SPEECH      \"stay back\""));
            Assert.That(text, Does.Contain("HEARD       BORIS: \"give me the rifle\""));
            Assert.That(text, Does.Contain("DAMAGE IN   BORIS: 20 x2"), "Hits in a row from one source are one line.");
            Assert.That(text, Does.Contain("DAMAGE OUT  BORIS: 5"));
            Assert.That(text, Does.Contain("DAMAGE OUT  SEWER RAT: 30"));
            Assert.That(text, Does.Contain("DELETED     SEWER RAT"));
            Assert.That(text, Does.Contain("WHISPER     \"wait\""));

            // Impersonal and not a stat block
            Assert.That(text, Does.Not.Contain("IVAN"), "The log never names the dead character.");
            Assert.That(text, Does.Not.Contain("LOG OF"));
            Assert.That(text, Does.Not.Contain(" IT "));
            Assert.That(text, Does.Not.Contain("YOU"));

            // In the order it happened
            Assert.That(text.IndexOf("SPEECH", StringComparison.Ordinal), Is.LessThan(text.IndexOf("DAMAGE IN", StringComparison.Ordinal)));
            Assert.That(text.IndexOf("DELETED", StringComparison.Ordinal), Is.LessThan(text.IndexOf("WHISPER", StringComparison.Ordinal)));
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyTheLastEightEventsAreShown()
    {
        var world = await Setup();
        var server = world.Pair.Server;

        await server.WaitPost(() =>
        {
            for (var i = 1; i <= 12; i++)
            {
                Say(server, world.Ivan, $"line number {i}");
            }
        });

        await server.WaitAssertion(() =>
        {
            var lines = Lines(world);
            var text = string.Join("\n", lines);

            // header, 8 events, closing line
            Assert.That(lines, Has.Count.EqualTo(10), text);
            Assert.That(text, Does.Not.Contain("INSTANTIATED"), "A long life no longer shows its beginning.");
            Assert.That(text, Does.Not.Contain("line number 4\""));
            Assert.That(text, Does.Contain("line number 5\""));
            Assert.That(text, Does.Contain("line number 12\""));
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task LongSpeechIsCut()
    {
        var world = await Setup();
        var server = world.Pair.Server;

        await server.WaitPost(() => Say(server, world.Ivan, new string('x', 100)));

        await server.WaitAssertion(() =>
        {
            var text = string.Join("\n", Lines(world));
            Assert.That(text, Does.Contain(new string('x', 40) + "..."));
            Assert.That(text, Does.Not.Contain(new string('x', 41)));
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task MobsAndFaultsAreNamed()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            Hurt(server, world.Ivan, world.Rat, "Piercing", 9);
            var heat = entMan.SpawnEntity("ErrorgateAnomalyHeat", new MapCoordinates(30, 30, entMan.GetComponent<TransformComponent>(world.Ivan).MapID));
            Hurt(server, world.Ivan, heat, "Heat", 6);
        });

        await server.WaitAssertion(() =>
        {
            var text = string.Join("\n", Lines(world));
            Assert.That(text, Does.Contain("DAMAGE IN   SEWER RAT: 9"));
            Assert.That(text, Does.Contain("DAMAGE IN   HEAT FAULT: 6"));
        });

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
            var text = string.Join("\n", Lines(world));
            Assert.That(text, Does.Contain("DAMAGE IN   " + cause + ": 2"), text);
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task MobsWithoutAPlayerHaveNoLog()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var lifeLog = server.EntMan.System<LifeLogSystem>();

        await server.WaitAssertion(() => Assert.That(lifeLog.BuildDeathLog(world.Rat), Is.Null));

        await world.Pair.CleanReturnAsync();
    }

    // The death message as the player's client receives it

    private static async Task<List<(string Channel, string Message, string Wrapped)>> ChatAfterDeath(World world, bool deleteBodyAfterwards)
    {
        var server = world.Pair.Server;
        var entMan = server.EntMan;
        var chat = world.Pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();

        // The client is reused between tests, with the chat of the earlier ones still in it
        await world.Pair.Client.WaitPost(() => chat.History.Clear());

        await server.WaitPost(() =>
        {
            Say(server, world.Ivan, "wait");
            Hurt(server, world.Ivan, world.Boris, "Blunt", 50);
            entMan.System<MobStateSystem>().ChangeMobState(world.Ivan, MobState.Dead);
        });
        await world.Pair.RunTicksSync(10);

        if (deleteBodyAfterwards)
        {
            // What a collapse fault does to a corpse: the body is gibbed, the player is already in the void
            await server.WaitPost(() => entMan.System<SharedBodySystem>().GibBody(world.Ivan, true));
            await world.Pair.RunTicksSync(10);
        }

        var result = new List<(string, string, string)>();
        await world.Pair.Client.WaitPost(() =>
        {
            foreach (var (_, msg) in chat.History)
            {
                result.Add((msg.Channel.ToString(), msg.Message, msg.WrappedMessage));
            }
        });

        return result;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TheDeathMessageComesOnceWithTheLogBeforeTheSubtitle(bool bodyDeletedAfterwards)
    {
        var world = await Setup();

        var chat = await ChatAfterDeath(world, bodyDeletedAfterwards);
        var errors = chat.Where(m => m.Wrapped.Contains("YOU ARE DEAD")).ToList();

        Assert.That(errors, Has.Count.EqualTo(1), "The death message must not be sent twice. ");

        var wrapped = errors[0].Wrapped;
        var title = wrapped.IndexOf("YOU ARE DEAD", StringComparison.Ordinal);
        var log = wrapped.IndexOf(">>> LOG <<<", StringComparison.Ordinal);
        var end = wrapped.IndexOf("SUBJECT TERMINATED", StringComparison.Ordinal);
        var subtitle = wrapped.IndexOf("YOU FAILED TO ESCAPE", StringComparison.Ordinal);

        Assert.That(new[] { title, log, end, subtitle }, Is.Ordered.Ascending.And.All.GreaterThanOrEqualTo(0),
            "Title, then the log, then the subtitle at the bottom.");
        Assert.That(wrapped, Does.Contain("SPEECH      \"wait\""));

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheDeadDoNotSeeTheirOwnDeathEmote()
    {
        var world = await Setup();

        var chat = await ChatAfterDeath(world, false);

        Assert.That(chat.Where(m => m.Channel == "Emotes" && m.Message.Contains("seizes up")), Is.Empty,
            "The player is in the void by then and sees nothing of the body.");

        await world.Pair.CleanReturnAsync();
    }
}
