#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server._ERRORGATE.AiGod;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.Mind;
using Robust.Client.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Every action of hers goes through one pipeline: checked, paid for, held for approval or logged as a dry run, then done.
/// </summary>
[TestFixture]
public sealed class GodDirectorTest
{
    private sealed class World
    {
        public Content.IntegrationTests.Pair.TestPair Pair = default!;
        public GodDirectorSystem Director = default!;
        public ChatUIController Chat = default!;
        public EntityUid Ivan;
        public int IvanNumber;
        public int BorisNumber;
    }

    /// <summary>
    ///     Ivan is the connected player. Boris is a second subject with no connection, so he cannot be reached.
    ///     Everything is switched on and loose: live, no approval, no quiet period, no cooldowns.
    /// </summary>
    private static async Task<World> Setup()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        var world = new World
        {
            Pair = pair,
            Director = entMan.System<GodDirectorSystem>(),
            Chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>(),
        };

        await pair.Client.WaitPost(() => world.Chat.History.Clear());

        await server.WaitAssertion(() =>
        {
            var cfg = server.CfgMan;
            cfg.SetCVar(ErrorgateCVars.GodEnabled, true);
            cfg.SetCVar(ErrorgateCVars.GodDryRun, false);
            cfg.SetCVar(ErrorgateCVars.GodApproval, false);
            cfg.SetCVar(ErrorgateCVars.GodQuietSeconds, 0f);
            cfg.SetCVar(ErrorgateCVars.GodMinPlayers, 1);
            cfg.SetCVar(ErrorgateCVars.GodTargetCooldown, 0f);
            cfg.SetCVar(ErrorgateCVars.GodAnnounceCooldown, 0f);
            cfg.SetCVar(ErrorgateCVars.GodAnnouncePerHour, 100);
            cfg.SetCVar(ErrorgateCVars.GodBudgetCap, 100f);
            cfg.SetCVar(ErrorgateCVars.GodBudgetPerMinute, 60f);
            cfg.SetCVar(ErrorgateCVars.GodApprovalTimeoutSeconds, 120);

            entMan.System<GodObserverSystem>().Clear();
            var ledgerSystem = entMan.System<GodLedgerSystem>();
            ledgerSystem.Clear();
            world.Director.Reset();
            entMan.System<GodScriptedSystem>().Reset();
            world.Director.Budget.Points = 100f;

            entMan.System<MapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var ivan = entMan.SpawnEntity("MobHuman", coords);
            var boris = entMan.SpawnEntity("MobHuman", coords);
            world.Ivan = ivan;
            entMan.System<MetaDataSystem>().SetEntityName(ivan, "Ivan Petrov");
            entMan.System<MetaDataSystem>().SetEntityName(boris, "Boris");

            var mind = entMan.System<SharedMindSystem>();
            mind.TransferTo(mind.CreateMind(session.UserId), ivan);

            var borisMind = mind.CreateMind(null);
            typeof(MindComponent).GetProperty(nameof(MindComponent.UserId))!.SetValue(entMan.GetComponent<MindComponent>(borisMind), (NetUserId?) new NetUserId(Guid.NewGuid()));
            mind.TransferTo(borisMind, boris);

            Assert.That(ledgerSystem.TrySubject(ivan, out var ivanSubject), Is.True);
            Assert.That(ledgerSystem.TrySubject(boris, out var borisSubject), Is.True);
            world.IvanNumber = ivanSubject.Number;
            world.BorisNumber = borisSubject.Number;
        });

