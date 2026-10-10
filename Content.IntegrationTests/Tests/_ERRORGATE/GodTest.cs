#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._ERRORGATE.AiGod;
using Content.Server.Chat.Systems;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Language;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     The AI GOD plumbing: the LLM client (no network, a fake sender), the event buffer and the observer.
/// </summary>
[TestFixture]
public sealed class GodTest
{
    private const string Reply = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"<think>hmm</think>  THE DOOR REMEMBERS.  \"}}]}";

    private sealed class FakeApi
    {
        public readonly List<HttpRequestMessage> Requests = new();
        public readonly List<string> Bodies = new();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = Reply;
        public bool Throw;

        public async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(token));

            if (Throw)
                throw new HttpRequestException("no route to host");

            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    private static async Task<(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance Server, GodLlmSystem Llm, FakeApi Api, Content.IntegrationTests.Pair.TestPair Pair)> LlmSetup(float minInterval = 0f)
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var llm = server.EntMan.System<GodLlmSystem>();
        var api = new FakeApi();

        await server.WaitPost(() =>
        {
            llm.Sender = api.Send;
            llm.Reset();
            server.CfgMan.SetCVar(ErrorgateCVars.GodEnabled, true);
            server.CfgMan.SetCVar(ErrorgateCVars.GodLlmApiUrl, "http://localhost:1/v1/chat/completions");
            server.CfgMan.SetCVar(ErrorgateCVars.GodLlmApiKey, "secret-key");
            server.CfgMan.SetCVar(ErrorgateCVars.GodLlmMinInterval, minInterval);
        });

