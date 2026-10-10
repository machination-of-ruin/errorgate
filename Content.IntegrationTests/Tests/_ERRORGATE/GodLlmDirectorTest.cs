#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._ERRORGATE.AiGod;
using Content.Shared._ERRORGATE.AiGod;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.Mind;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     The director that asks the model, with a fake model: what it sends, what it does with the answer, and what it does when
///     there is none.
/// </summary>
[TestFixture]
public sealed class GodLlmDirectorTest
{
    /// <summary>
    ///     A model that answers from a function of (system message, user message). It records every request.
    /// </summary>
    private sealed class FakeModel
    {
        public readonly List<(string System, string User)> Requests = new();
        public Func<string, string, (HttpStatusCode Status, string Reply)> Answer = (_, _) => (HttpStatusCode.OK, "{\"notes\": \"\", \"actions\": []}");

        public async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken token)
        {
            var body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(token);
            using var document = JsonDocument.Parse(body);
            var messages = document.RootElement.GetProperty("messages").EnumerateArray().ToList();
            var system = messages[0].GetProperty("content").GetString() ?? string.Empty;
            var user = messages[1].GetProperty("content").GetString() ?? string.Empty;
            Requests.Add((system, user));

            var (status, reply) = Answer(system, user);
            var json = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = reply } } } });
            return new HttpResponseMessage(status) { Content = new StringContent(json) };
        }
    }

    private sealed class World
    {
        public Content.IntegrationTests.Pair.TestPair Pair = default!;
        public GodDirectorSystem Director = default!;
        public GodLlmDirectorSystem Llm = default!;
        public GodObserverSystem Observer = default!;
        public GodDigestSystem Digest = default!;
        public FakeModel Model = default!;
        public Robust.Shared.Player.ICommonSession Session = default!;
        public EntityUid Ivan;
        public EntityUid Boris;
        public Subject IvanSubject = default!;
        public Subject BorisSubject = default!;
    }

    private static async Task<World> Setup(string mode = "assisted")
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var world = new World
        {
            Pair = pair,
            Director = entMan.System<GodDirectorSystem>(),
            Llm = entMan.System<GodLlmDirectorSystem>(),
            Observer = entMan.System<GodObserverSystem>(),
            Digest = entMan.System<GodDigestSystem>(),
            Model = new FakeModel(),
            Session = server.ResolveDependency<IPlayerManager>().Sessions.Single(),
        };

        await server.WaitAssertion(() =>
        {
            var cfg = server.CfgMan;
            cfg.SetCVar(ErrorgateCVars.GodEnabled, true);
            cfg.SetCVar(ErrorgateCVars.GodMode, mode);
            cfg.SetCVar(ErrorgateCVars.GodDryRun, false);
            cfg.SetCVar(ErrorgateCVars.GodApproval, false);
            cfg.SetCVar(ErrorgateCVars.GodQuietSeconds, 0f);
            cfg.SetCVar(ErrorgateCVars.GodMinPlayers, 1);
            cfg.SetCVar(ErrorgateCVars.GodTargetCooldown, 0f);
            cfg.SetCVar(ErrorgateCVars.GodBudgetCap, 100f);
            cfg.SetCVar(ErrorgateCVars.GodLlmApiUrl, "http://localhost:1/v1/chat/completions");
            cfg.SetCVar(ErrorgateCVars.GodLlmMinInterval, 0f);
            cfg.SetCVar(ErrorgateCVars.GodLlmEarlyGap, 0f);
            cfg.SetCVar(ErrorgateCVars.GodLlmInterval, 100000f);
            cfg.SetCVar(ErrorgateCVars.GodWriterInterval, 0f);
            cfg.SetCVar(ErrorgateCVars.GodWhisperInterval, 100000f);

            var llm = entMan.System<GodLlmSystem>();
            llm.Sender = world.Model.Send;
            llm.Reset();

            world.Observer.Clear();
            var ledgerSystem = entMan.System<GodLedgerSystem>();
            ledgerSystem.Clear();
            world.Director.Reset();
            entMan.System<GodScriptedSystem>().Reset();
            world.Director.Budget.Points = 100f;
            world.Llm.Reset();
            world.Digest.Notes = string.Empty;

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

    private static async Task Turn(World world)
    {
        Task task = Task.CompletedTask;
        await world.Pair.Server.WaitPost(() => task = world.Llm.TurnAsync());
        await task;
        await world.Pair.RunTicksSync(2);
    }

    private static void Pray(World world, string text)
    {
        var entMan = world.Pair.Server.EntMan;
        var altar = entMan.SpawnEntity("AltarHeaven", entMan.GetComponent<TransformComponent>(world.Ivan).Coordinates);
        entMan.EventBus.RaiseEvent(EventSource.Local, new PrayedEvent(world.Session, altar, text));
    }

    private static string Reply(string notes, params string[] actions)
    {
        return "{\"notes\": \"" + notes + "\", \"actions\": [" + string.Join(",", actions) + "]}";
    }

    [Test]
    public async Task TheDigestGoesInAndTheActionsComeOutThroughThePipeline()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var n = world.IvanSubject.Number;

        world.Model.Answer = (_, _) => (HttpStatusCode.OK,
            Reply("Prayers come from the north.", $"{{\"type\": \"subtle\", \"targets\": [\"S{n}\"], \"text\": \"S{n}: INPUT RECEIVED.\"}}"));

        await server.WaitPost(() => Pray(world, "Open the door. I did not mean to leave him there."));
        await Turn(world);

        Assert.That(world.Model.Requests, Has.Count.EqualTo(1));
        var (system, user) = world.Model.Requests[0];
        Assert.That(system, Does.Contain("You are MACHINATION OF RUIN"), "The fixed prompt is the system message.");
        Assert.That(system, Does.Contain("You NEVER guide"));
        Assert.That(user, Does.Contain("[ROUND]"));
        Assert.That(user, Does.Contain("PRAYER S" + n + " IVAN PETROV"));
        Assert.That(user, Does.Contain("Open the door. I did not mean to leave him there."), "Prayers go in raw.");

        await server.WaitAssertion(() =>
        {
            var llm = world.Director.Log.Where(d => d.Source == GodSource.Llm).ToList();
            Assert.That(llm, Has.Count.EqualTo(1));
            Assert.That(llm[0].Status, Is.EqualTo(GodActionStatus.Executed));
            Assert.That(world.Digest.Notes, Is.EqualTo("Prayers come from the north."));
            Assert.That(world.Llm.LastReply, Does.Contain("INPUT RECEIVED"));
            Assert.That(world.Director.LlmFailing, Is.False);
        });

        // The notes come back next turn, and the earlier action is in her recent actions
        await server.WaitPost(() => Pray(world, "Again."));
        await Turn(world);

        var second = world.Model.Requests[1].User;
        Assert.That(second, Does.Contain("[NOTES] Prayers come from the north."));
        Assert.That(second, Does.Contain("[HER RECENT]"));

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task BadActionsAreThrownAwayAndSaidSo()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var n = world.IvanSubject.Number;

        world.Model.Answer = (_, _) => (HttpStatusCode.OK, Reply("",
            $"{{\"type\": \"subtle\", \"targets\": [\"S{n}\"], \"text\": \"You must go to the factory.\"}}",
            "{\"type\": \"kill\", \"targets\": [\"S1\"]}",
            "{\"type\": \"subtle\", \"targets\": [\"S99\"], \"text\": \"ERROR.\"}",
            $"{{\"type\": \"subtle\", \"targets\": [\"S{n}\"], \"text\": \"ERROR LOGGED.\"}}",
            $"{{\"type\": \"subtle\", \"targets\": [\"S{n}\"], \"text\": \"ONE MORE THAN ALLOWED.\"}}"));

        await server.WaitPost(() => Pray(world, "Please."));
        await Turn(world);

        await server.WaitAssertion(() =>
        {
            var llm = world.Director.Log.Where(d => d.Source == GodSource.Llm).ToList();
            Assert.That(llm.Count(d => d.Status == GodActionStatus.Executed), Is.EqualTo(1), "Only the one good action among the first three is done.");
            Assert.That(llm.Any(d => d.Action.Text.Contains("ONE MORE THAN ALLOWED")), Is.False, "The action beyond the third is never offered.");
            Assert.That(llm.Any(d => d.Reason != null && d.Reason.Contains("order")), Is.True, string.Join(" | ", llm));
            Assert.That(llm.Any(d => d.Reason != null && d.Reason.Contains("unknown action type kill")), Is.True);
            Assert.That(llm.Any(d => d.Reason != null && d.Reason.Contains("no subject S99")), Is.True);
            Assert.That(llm.Any(d => d.Reason != null && d.Reason.Contains("more than 3 actions")), Is.True);
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARepliesWithoutJsonCountsAsNotAnswering()
    {
        var world = await Setup();
        var server = world.Pair.Server;

        world.Model.Answer = (_, _) => (HttpStatusCode.OK, "I am the machine. I will not answer in JSON.");

        await server.WaitPost(() => Pray(world, "Please."));
        await Turn(world);

        await server.WaitAssertion(() =>
        {
            Assert.That(world.Director.LlmFailing, Is.True);
            Assert.That(world.Director.Log.Last().Reason, Does.Contain("no JSON"));
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task WhenTheModelDoesNotAnswerNothingIsLostAndTheNextTurnHasIt()
    {
        var world = await Setup();
        var server = world.Pair.Server;

        world.Model.Answer = (_, _) => (HttpStatusCode.InternalServerError, "down");

        await server.WaitPost(() => Pray(world, "Is anyone there?"));
        await Turn(world);

        await server.WaitAssertion(() =>
        {
            Assert.That(world.Director.LlmFailing, Is.True);
            Assert.That(world.Observer.Buffer.Prayers, Has.Count.EqualTo(1), "The prayer goes back into the buffer.");
            Assert.That(world.Director.Log, Is.Empty);
        });

        // It answers again (the backoff after a failure is forgotten, as at a new round)
        world.Model.Answer = (_, _) => (HttpStatusCode.OK, Reply("back"));
        await server.WaitPost(() => server.EntMan.System<GodLlmSystem>().Reset());
        await Turn(world);

        Assert.That(world.Model.Requests.Last().User, Does.Contain("Is anyone there?"), "The prayer is in the next digest.");
        await server.WaitAssertion(() =>
        {
            Assert.That(world.Director.LlmFailing, Is.False);
            Assert.That(world.Observer.Buffer.Prayers, Is.Empty);
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task InFullModeTheScriptedRulesTakeOverWhileTheModelIsDown()
    {
        var world = await Setup(mode: "full");
        var server = world.Pair.Server;

        // Scripted rules are off while the model is fine
        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodWhisperInterval, 0.5f));
        await world.Pair.RunSeconds(3);
        Assert.That(world.Director.Log.Where(d => d.Source == GodSource.Scripted), Is.Empty, "In full mode only the model speaks.");

        // The model goes down
        world.Model.Answer = (_, _) => (HttpStatusCode.InternalServerError, "down");
        await server.WaitPost(() => Pray(world, "Hello?"));
        await Turn(world);
        Assert.That(world.Director.LlmFailing, Is.True);

        await world.Pair.RunSeconds(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(world.Director.Log.Any(d => d.Source == GodSource.Scripted && d.Status == GodActionStatus.Executed), Is.True,
                "The scripted whisper should have started.");
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task NothingToReportMeansNoCall()
    {
        var world = await Setup();

        await Turn(world);

        Assert.That(world.Model.Requests, Is.Empty);
        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task MentionLinesThatDoNotFitAreSummarisedFirst()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var n = world.IvanSubject.Number;

        world.Model.Answer = (system, _) => system.Contains("Summarise them")
            ? (HttpStatusCode.OK, "S" + n + " asks again and again whether anyone listens.")
            : (HttpStatusCode.OK, Reply(""));

        await server.WaitPost(() =>
        {
            for (var i = 1; i <= 13; i++)
            {
                world.Observer.Buffer.AddMention(new GodQuote(TimeSpan.FromSeconds(i), n, "Ivan Petrov", "C4", $"god are you there {i}", false));
            }
        });

        await Turn(world);

        Assert.That(world.Model.Requests, Has.Count.EqualTo(2), "A summary call, then the main one.");
        Assert.That(world.Model.Requests[0].System, Does.Contain("Summarise them"));
        Assert.That(world.Model.Requests[0].User, Does.Contain("god are you there 1\""));
        Assert.That(world.Model.Requests[0].User, Does.Not.Contain("god are you there 13"), "Only the surplus is summarised.");

        var main = world.Model.Requests[1].User;
        Assert.That(main, Does.Contain("EARLIER MENTIONS (summary): S" + n + " asks again"));
        Assert.That(main, Does.Contain("god are you there 13"), "The newest ten go in raw.");
        Assert.That(main, Does.Not.Contain("god are you there 2\""));

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task WriterModeAddsOnlyLinesThatPassTheRules()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodWriterInterval, 100000f));

        world.Model.Answer = (system, _) => system.Contains("You write lines")
            ? (HttpStatusCode.OK, "{\"error\": [\"{name} WAS COUNTED.\", \"You should run, {name}.\", \"{name} {secret}.\", \"ONE LESS.\"], " +
                                  "\"omen\": [\"SECTOR {sector} IS QUIET.\", \"SECTOR {sector} IS QUIET.\"], \"prayer\": [\"NOTHING WAS HEARD.\"]}")
            : (HttpStatusCode.OK, Reply(""));

        await server.WaitPost(() => Pray(world, "Please."));
        await Turn(world);

        await server.WaitAssertion(() =>
        {
            var extra = world.Director.ExtraLines;
            Assert.That(extra["error"], Is.EqualTo(new[] { "{name} WAS COUNTED.", "ONE LESS." }).Or.EqualTo(new[] { "{name} WAS COUNTED." }),
                "An order and an unknown slot are left out; a line the bank already has is a repeat.");
            Assert.That(extra["omen"], Is.EqualTo(new[] { "SECTOR {sector} IS QUIET." }), "No duplicates.");
            Assert.That(extra["prayer"], Is.EqualTo(new[] { "NOTHING WAS HEARD." }));
        });

        await world.Pair.CleanReturnAsync();
    }

    [TestCase("scripted")]
    [TestCase("off")]
    public async Task OtherModesNeverCallTheModel(string mode)
    {
        var world = await Setup(mode);
        var server = world.Pair.Server;
        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodLlmInterval, 0.5f));

        await server.WaitPost(() => Pray(world, "Please."));
        await world.Pair.RunSeconds(4);

        Assert.That(world.Model.Requests, Is.Empty);
        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task APrayerAndARunOfDeathsCallTheModelSoon()
    {
        var world = await Setup();
        var server = world.Pair.Server;

        // A prayer at an altar
        await server.WaitPost(() => Pray(world, "Please."));
        await world.Pair.RunSeconds(3);
        Assert.That(world.Model.Requests, Has.Count.EqualTo(1), "A prayer should not wait for the next turn.");

        // Three deaths within a minute
        await server.WaitPost(() =>
        {
            world.Observer.Buffer.Add(new GodEvent(TimeSpan.Zero, GodEventKind.Death, GodEventSeverity.High, "Boris died.", new[] { "Boris" }));
            for (var i = 0; i < 3; i++)
            {
                server.EntMan.EventBus.RaiseEvent(EventSource.Local, new GodSubjectDiedEvent(world.BorisSubject));
            }
        });

        await world.Pair.RunSeconds(3);
        Assert.That(world.Model.Requests, Has.Count.EqualTo(2));

        await world.Pair.CleanReturnAsync();
    }
}
