using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     Keeps the <see cref="GodLedger"/>: a record per player of what they have done this round. It is bookkeeping only,
///     it does not decide anything. Death is the only thing counted as an error, everything else is just recorded.
/// </summary>
public sealed class GodLedgerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SectorSystem _sectors = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>How often subjects are looked at for their sector and who is near.</summary>
    private static readonly TimeSpan LookInterval = TimeSpan.FromSeconds(10);

    // Players this close count as being together
    private const float NearRange = 12f;

    // A kill is credited to the last player who hurt the victim within this long
    private static readonly TimeSpan BlameWindow = TimeSpan.FromSeconds(60);

    private const int MaxWordsLength = 80;

    public readonly GodLedger Ledger = new();

    private readonly Dictionary<EntityUid, (NetUserId Attacker, TimeSpan Time)> _hurtBy = new();

    // Whose body is whose, once seen. A body that is gibbed or deleted loses its mind link before every handler has run.
    private readonly Dictionary<EntityUid, NetUserId> _bodies = new();
    private TimeSpan _nextLook;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawned);
        // MetaData and not MobState: the engine allows one subscription per component and event, and the life log has MobState
        // Before the thresholds, like the life log: the hit that kills must be written down before the death is handled
        SubscribeLocalEvent<MetaDataComponent, DamageChangedEvent>(OnDamageChanged, before: new[] { typeof(MobThresholdSystem) });
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());
    }

    public void Clear()
    {
        Ledger.Clear();
        _hurtBy.Clear();
        _bodies.Clear();
    }

    /// <summary>
    ///     The subject whose character is this body, null for mobs and for bodies nobody plays.
    /// </summary>
    public bool TrySubject(EntityUid body, out Subject subject)
    {
        subject = null!;

        NetUserId user;
        if (TryComp<MindContainerComponent>(body, out var container)
            && container.Mind is { } mindId
            && TryComp<MindComponent>(mindId, out var mind)
            && mind.UserId is { } known)
        {
            user = known;
            _bodies[body] = user;
        }
        else if (!_bodies.TryGetValue(body, out user))
        {
            return false;
        }

        subject = Ledger.GetOrAdd(user);
        if (TryComp<MetaDataComponent>(body, out var meta))
            subject.Name = meta.EntityName;

        // First seen without a spawn (an admin gave them a body, the round was already running): a life has begun
        if (subject.Lives == 0)
        {
            subject.Lives = 1;
            subject.Alive = !TryComp<MobStateComponent>(body, out var state) || state.CurrentState != MobState.Dead;
            subject.LifeStart = _timing.CurTime;
        }

        return true;
    }

    /// <summary>
    ///     The living body a subject plays right now, with the player's session, when they are in the world and not in the void.
    /// </summary>
    public bool TryGetReachable(Subject subject, out EntityUid body, out ICommonSession session)
    {
        body = default;
        session = null!;

        if (!subject.Alive)
            return false;

        foreach (var (candidate, user) in _bodies)
        {
            if (user != subject.User || !Exists(candidate) || !TryComp<ActorComponent>(candidate, out var actor))
                continue;

            if (TryComp<MobStateComponent>(candidate, out var state) && state.CurrentState == MobState.Dead)
                continue;

            body = candidate;
            session = actor.PlayerSession;
            return true;
        }

        return false;
    }

    private void OnSpawned(PlayerSpawnCompleteEvent args)
    {
        var subject = Ledger.GetOrAdd(args.Player.UserId);
        _bodies[args.Mob] = args.Player.UserId;
        subject.Name = MetaData(args.Mob).EntityName;
        subject.Lives++;
        subject.Alive = true;
        subject.LifeStart = _timing.CurTime;
        Note(subject, subject.Lives == 1 ? "began the round" : "was instantiated again");
        Look(subject, args.Mob);
    }

    private void OnDamageChanged(Entity<MetaDataComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null || !HasComp<MobStateComponent>(ent))
            return;

        var total = (float) args.DamageDelta.GetTotal();
        if (total <= 0f)
            return;

        var now = _timing.CurTime;

        if (TrySubject(ent.Owner, out var victim))
            victim.DamageTaken += total;

        if (args.Origin is { } origin && origin != ent.Owner && Exists(origin) && TrySubject(origin, out var attacker))
        {
            attacker.DamageDealt += total;
            _hurtBy[ent.Owner] = (attacker.User, now);
        }
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var victimIsSubject = TrySubject(args.Target, out var victim);
        var name = TryComp<MetaDataComponent>(args.Target, out var targetMeta) ? targetMeta.EntityName : string.Empty;

        if (victimIsSubject)
        {
            victim.Errors++;
            victim.Alive = false;
            Note(victim, "died");
            RaiseLocalEvent(new GodSubjectDiedEvent(victim));
        }

        _bodies.Remove(args.Target);

        if (_hurtBy.Remove(args.Target, out var hit)
            && _timing.CurTime - hit.Time < BlameWindow
            && Ledger.TryGet(hit.Attacker, out var killer))
        {
            if (victimIsSubject)
                killer.KillsOfPlayers++;
            else
                killer.KillsOfMobs++;

            Note(killer, $"killed {name}");
        }
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        if (!TrySubject(args.Source, out var subject))
            return;

        subject.SpeechLines++;
        subject.LastWords = args.Message.Length > MaxWordsLength ? args.Message[..MaxWordsLength] + "..." : args.Message;
    }

    /// <summary>
    ///     Notes something a subject has just done or suffered.
    /// </summary>
    public void Note(Subject subject, string what)
    {
        subject.LastEvent = what;
        subject.LastEventTime = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextLook)
            return;

        _nextLook = now + LookInterval;

        // One pass over the minds' bodies, not one per subject
        var bodies = new Dictionary<NetUserId, EntityUid>();
        var minds = EntityQueryEnumerator<MindContainerComponent, MobStateComponent>();
        while (minds.MoveNext(out var uid, out var container, out _))
        {
            if (container.Mind is { } mindId && TryComp<MindComponent>(mindId, out var mind) && mind.UserId is { } user)
            {
                bodies[user] = uid;
                _bodies[uid] = user;
            }
        }

        var alive = new List<(Subject Subject, EntityUid Body)>();
        foreach (var subject in Ledger.All)
        {
            if (subject.Alive && bodies.TryGetValue(subject.User, out var body))
                alive.Add((subject, body));
        }

        foreach (var (subject, body) in alive)
        {
            Look(subject, body);
            subject.Near.Clear();

            var position = _transform.GetMapCoordinates(body);
            foreach (var (other, otherBody) in alive)
            {
                if (other == subject)
                    continue;

                var otherPosition = _transform.GetMapCoordinates(otherBody);
                if (otherPosition.MapId == position.MapId && (otherPosition.Position - position.Position).Length() <= NearRange)
                    subject.Near.Add(other.Number);
            }
        }
    }

    private void Look(Subject subject, EntityUid body)
    {
        var sector = _sectors.SectorOf(body);
        subject.Sector = sector;
        if (sector != null)
            subject.SectorsVisited.Add(sector);
    }
}

/// <summary>
///     Raised (broadcast) when a subject has died. The subject's <see cref="Subject.Near"/> still lists who was around.
/// </summary>
public sealed class GodSubjectDiedEvent : EntityEventArgs
{
    public readonly Subject Subject;

    public GodSubjectDiedEvent(Subject subject)
    {
        Subject = subject;
    }
}
