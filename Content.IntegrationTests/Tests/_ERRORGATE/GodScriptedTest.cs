#nullable enable
using System.Linq;
using Content.Server._ERRORGATE.AiGod;
using Content.Shared._ERRORGATE.AiGod;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     The scripted director and its line bank: small signs, witnesses of a death, the answer to a prayer.
/// </summary>
[TestFixture]
public sealed class GodScriptedTest
{
    [Test]
    public async Task EveryLineInTheBankPassesTheVoiceRules()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var proto = pair.Server.ProtoMan;
        var validator = new GodActionValidator(proto.Index<GodVoicePrototype>("Default"));
        var lines = proto.Index<GodLinesPrototype>("Default");

        var about = new Subject { Number = 17, Name = "Ivan Petrov" };
        var context = new GodValidationContext();

        foreach (var (category, list) in new[] { ("error", lines.Error), ("omen", lines.Omen), ("prayer", lines.Prayer) })
        {
            Assert.That(list, Is.Not.Empty, $"The {category} list should not be empty.");
            foreach (var line in list)
            {
                var action = new GodAction { Type = GodActionType.Subtle, Targets = { 1 }, Text = GodScriptedSystem.Fill(line, about, "C4") };
                Assert.That(validator.Validate(action, context), Is.Null, $"{category}: {line}");
                Assert.That(action.Text, Does.Not.Contain("{"), $"{category}: {line} has an unfilled slot.");
            }
        }

