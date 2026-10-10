using Content.Server.Chat.Systems;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     Watches the round for MACHINATION OF RUIN and keeps what it saw in <see cref="Buffer"/>: deaths, damage taken,
///     arrivals and speech of players, plus whatever other systems report through <see cref="Record"/> (anomalies, storms,
///     airdrops). Nothing is recorded while <c>errorgate.god.enabled</c> is off. It only collects, deciding what to do
///     about it is the job of the gamerule that will read the buffer.
/// </summary>
public sealed class GodObserverSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>
    ///     Damage of one victim is noted at most this often, a firefight is one entry per few seconds, not one per bullet.
    /// </summary>
    private static readonly TimeSpan CombatThrottle = TimeSpan.FromSeconds(5);

    // Damage totals that make an entry more or less important
    private const float MediumDamage = 10f;
    private const float HighDamage = 30f;

    public readonly GodEventBuffer Buffer = new();

    private bool _enabled;
    private bool _monitorChat = true;

    // Who last hurt a player, so a death can name them. Cleared at the end of the round.
    private readonly Dictionary<EntityUid, (string Name, TimeSpan Time)> _lastHarmedBy = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastCombatNote = new();

    // Bodies players have been in this round. The death void takes the player out of a body as it dies, so by the time
    // the death is announced the body no longer has a player attached.
    private readonly HashSet<EntityUid> _playerBodies = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        // Raised on the victim, not broadcast: only players are of interest anyway
        SubscribeLocalEvent<ActorComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        SubscribeLocalEvent<EntitySpokeEvent>(OnEntitySpoke);
        SubscribeLocalEvent<ActorComponent, PlayerAttachedEvent>((uid, _, _) => _playerBodies.Add(uid));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());

        Subs.CVar(_cfg, ErrorgateCVars.GodEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodMonitorChat, v => _monitorChat = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodMaxEvents, v => Buffer.EventCapacity = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodChatBufferSize, v => Buffer.ChatCapacity = v, true);
    }

    /// <summary>
    ///     Notes something that happened. Other systems call this for what the world did.
    /// </summary>
    public void Record(GodEventKind kind, GodEventSeverity severity, string text, params string[] subjects)
    {
        if (!_enabled)
            return;

        Buffer.Add(new GodEvent(_timing.CurTime, kind, severity, text, subjects));
    }

    public void Clear()
    {
        Buffer.Clear();
        _lastHarmedBy.Clear();
        _lastCombatNote.Clear();
        _playerBodies.Clear();
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (!_enabled || args.NewMobState != MobState.Dead || !_playerBodies.Contains(args.Target) && !HasComp<ActorComponent>(args.Target))
            return;

        var name = Name(args.Target);
        var text = $"{name} died.";
        var subjects = new List<string> { name };

        if (_lastHarmedBy.TryGetValue(args.Target, out var harm) && _timing.CurTime - harm.Time < TimeSpan.FromSeconds(30))
        {
            text = $"{name} died. Last harmed by {harm.Name}.";
            subjects.Add(harm.Name);
        }

        Buffer.Add(new GodEvent(_timing.CurTime, GodEventKind.Death, GodEventSeverity.High, text, subjects));
        _lastHarmedBy.Remove(args.Target);
        _lastCombatNote.Remove(args.Target);
        _playerBodies.Remove(args.Target);
    }

    private void OnDamageChanged(Entity<ActorComponent> ent, ref DamageChangedEvent args)
    {
        if (!_enabled || !args.DamageIncreased || args.DamageDelta == null)
            return;

        var victim = ent.Owner;

        var now = _timing.CurTime;
        string? attackerName = null;

        if (args.Origin is { } origin && origin != victim && Exists(origin))
        {
            attackerName = Name(origin);
            _lastHarmedBy[victim] = (attackerName, now);
        }

        if (_lastCombatNote.TryGetValue(victim, out var last) && now - last < CombatThrottle)
            return;

        var total = (float) args.DamageDelta.GetTotal();
        if (total <= 0f)
            return;

        _lastCombatNote[victim] = now;

        var severity = total >= HighDamage ? GodEventSeverity.High : total >= MediumDamage ? GodEventSeverity.Medium : GodEventSeverity.Low;
        var name = Name(victim);
        var text = attackerName != null ? $"{name} was hurt by {attackerName} ({total:0} damage)." : $"{name} was hurt ({total:0} damage).";
        var subjects = attackerName != null ? new[] { name, attackerName } : new[] { name };

        Buffer.Add(new GodEvent(now, GodEventKind.Combat, severity, text, subjects));
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        if (!_enabled)
            return;

        var name = Name(args.Mob);
        var text = args.LateJoin ? $"{name} arrived." : $"{name} began the round.";
        Buffer.Add(new GodEvent(_timing.CurTime, GodEventKind.Arrival, GodEventSeverity.Low, text, new[] { name }));
    }

    private void OnEntitySpoke(EntitySpokeEvent args)
    {
        if (!_enabled || !_monitorChat || !HasComp<ActorComponent>(args.Source))
            return;

        Buffer.AddChat(Name(args.Source), args.Message, args.IsWhisper);
    }

    private string Name(EntityUid uid)
    {
        return MetaData(uid).EntityName;
    }
}
