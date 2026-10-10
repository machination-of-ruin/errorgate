#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._ERRORGATE.Anomalies;
using Content.Server._ERRORGATE.CombatLogs;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Collects the messages <see cref="CombatLogsSystem"/> sends so tests can read them.
/// </summary>
public sealed class TestCombatLogListenerSystem : EntitySystem
{
    public readonly List<string> Messages = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CombatLogSentEvent>(OnSent);
    }

    private void OnSent(ref CombatLogSentEvent args)
    {
        Messages.Add(args.Message);
    }
}

[TestFixture]
public sealed class CombatLogsTest
{
    private static async Task<(TestPair Pair, EntityUid Victim, EntityUid Attacker, TestCombatLogListenerSystem Log)> Setup()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();

        EntityUid victim = default;
        EntityUid attacker = default;
        await server.WaitAssertion(() =>
        {
            var map = entMan.System<MapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            victim = entMan.SpawnEntity("MobHuman", coords);
            attacker = entMan.SpawnEntity("MobHuman", coords);

            var mind = entMan.System<SharedMindSystem>();
            var mindId = mind.CreateMind(session.UserId);
            mind.TransferTo(mindId, victim);
            Assert.That(session.AttachedEntity, Is.EqualTo(victim));
        });

        var log = server.EntMan.System<TestCombatLogListenerSystem>();
        log.Messages.Clear();
        return (pair, victim, attacker, log);
    }

    private static List<string> Combat(TestCombatLogListenerSystem log) =>
        log.Messages.Where(m => !m.Contains("bleeding")).ToList();

    private static async Task Hit(TestPair pair, EntityUid victim, EntityUid attacker, TargetBodyPart part, int amount)
    {
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.EntMan;
            var blunt = new DamageSpecifier(pair.Server.ProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(amount));

            // The same two things a melee attack does: announce the attack, then deal the damage.
            entMan.EventBus.RaiseLocalEvent(victim, new AttackedEvent(attacker, attacker, new EntityCoordinates(victim, default)));
            entMan.System<DamageableSystem>().TryChangeDamage(victim, blunt, origin: attacker, targetPart: part);
        });
        await pair.RunTicksSync(3);
    }

    [Test]
    public async Task MeleeHitNamesTheAttackerAndTheBodyPart()
    {
        var (pair, victim, attacker, log) = await Setup();

        await Hit(pair, victim, attacker, TargetBodyPart.LeftArm, 10);

        var messages = Combat(log);
        Assert.That(messages, Has.Count.EqualTo(1), string.Join(" | ", log.Messages));
        Assert.That(messages[0], Does.Contain("punches you"));
        Assert.That(messages[0], Does.Contain("left arm"));

        await pair.CleanReturnAsync();
    }

    [TestCase("ErrorgateAnomalyHeat", "THE HEAT FAULT BURNS YOU!")]
    [TestCase("ErrorgateAnomalyArc", "AN ARC FAULT TEARS THROUGH YOU!")]
    [TestCase("ErrorgateAnomalyCollapse", "THE COLLAPSE FAULT CRUSHES YOU!")]
    public async Task WorldFaultsHaveTheirOwnLine(string proto, string expected)
    {
        var (pair, victim, bystander, log) = await Setup();

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.EntMan;

            var fault = entMan.SpawnEntity(proto, entMan.GetComponent<TransformComponent>(victim).Coordinates);

            // Make the shock certain, the real chance is below one
            if (entMan.TryGetComponent(fault, out ArcFaultComponent? arc))
            {
                arc.InnerShockChance = 1f;

                // The arc picks one target, make it the one with the log
                entMan.DeleteEntity(bystander);
            }
        });
        await pair.RunSeconds(1.5f);

        Assert.That(log.Messages, Has.Some.Contains(expected), string.Join(" | ", log.Messages));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WeaponsUseTheirOwnVerb()
    {
        var (pair, victim, attacker, log) = await Setup();

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.EntMan;
            var knife = entMan.SpawnEntity("CombatKnife", entMan.GetComponent<TransformComponent>(attacker).Coordinates);
            var blunt = new DamageSpecifier(pair.Server.ProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(10));
            entMan.EventBus.RaiseLocalEvent(victim, new AttackedEvent(knife, attacker, new EntityCoordinates(victim, default)));
            entMan.System<DamageableSystem>().TryChangeDamage(victim, blunt, origin: attacker, targetPart: TargetBodyPart.Head);
        });
        await pair.RunTicksSync(3);

        var messages = Combat(log);
        Assert.That(messages, Has.Count.EqualTo(1), string.Join(" | ", log.Messages));
        Assert.That(messages[0], Does.Contain("stabs you in the head with the"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BlindVictimDoesNotSeeTheirAttacker()
    {
        var (pair, victim, attacker, log) = await Setup();

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.EntMan;
            entMan.System<Content.Shared.Eye.Blinding.Systems.BlindableSystem>().AdjustEyeDamage(victim, 100);
        });

        await Hit(pair, victim, attacker, TargetBodyPart.Head, 10);

        var messages = Combat(log);
        Assert.That(messages, Has.Count.EqualTo(1), string.Join(" | ", log.Messages));
        Assert.That(messages[0], Does.StartWith("Something punches you"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DamageWithoutContextFromNobodyIsSilent()
    {
        var (pair, victim, _, log) = await Setup();

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.EntMan;
            var blunt = new DamageSpecifier(pair.Server.ProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(5));
            entMan.System<DamageableSystem>().TryChangeDamage(victim, blunt);
        });
        await pair.RunTicksSync(3);

        Assert.That(Combat(log), Is.Empty);

        await pair.CleanReturnAsync();
    }
}