        await pair.CleanReturnAsync();
    }

    private sealed class World
    {
        public Content.IntegrationTests.Pair.TestPair Pair = default!;
        public GodDirectorSystem Director = default!;
        public Robust.Shared.Player.ICommonSession Session = default!;
        public EntityUid Ivan;
        public EntityUid Boris;
        public Subject IvanSubject = default!;
        public Subject BorisSubject = default!;
    }

    /// <summary>
    ///     Ivan is the connected player and can be reached, Boris is a second subject without a connection. She is live,
    ///     needs no approval and has no cooldowns, but her own small signs are far apart so a test can tell what caused what.
    /// </summary>
    private static async Task<World> Setup(float whisperInterval = 100000f)
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var world = new World
        {
            Pair = pair,
            Director = entMan.System<GodDirectorSystem>(),
            Session = server.ResolveDependency<IPlayerManager>().Sessions.Single(),
        };

        await server.WaitAssertion(() =>
        {
            var cfg = server.CfgMan;
            cfg.SetCVar(ErrorgateCVars.GodEnabled, true);
            cfg.SetCVar(ErrorgateCVars.GodMode, "scripted");
            cfg.SetCVar(ErrorgateCVars.GodDryRun, false);
            cfg.SetCVar(ErrorgateCVars.GodApproval, false);
            cfg.SetCVar(ErrorgateCVars.GodQuietSeconds, 0f);
            cfg.SetCVar(ErrorgateCVars.GodMinPlayers, 1);
            cfg.SetCVar(ErrorgateCVars.GodTargetCooldown, 0f);
            cfg.SetCVar(ErrorgateCVars.GodBudgetCap, 100f);
            cfg.SetCVar(ErrorgateCVars.GodBudgetPerMinute, 60f);
            cfg.SetCVar(ErrorgateCVars.GodWhisperInterval, whisperInterval);
            cfg.SetCVar(ErrorgateCVars.GodPrayerDelay, 0.3f);

            entMan.System<GodObserverSystem>().Clear();
            var ledgerSystem = entMan.System<GodLedgerSystem>();
            ledgerSystem.Clear();
            world.Director.Reset();
            world.Director.Budget.Points = 100f;

            entMan.System<MapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            world.Ivan = entMan.SpawnEntity("MobHuman", coords);
            world.Boris = entMan.SpawnEntity("MobHuman", coords);
            entMan.System<MetaDataSystem>().SetEntityName(world.Ivan, "Ivan Petrov");
            entMan.System<MetaDataSystem>().SetEntityName(world.Boris, "Boris");

            var mind = entMan.System<SharedMindSystem>();
            mind.TransferTo(mind.CreateMind(world.Session.UserId), world.Ivan);

            var borisMind = mind.CreateMind(null);
            typeof(MindComponent).GetProperty(nameof(MindComponent.UserId))!.SetValue(entMan.GetComponent<MindComponent>(borisMind), (NetUserId?) new NetUserId(Guid.NewGuid()));
            mind.TransferTo(borisMind, world.Boris);

            Assert.That(ledgerSystem.TrySubject(world.Ivan, out world.IvanSubject!), Is.True);
            Assert.That(ledgerSystem.TrySubject(world.Boris, out world.BorisSubject!), Is.True);
        });

        return world;
    }

    private static void Kill(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid victim, EntityUid attacker)
    {
        var spec = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(900));
        server.EntMan.System<DamageableSystem>().TryChangeDamage(victim, spec, origin: attacker);
    }

    private static string Describe(World world) => string.Join(" | ", world.Director.Log.Select(d => d.ToString()));

    [Test]
    public async Task SmallSignsComeByThemselvesAndGoToReachableSubjects()
    {
        var world = await Setup(whisperInterval: 0.5f);

        await world.Pair.RunSeconds(6);

        await world.Pair.Server.WaitAssertion(() =>
        {
            var scripted = world.Director.Log.Where(d => d.Source == GodSource.Scripted).ToList();
            Assert.That(scripted, Is.Not.Empty, "She should have whispered.");
            Assert.That(scripted.All(d => d.Status == GodActionStatus.Executed), Is.True, Describe(world));
            Assert.That(scripted.All(d => d.Action.Targets.SequenceEqual(new[] { world.IvanSubject.Number })),
                Is.True, "Only Ivan can be reached, Boris has no connection.");
            Assert.That(scripted.Any(d => d.Action.Type == GodActionType.Subtle), Is.True);
        });

        await world.Pair.CleanReturnAsync();
    }

    [TestCase("off")]
    [TestCase("full")]
    public async Task NothingScriptedHappensInOffAndFullMode(string mode)
    {
        var world = await Setup(whisperInterval: 0.5f);
        await world.Pair.Server.WaitPost(() => world.Pair.Server.CfgMan.SetCVar(ErrorgateCVars.GodMode, mode));

        await world.Pair.RunSeconds(4);

        Assert.That(world.Director.Log, Is.Empty, Describe(world));
        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task WitnessesOfADeathHearOfIt()
    {
        var world = await Setup();
        var server = world.Pair.Server;

        // Ivan stood next to Boris when he died
        await server.WaitPost(() =>
        {
            world.BorisSubject.Near.Add(world.IvanSubject.Number);
            Kill(server, world.Boris, world.Ivan);
        });

        await world.Pair.RunSeconds(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(world.BorisSubject.Errors, Is.EqualTo(1));
            var sent = world.Director.Log.Where(d => d.Source == GodSource.Scripted).ToList();
            Assert.That(sent, Has.Count.EqualTo(1), Describe(world));
            Assert.That(sent[0].Status, Is.EqualTo(GodActionStatus.Executed));
            Assert.That(sent[0].Action.Targets, Is.EqualTo(new[] { world.IvanSubject.Number }));
            Assert.That(sent[0].Action.Text, Does.Contain("BORIS").Or.Contain($"S{world.BorisSubject.Number}"));
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADeathAloneIsSilent()
    {
        var world = await Setup();
        var server = world.Pair.Server;

        await server.WaitPost(() => Kill(server, world.Boris, world.Ivan));
        await world.Pair.RunSeconds(5);

        Assert.That(world.Director.Log, Is.Empty, Describe(world));
        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task APrayerAtAnAltarIsAnswered()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var altar = entMan.SpawnEntity("AltarHeaven", entMan.GetComponent<TransformComponent>(world.Ivan).Coordinates);
            var crowbar = entMan.SpawnEntity("Crowbar", entMan.GetComponent<TransformComponent>(world.Ivan).Coordinates);
            entMan.EventBus.RaiseEvent(EventSource.Local, new PrayedEvent(world.Session, crowbar, "no altar, no answer"));
            entMan.EventBus.RaiseEvent(EventSource.Local, new PrayedEvent(world.Session, altar, "Please."));
        });

        await world.Pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            var sent = world.Director.Log.Where(d => d.Source == GodSource.Scripted).ToList();
            Assert.That(sent, Has.Count.EqualTo(1), "Only the prayer at the altar is answered. " + Describe(world));
            Assert.That(sent[0].Status, Is.EqualTo(GodActionStatus.Executed));
            Assert.That(sent[0].Action.Targets, Is.EqualTo(new[] { world.IvanSubject.Number }));
        });

        await world.Pair.CleanReturnAsync();
    }
}