        return (server, llm, api, pair);
    }

    private static async Task<string?> Ask(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, GodLlmSystem llm)
    {
        Task<string?> task = Task.FromResult<string?>(null);
        await server.WaitPost(() => task = llm.Complete(new[] { new LlmMessage("system", "you are the machine"), new LlmMessage("user", "events") }));
        return await task;
    }

    [Test]
    public async Task LlmReplyIsCleanedAndTheRequestCarriesTheKey()
    {
        var (server, llm, api, pair) = await LlmSetup();

        var answer = await Ask(server, llm);

        Assert.That(answer, Is.EqualTo("THE DOOR REMEMBERS."), "The reasoning block and the padding should be removed.");
        Assert.That(api.Requests, Has.Count.EqualTo(1));
        Assert.That(api.Requests[0].Headers.Authorization?.ToString(), Is.EqualTo("Bearer secret-key"));
        Assert.That(api.Bodies[0], Does.Contain("\"messages\""));
        Assert.That(api.Bodies[0], Does.Not.Contain("\"stop\""), "No stop sequences are sent unless configured.");
        Assert.That(api.Bodies[0], Does.Not.Contain("secret-key"), "The key travels in the header only.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LlmDoesNothingWhileSwitchedOff()
    {
        var (server, llm, api, pair) = await LlmSetup();
        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodEnabled, false));

        Assert.That(await Ask(server, llm), Is.Null);
        Assert.That(api.Requests, Is.Empty, "Nothing is sent while errorgate.god.enabled is off.");

        await pair.CleanReturnAsync();
    }

    [TestCase(HttpStatusCode.InternalServerError, "oops")]
    [TestCase(HttpStatusCode.OK, "not json at all")]
    [TestCase(HttpStatusCode.OK, "{\"choices\":[]}")]
    [TestCase(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"<think>only thoughts</think>\"}}]}")]
    public async Task LlmFailureGivesNullAndBacksOff(HttpStatusCode status, string body)
    {
        var (server, llm, api, pair) = await LlmSetup();
        api.Status = status;
        api.Body = body;

        Assert.That(await Ask(server, llm), Is.Null);
        Assert.That(api.Requests, Has.Count.EqualTo(1));

        // The endpoint is not hammered: the next try is refused without a request
        Assert.That(await Ask(server, llm), Is.Null);
        Assert.That(api.Requests, Has.Count.EqualTo(1), "A failed request should start a backoff.");

        // A new round clears it
        await server.WaitPost(() => llm.Reset());
        api.Status = HttpStatusCode.OK;
        api.Body = Reply;
        Assert.That(await Ask(server, llm), Is.EqualTo("THE DOOR REMEMBERS."));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LlmNetworkErrorGivesNull()
    {
        var (server, llm, api, pair) = await LlmSetup();
        api.Throw = true;

        Assert.That(await Ask(server, llm), Is.Null);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LlmRespectsTheMinimumInterval()
    {
        var (server, llm, api, pair) = await LlmSetup(minInterval: 1000f);

        Assert.That(await Ask(server, llm), Is.Not.Null);
        Assert.That(await Ask(server, llm), Is.Null, "A second request inside the interval should be refused.");
        Assert.That(api.Requests, Has.Count.EqualTo(1));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LlmRespectsTheCallCapPerRound()
    {
        var (server, llm, api, pair) = await LlmSetup();
        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodLlmMaxCallsPerRound, 2));

        Assert.That(await Ask(server, llm), Is.Not.Null);
        Assert.That(await Ask(server, llm), Is.Not.Null);
        Assert.That(await Ask(server, llm), Is.Null, "The third call is over the cap.");
        Assert.That(api.Requests, Has.Count.EqualTo(2));

        await server.WaitPost(() => llm.Reset());
        Assert.That(await Ask(server, llm), Is.Not.Null, "The cap starts over with a new round.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LlmRefusesAddressesThatAreNotHttp()
    {
        var (server, llm, api, pair) = await LlmSetup();
        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodLlmApiUrl, "file:///etc/passwd"));

        Assert.That(await Ask(server, llm), Is.Null);
        Assert.That(api.Requests, Is.Empty);

        await pair.CleanReturnAsync();
    }

    // Event buffer

    [Test]
    public void BufferKeepsTheNewestEvents()
    {
        var buffer = new GodEventBuffer { EventCapacity = 3 };
        for (var i = 0; i < 5; i++)
        {
            buffer.Add(new GodEvent(TimeSpan.FromSeconds(i), GodEventKind.World, GodEventSeverity.Low, $"event {i}", Array.Empty<string>()));
        }

        Assert.That(buffer.Events.Select(e => e.Text), Is.EqualTo(new[] { "event 2", "event 3", "event 4" }));
    }

    [Test]
    public void BufferSummarisesChatIntoOneEvent()
    {
        var buffer = new GodEventBuffer { ChatCapacity = 3 };
        buffer.AddChat("Ivan", "first, dropped", false);
        buffer.AddChat("Ivan", "hello there", false);
        buffer.AddChat("Olga", new string('x', 500), true);
        buffer.AddChat("Ivan", "   ", false);
        buffer.AddChat("Ivan", "last", false);

        Assert.That(buffer.ChatCount, Is.EqualTo(3), "Blank lines are ignored and the oldest line is dropped at capacity.");

        var summary = buffer.FlushChat(TimeSpan.FromSeconds(9));
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.Value.Kind, Is.EqualTo(GodEventKind.Chat));
        Assert.That(summary.Value.Subjects, Is.EquivalentTo(new[] { "Ivan", "Olga" }));
        Assert.That(summary.Value.Text, Does.Contain("Olga (whispering)"));
        Assert.That(summary.Value.Text, Does.Not.Contain(new string('x', GodEventBuffer.MaxChatLineLength + 10)), "Long lines are cut.");
        Assert.That(summary.Value.Text, Does.Not.Contain("first, dropped"));
        Assert.That(buffer.ChatCount, Is.EqualTo(0));
        Assert.That(buffer.FlushChat(TimeSpan.Zero), Is.Null, "Nothing is left to summarise.");
    }

    [Test]
    public void DrainEmptiesTheBufferAndAppendsTheChatSummary()
    {
        var buffer = new GodEventBuffer();
        buffer.Add(new GodEvent(TimeSpan.Zero, GodEventKind.Death, GodEventSeverity.High, "Ivan died.", new[] { "Ivan" }));
        buffer.AddChat("Olga", "no", false);

        var drained = buffer.Drain(TimeSpan.FromSeconds(5));

        Assert.That(drained.Select(e => e.Kind), Is.EqualTo(new[] { GodEventKind.Death, GodEventKind.Chat }));
        Assert.That(buffer.Events, Is.Empty);
        Assert.That(buffer.ChatCount, Is.EqualTo(0));
    }

    // Observer

    [Test]
    public async Task ObserverNotesPlayersAndNothingWhileOff()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var observer = entMan.System<GodObserverSystem>();
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();

        EntityUid victim = default;
        EntityUid attacker = default;
        await server.WaitAssertion(() =>
        {
            observer.Clear();
            entMan.System<MapSystem>().CreateMap(out var mapId);
            victim = entMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            attacker = entMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            entMan.System<MetaDataSystem>().SetEntityName(victim, "Ivan");
            entMan.System<MetaDataSystem>().SetEntityName(attacker, "Boris");

            var mind = entMan.System<SharedMindSystem>();
            mind.TransferTo(mind.CreateMind(session.UserId), victim);
            Assert.That(session.AttachedEntity, Is.EqualTo(victim), "The victim should be the player's body.");
            Assert.That(entMan.HasComponent<Robust.Shared.Player.ActorComponent>(victim), Is.True);
            server.CfgMan.SetCVar(ErrorgateCVars.GodEnabled, false);
        });

        var blunt = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(15));

        // Off: nothing is recorded
        await server.WaitPost(() =>
        {
            entMan.System<DamageableSystem>().TryChangeDamage(victim, blunt, origin: attacker);
            entMan.EventBus.RaiseLocalEvent(victim, new EntitySpokeEvent(victim, "hello", null, false, server.ProtoMan.Index<LanguagePrototype>("Universal")), true);
        });
        await pair.RunTicksSync(3);
        Assert.That(observer.Buffer.Events, Is.Empty);
        Assert.That(observer.Buffer.ChatCount, Is.EqualTo(0));

        // On: damage, speech and death are noted, with the attacker named
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(ErrorgateCVars.GodEnabled, true);
            entMan.System<DamageableSystem>().TryChangeDamage(victim, blunt, origin: attacker);
            entMan.EventBus.RaiseLocalEvent(victim, new EntitySpokeEvent(victim, "hello", null, false, server.ProtoMan.Index<LanguagePrototype>("Universal")), true);
            entMan.System<MobStateSystem>().ChangeMobState(victim, MobState.Dead);
        });
        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.CfgMan.GetCVar(ErrorgateCVars.GodEnabled), Is.True);
            var events = observer.Buffer.Events;
            Assert.That(events.Select(e => e.Kind), Does.Contain(GodEventKind.Combat), string.Join(" | ", events.Select(e => e.Text)));
            Assert.That(events.Select(e => e.Kind), Does.Contain(GodEventKind.Death), string.Join(" | ", events.Select(e => e.Text)));
            Assert.That(events.Single(e => e.Kind == GodEventKind.Combat).Text, Does.Contain("Ivan was hurt by Boris"));
            Assert.That(events.Single(e => e.Kind == GodEventKind.Death).Text, Is.EqualTo("Ivan died. Last harmed by Boris."));
            Assert.That(observer.Buffer.ChatCount, Is.EqualTo(1));

            // The attacker is no player, damage to or speech of mobs without a player is not noted
            var before = observer.Buffer.Events.Count;
            entMan.System<DamageableSystem>().TryChangeDamage(attacker, blunt, origin: victim);
            Assert.That(observer.Buffer.Events, Has.Count.EqualTo(before));
        });

        await pair.CleanReturnAsync();
    }
}
