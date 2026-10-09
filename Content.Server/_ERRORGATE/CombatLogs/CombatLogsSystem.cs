using Content.Server.Body.Components;
using Content.Server.Chat.Managers;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Player;

namespace Content.Server._ERRORGATE.CombatLogs;

/// <summary>
///     Tells players what is happening to their character in chat: who hit them, with what, where, and how badly.
///     Attackers are only named if the victim could actually see them, and are otherwise "Someone".
/// </summary>
/// <remarks>
///     Context events (melee, projectile, beam, thrown, shove) and damage events can arrive in any order inside a tick,
///     so everything is collected into a <see cref="PendingHit"/> per victim and turned into one message in <see cref="Update"/>.
/// </remarks>
public sealed class CombatLogsSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;

    private const float VisibleRange = 25f;
    private const float BleedCheckInterval = 0.5f;

    private readonly Dictionary<EntityUid, PendingHit> _pending = new();
    private readonly HashSet<EntityUid> _bleeding = new();
    private float _bleedTimer;

    public override void Initialize()
    {
        base.Initialize();

        // A (component, event) pair can only have one subscriber in the engine, so everything hangs off our own component.
        SubscribeLocalEvent<CombatLogsComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<CombatLogsComponent, ThrowHitByEvent>(OnThrownHit);
        SubscribeLocalEvent<CombatLogsComponent, DisarmedEvent>(OnDisarmed);
        SubscribeLocalEvent<CombatLogsComponent, HitScanReflectAttemptEvent>(OnHitscan, after: new[] { typeof(ReflectSystem) });
        SubscribeLocalEvent<CombatLogsComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
        SubscribeLocalEvent<CombatLogsComponent, DamageChangedEvent>(OnDamageChanged);

        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    #region Context

    private PendingHit Pending(EntityUid victim) =>
        _pending.TryGetValue(victim, out var hit) ? hit : _pending[victim] = new PendingHit();

    private void OnAttacked(Entity<CombatLogsComponent> victim, ref AttackedEvent args)
    {
        var hit = Pending(victim);
        hit.SetContext(HitKind.Melee, args.User, args.Used == args.User ? null : args.Used);
    }

    private void OnThrownHit(Entity<CombatLogsComponent> victim, ref ThrowHitByEvent args)
    {
        var hit = Pending(victim);
        hit.SetContext(HitKind.Thrown, args.User, args.Thrown);
        hit.Part ??= args.TargetPart;
    }

    private void OnDisarmed(Entity<CombatLogsComponent> victim, ref DisarmedEvent args)
    {
        Pending(victim).SetContext(HitKind.Shove, args.Source, null);
    }

    private void OnProjectileHit(Entity<ProjectileComponent> projectile, ref ProjectileHitEvent args)
    {
        if (!HasComp<CombatLogsComponent>(args.Target))
            return;

        Pending(args.Target).SetContext(HitKind.Projectile, args.Shooter, projectile);
    }

    private void OnHitscan(Entity<CombatLogsComponent> victim, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected)
        {
            Send(victim, Loc.GetString("errorgate-combat-log-beam-reflected"), 0.3f);
            return;
        }

        Pending(victim).SetContext(HitKind.Beam, args.Shooter, null);
    }

    #endregion

    #region Damage

    private void OnBeforeDamage(Entity<CombatLogsComponent> victim, ref BeforeDamageChangedEvent args)
    {
        // Some damage names the exact part it is aimed at.
        if (args.TargetPart is { } part && !args.Cancelled)
            Pending(victim).Part = part;
    }

    private void OnDamageChanged(Entity<CombatLogsComponent> victim, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        var total = FixedPoint2.Zero;
        foreach (var value in args.DamageDelta.DamageDict.Values)
        {
            if (value > 0)
                total += value;
        }

        if (total <= 0)
            return;

        var hit = Pending(victim);
        hit.Damage += (float) total;

        // Damage that nothing above explained, but that someone caused.
        if (hit.Kind == HitKind.None && args.Origin is { } origin)
            hit.SetContext(HitKind.Generic, origin, null);

        // The body system hits the part the attacker is aiming at, so that is where it landed.
        if (hit.Part == null && args.Origin is { } aimer && TryComp<TargetingComponent>(aimer, out var aim))
            hit.Part = aim.Target;
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count > 0)
        {
            foreach (var (victim, hit) in _pending)
            {
                if (!TerminatingOrDeleted(victim))
                    Flush(victim, hit);
            }

            _pending.Clear();
        }

        _bleedTimer += frameTime;
        if (_bleedTimer >= BleedCheckInterval)
        {
            _bleedTimer = 0;
            CheckBleeding();
        }
    }

    private void Flush(EntityUid victim, PendingHit hit)
    {
        if (!HasComp<ActorComponent>(victim) || _mobState.IsDead(victim))
            return;

        // Environmental damage (fire, pressure, ...) is not a combat message.
        if (hit.Kind == HitKind.None)
            return;

        // A shove does not have to hurt, everything else needs damage to be worth a message.
        if (hit.Damage <= 0 && hit.Kind != HitKind.Shove)
            return;

        var where = WherePhrase(hit.Part);
        var attacker = AttackerName(victim, hit.Attacker, hit.Kind is HitKind.Melee or HitKind.Shove ? null : VisibleRange);
        var self = hit.Attacker == victim;
        var weapon = hit.Weapon is { } w && !TerminatingOrDeleted(w) ? Name(w) : null;

        string message;
        switch (hit.Kind)
        {
            case HitKind.Melee when weapon == null:
                var (unarmedRoot, unarmedPresent) = MeleeVerbs(hit.Attacker);
                message = self
                    ? Loc.GetString("errorgate-combat-log-melee-unarmed-self", ("verb", unarmedRoot), ("where", where))
                    : Loc.GetString("errorgate-combat-log-melee-unarmed", ("attacker", attacker ?? Someone()), ("verb", unarmedPresent), ("where", where));
                break;
            case HitKind.Melee:
                var (root, present) = MeleeVerbs(hit.Weapon);
                message = self
                    ? Loc.GetString("errorgate-combat-log-melee-self", ("verb", root), ("where", where), ("weapon", weapon!))
                    : Loc.GetString("errorgate-combat-log-melee", ("attacker", attacker ?? Someone()), ("verb", present), ("where", where), ("weapon", weapon!));
                break;
            case HitKind.Projectile:
                message = attacker != null
                    ? Loc.GetString("errorgate-combat-log-shot", ("attacker", attacker), ("where", where), ("weapon", weapon ?? "projectile"))
                    : Loc.GetString("errorgate-combat-log-projectile", ("where", where), ("weapon", weapon ?? "projectile"));
                break;
            case HitKind.Beam:
                message = attacker != null
                    ? Loc.GetString("errorgate-combat-log-beam-known", ("attacker", attacker), ("where", where))
                    : Loc.GetString("errorgate-combat-log-beam", ("where", where));
                break;
            case HitKind.Thrown:
                message = attacker != null
                    ? Loc.GetString("errorgate-combat-log-thrown-known", ("attacker", attacker), ("where", where), ("weapon", weapon ?? "object"))
                    : Loc.GetString("errorgate-combat-log-thrown", ("where", where), ("weapon", weapon ?? "object"));
                break;
            case HitKind.Shove:
                message = Loc.GetString("errorgate-combat-log-shove", ("attacker", attacker ?? Someone()));
                break;
            default:
                message = attacker != null && !self
                    ? Loc.GetString("errorgate-combat-log-hurt-known", ("attacker", attacker), ("where", where))
                    : Loc.GetString("errorgate-combat-log-hurt", ("where", where));
                break;
        }

        Send(victim, message, Intensity(victim, hit.Damage));
    }

    /// <summary>
    ///     Who the victim sees attacking them, or null if they cannot tell. Melee attackers are always adjacent so the
    ///     range check is skipped, but being blind or unconscious still hides them.
    /// </summary>
    private string? AttackerName(EntityUid victim, EntityUid? attacker, float? range)
    {
        if (attacker is not { } who || TerminatingOrDeleted(who))
            return null;

        if (who == victim)
            return Name(who);

        if (!_mobState.IsAlive(victim))
            return null;

        if (TryComp<BlindableComponent>(victim, out var blindable) && blindable.IsBlind)
            return null;

        if (range != null && !_examine.InRangeUnOccluded(victim, who, range.Value))
            return null;

        return Identity.Name(who, EntityManager, victim);
    }

    private string WherePhrase(TargetBodyPart? part)
    {
        if (part is not { } target)
            return string.Empty;

        var id = "errorgate-combat-log-part-" + target.ToString().ToLowerInvariant();
        return Loc.TryGetString(id, out var text)
            ? " " + Loc.GetString("errorgate-combat-log-where", ("part", text))
            : string.Empty;
    }

    /// <summary>
    ///     0..1, how big this hit is. A quarter of what it takes to kill the victim is already the maximum.
    /// </summary>
    private float Intensity(EntityUid victim, float damage)
    {
        var dead = _thresholds.TryGetThresholdForState(victim, MobState.Dead, out var threshold)
            ? (float) threshold.Value
            : 100f;

        return Math.Clamp(damage / (dead * 0.25f), 0f, 1f);
    }

    private void Send(EntityUid victim, string message, float intensity, Color? fixedColor = null)
    {
        if (!TryComp<ActorComponent>(victim, out var actor))
            return;

        var size = (int) float.Lerp(11, 19, intensity);
        // hits go from orange to red with their size, bleeding is always red
        var color = fixedColor ?? Color.InterpolateBetween(Color.Orange, Color.Red, intensity);

        _chat.ChatMessageToOne(ChatChannel.Local,
            message,
            Loc.GetString("errorgate-combat-log-wrap", ("size", size), ("message", message)),
            EntityUid.Invalid,
            false,
            actor.PlayerSession.Channel,
            color);

        var ev = new CombatLogSentEvent(victim, message, intensity);
        RaiseLocalEvent(victim, ref ev, true);
    }

    private void CheckBleeding()
    {
        var query = EntityQueryEnumerator<ActorComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out _, out var blood))
        {
            var dead = _mobState.IsDead(uid);
            var bleeding = blood.BleedAmount > 0 && !dead;

            if (bleeding && _bleeding.Add(uid))
                Send(uid, Loc.GetString("errorgate-combat-log-bleeding-start"), 0.15f, Color.Red);
            else if (!bleeding && _bleeding.Remove(uid) && !dead)
                Send(uid, Loc.GetString("errorgate-combat-log-bleeding-stop"), 0.1f, Color.Red);
        }
    }

    /// <summary>
    ///     The verbs the melee weapon (or the bare hands of the attacker) uses in the log: "stab" / "stabs".
    /// </summary>
    private (string Root, string Present) MeleeVerbs(EntityUid? weapon)
    {
        if (weapon is { } uid && TryComp<MeleeWeaponComponent>(uid, out var melee))
            return (melee.ChatLogVerbRoot, melee.ChatLogVerbPresent);

        return ("hit", "hits");
    }

    private string Name(EntityUid uid) => MetaData(uid).EntityName;

    private string Someone() => Loc.GetString("errorgate-combat-log-someone");

    private enum HitKind
    {
        None,
        Generic,
        Melee,
        Projectile,
        Beam,
        Thrown,
        Shove,
    }

    private sealed class PendingHit
    {
        public HitKind Kind;
        public EntityUid? Attacker;
        public EntityUid? Weapon;
        public float Damage;
        public TargetBodyPart? Part;

        public void SetContext(HitKind kind, EntityUid? attacker, EntityUid? weapon)
        {
            // The first specific context wins, e.g. a thrown item is also an attack.
            if (Kind > HitKind.Generic)
                return;

            Kind = kind;
            Attacker = attacker;
            Weapon = weapon;
        }
    }
}

/// <summary>
///     Raised on a player's entity after a combat log message was sent to them.
///     Other systems (for example the future AI GOD) can use it to follow what happens to players.
/// </summary>
[ByRefEvent]
public readonly record struct CombatLogSentEvent(EntityUid Victim, string Message, float Intensity);