        return world;
    }

    private static GodAction Subtle(string text, params int[] targets)
    {
        return new GodAction { Type = GodActionType.Subtle, Targets = targets.ToList(), Text = text };
    }

    private static async Task<bool> ClientSaw(World world, string text)
    {
        await world.Pair.RunTicksSync(5);

        var seen = false;
        await world.Pair.Client.WaitPost(() => seen = world.Chat.History.Any(m => m.Msg.Message == text));
        return seen;
    }

    private static async Task<GodDecision> Submit(World world, GodAction action, GodSource source = GodSource.Scripted)
    {
        GodDecision? decision = null;
        await world.Pair.Server.WaitPost(() => decision = world.Director.Submit(action, source));
        return decision!;
    }

    [Test]
    public async Task ASubtleMessageReachesItsSubjectInCapitals()
    {
        var world = await Setup();

        var decision = await Submit(world, Subtle("Subject logged. Error counted.", world.IvanNumber));

        Assert.That(decision.Status, Is.EqualTo(GodActionStatus.Executed), decision.ToString());
        Assert.That(await ClientSaw(world, "SUBJECT LOGGED. ERROR COUNTED."), Is.True, "The player should have the message, in capitals.");

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnAnnouncementReachesEveryone()
    {
        var world = await Setup();

        var decision = await Submit(world, new GodAction { Type = GodActionType.Announce, Text = "One less." });

        Assert.That(decision.Status, Is.EqualTo(GodActionStatus.Executed), decision.ToString());
        Assert.That(await ClientSaw(world, "ONE LESS."), Is.True);

        var wrapped = "";
        await world.Pair.Client.WaitPost(() => wrapped = world.Chat.History.Last(m => m.Msg.Message == "ONE LESS.").Msg.WrappedMessage);
        Assert.That(wrapped, Does.Contain("MACHINATION OF RUIN"));

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task AGlitchIsDoneWithoutText()
    {
        var world = await Setup();

        var decision = await Submit(world, new GodAction { Type = GodActionType.Glitch, Targets = { world.IvanNumber } });

        Assert.That(decision.Status, Is.EqualTo(GodActionStatus.Executed), decision.ToString());
        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADryRunDecidesButDoesNothing()
    {
        var world = await Setup();
        await world.Pair.Server.WaitPost(() => world.Pair.Server.CfgMan.SetCVar(ErrorgateCVars.GodDryRun, true));
        var before = 0f;
        await world.Pair.Server.WaitPost(() => before = world.Director.Budget.Points);

        var decision = await Submit(world, Subtle("Subject logged.", world.IvanNumber));

        Assert.That(decision.Status, Is.EqualTo(GodActionStatus.DryRun));
        Assert.That(await ClientSaw(world, "SUBJECT LOGGED."), Is.False, "Nothing reaches a player in a dry run.");
        Assert.That(world.Director.Budget.Points, Is.LessThan(before), "A dry run still spends points, so the pacing can be judged.");

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task ApprovalHoldsAnActionUntilAnAdminAnswers()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodApproval, true));

        var first = await Submit(world, Subtle("First.", world.IvanNumber));
        var second = await Submit(world, Subtle("Second.", world.IvanNumber));
        Assert.That(first.Status, Is.EqualTo(GodActionStatus.Pending));
        Assert.That(await ClientSaw(world, "FIRST."), Is.False, "Nothing happens while it waits.");

        string message = "";
        var approved = false;
        await server.WaitPost(() => approved = world.Director.Approve(first.Id, out message));
        Assert.That(approved, Is.True, message);
        Assert.That(first.Status, Is.EqualTo(GodActionStatus.Executed));
        Assert.That(await ClientSaw(world, "FIRST."), Is.True);

        var denied = false;
        await server.WaitPost(() => denied = world.Director.Deny(second.Id, out message));
        Assert.That(denied, Is.True);
        Assert.That(second.Status, Is.EqualTo(GodActionStatus.Denied));
        Assert.That(await ClientSaw(world, "SECOND."), Is.False);

        // A decision is answered once
        var again = true;
        await server.WaitPost(() => again = world.Director.Approve(first.Id, out message));
        Assert.That(again, Is.False);

        // Godforce by an admin needs no approval
        var forced = await Submit(world, Subtle("Forced.", world.IvanNumber), GodSource.Admin);
        Assert.That(forced.Status, Is.EqualTo(GodActionStatus.Executed));

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryDecisionThatWaitsIsAnnouncedToTheAdmins()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var announced = new List<GodDecision>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(ErrorgateCVars.GodApproval, true);
            world.Director.DecisionPending += announced.Add;
        });

        var held = await Submit(world, Subtle("First.", world.IvanNumber));
        var refused = await Submit(world, Subtle("Hello.", 99));
        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodApproval, false));
        var done = await Submit(world, Subtle("Direct.", world.IvanNumber));

        Assert.That(announced, Is.EqualTo(new[] { held }), "Only a decision that waits is announced, not a refused or an executed one.");
        Assert.That(refused.Status, Is.EqualTo(GodActionStatus.Rejected));
        Assert.That(done.Status, Is.EqualTo(GodActionStatus.Executed));

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryActionDoneIsAnnouncedToTheAdminsAndItShowsOnceToThePlayer()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var done = new List<GodDecision>();
        await server.WaitPost(() => world.Director.DecisionDone += done.Add);

        var subtle = await Submit(world, Subtle("Logged.", world.IvanNumber));
        var glitch = await Submit(world, new GodAction { Type = GodActionType.Glitch, Targets = { world.IvanNumber } });
        var refused = await Submit(world, Subtle("Hello.", 99));
        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodDryRun, true));
        var dry = await Submit(world, Subtle("Dry.", world.IvanNumber));

        Assert.That(done, Is.EqualTo(new[] { subtle, glitch, dry }), "Each action done, and each dry run, but no refusal.");
        Assert.That(refused.Status, Is.EqualTo(GodActionStatus.Rejected));

        // One line in the chat, not the line plus a popup copy of it
        await world.Pair.RunTicksSync(5);
        var lines = 0;
        await world.Pair.Client.WaitPost(() => lines = world.Chat.History.Count(m => m.Msg.Message == "LOGGED."));
        Assert.That(lines, Is.EqualTo(1));

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnansweredApprovalsExpireAsDenied()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(ErrorgateCVars.GodApproval, true);
            server.CfgMan.SetCVar(ErrorgateCVars.GodApprovalTimeoutSeconds, 1);
        });

        var decision = await Submit(world, Subtle("Late.", world.IvanNumber));
        Assert.That(decision.Status, Is.EqualTo(GodActionStatus.Pending));

        await world.Pair.RunSeconds(2);
        Assert.That(decision.Status, Is.EqualTo(GodActionStatus.Expired));

        var answered = true;
        string message = "";
        await server.WaitPost(() => answered = world.Director.Approve(decision.Id, out message));
        Assert.That(answered, Is.False, "Too late.");
        Assert.That(await ClientSaw(world, "LATE."), Is.False);

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task RefusalsSayWhy()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

        async Task<GodDecision> Refused(GodAction action, string reason, GodSource source = GodSource.Scripted)
        {
            var decision = await Submit(world, action, source);
            Assert.That(decision.Status, Is.EqualTo(GodActionStatus.Rejected), decision.ToString());
            Assert.That(decision.Reason, Does.Contain(reason), decision.ToString());
            return decision;
        }

        // Nobody by that number, somebody who cannot be reached, an order
        await Refused(Subtle("Hello.", 99), "no subject S99");
        await Refused(Subtle("Hello.", world.BorisNumber), "cannot be reached");
        await Refused(Subtle("You must go to the factory.", world.IvanNumber), "order");
        await Refused(Subtle("Hello.", world.IvanNumber, world.BorisNumber), "cannot be reached");

        // No points
        await server.WaitPost(() => world.Director.Budget.Points = 0f);
        await Refused(Subtle("Hello.", world.IvanNumber), "not enough points");
        await Refused(Subtle("Hello.", world.IvanNumber), "not enough points");

        // An admin pays no points
        var forced = await Submit(world, Subtle("Hello.", world.IvanNumber), GodSource.Admin);
        Assert.That(forced.Status, Is.EqualTo(GodActionStatus.Executed));

        // Cooldown on the same subject
        await server.WaitPost(() =>
        {
            world.Director.Budget.Points = 100f;
            server.CfgMan.SetCVar(ErrorgateCVars.GodTargetCooldown, 60f);
            world.Director.Budget.Reset(timing.CurTime);
        });
        var ok = await Submit(world, Subtle("One.", world.IvanNumber));
        Assert.That(ok.Status, Is.EqualTo(GodActionStatus.Executed), ok.ToString());
        await Refused(Subtle("Two.", world.IvanNumber), "was addressed");

        // Paused, quiet, switched off
        await server.WaitPost(() => world.Director.Paused = true);
        await Refused(Subtle("Hello.", world.IvanNumber), "paused");
        await server.WaitPost(() => world.Director.Paused = false);

        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodQuietSeconds, 100000f));
        await Refused(Subtle("Hello.", world.IvanNumber), "quiet period");
        var admin = await Submit(world, Subtle("Hello.", world.IvanNumber), GodSource.Admin);
        Assert.That(admin.Status, Is.EqualTo(GodActionStatus.Executed), "An admin is not held back by the quiet period.");
        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodQuietSeconds, 0f));

        await server.WaitPost(() => server.CfgMan.SetCVar(ErrorgateCVars.GodEnabled, false));
        await Refused(Subtle("Hello.", world.IvanNumber), "switched off", GodSource.Admin);

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheLogAndTheDigestShowWhatSheDid()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var digest = server.EntMan.System<GodDigestSystem>();

        await Submit(world, Subtle("Subject logged.", world.IvanNumber));
        await Submit(world, Subtle("You must run.", world.IvanNumber));

        await server.WaitAssertion(() =>
        {
            Assert.That(world.Director.Log, Has.Count.EqualTo(2));
            Assert.That(world.Director.Log.Select(d => d.Status), Is.EqualTo(new[] { GodActionStatus.Executed, GodActionStatus.Rejected }));

            var text = digest.Preview().Text;
            Assert.That(text, Does.Contain("[HER RECENT]"));
            Assert.That(text, Does.Contain($"subtle S{world.IvanNumber}: SUBJECT LOGGED.").Or.Contain($"subtle S{world.IvanNumber}: Subject logged."));
            Assert.That(text, Does.Not.Contain("You must run"), "A rejected line was never said, so it is not remembered.");
            Assert.That(text, Does.Contain("[STATE] budget"));
        });

        await world.Pair.CleanReturnAsync();
    }
}
