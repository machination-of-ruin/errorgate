using System.Linq;
using Content.Shared._ERRORGATE.Reputation;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Reputation;

/// <summary>
///     The kill matrix: a player who kills moves their standing with the factions. NPCs pay what their
///     <see cref="ReputationRewardComponent"/> says, players pay <see cref="PlayerKill"/>. The kill belongs to the player who dealt
///     the last blow; a victim that dies of something nobody dealt (blood loss, suffocation, fire) belongs to the last player who hurt
///     it within <see cref="BlameWindow"/>. Killing the same player again within <see cref="PlayerCooldown"/> pays nothing (no farming
///     a friend or an alt). No other cap or decay.
/// </summary>
public sealed class ReputationKillSystem : EntitySystem
{
    /// <summary>What killing a player does to the standings of the killer: faction to change.</summary>
    public static readonly (string Faction, int Delta)[] PlayerKill = [("Military", -25), ("Bandit", 25)];

    public static readonly TimeSpan PlayerCooldown = TimeSpan.FromHours(1);

    /// <summary>How long after a player's hit a death without a killer still counts as their kill.</summary>
    public static readonly TimeSpan BlameWindow = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(1);

    [Dependency] private readonly ReputationSystem _reputation = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<(NetUserId Killer, NetUserId Victim), TimeSpan> _lastKill = new();
    private readonly Dictionary<EntityUid, (NetUserId User, TimeSpan Time)> _hurtBy = new();
    private TimeSpan _nextPrune;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<ReputationRewardComponent, DamageChangedEvent>(OnNpcDamaged);
        SubscribeLocalEvent<ActorComponent, DamageChangedEvent>(OnPlayerDamaged);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextPrune)
            return;

        _nextPrune = now + PruneInterval;
        foreach (var key in _lastKill.Where(k => now - k.Value >= PlayerCooldown).Select(k => k.Key).ToArray())
            _lastKill.Remove(key);

        foreach (var key in _hurtBy.Where(h => now - h.Value.Time >= BlameWindow).Select(h => h.Key).ToArray())
            _hurtBy.Remove(key);
    }

    private void OnNpcDamaged(EntityUid uid, ReputationRewardComponent comp, DamageChangedEvent args) => Remember(uid, args);

    private void OnPlayerDamaged(EntityUid uid, ActorComponent comp, DamageChangedEvent args) => Remember(uid, args);

    private void Remember(EntityUid victim, DamageChangedEvent args)
    {
        if (args.DamageIncreased && args.Origin is { } origin && origin != victim && TryComp<ActorComponent>(origin, out var attacker))
            _hurtBy[victim] = (attacker.PlayerSession.UserId, _timing.CurTime);
    }

    private void OnMobState(MobStateChangedEvent args)
    {
        var uid = args.Target;
        if (args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead)
            return;

        var blamed = _hurtBy.Remove(uid, out var hit) && _timing.CurTime - hit.Time < BlameWindow;

        NetUserId user;
        if (args.Origin is { } origin)
        {
            // Somebody dealt the last blow: theirs, or nobody's (an NPC, the victim itself)
            if (origin == uid || !TryComp<ActorComponent>(origin, out var killer))
                return;

            user = killer.PlayerSession.UserId;
        }
        else if (blamed)
        {
            user = hit.User;
        }
        else
        {
            return;
        }

        if (TryComp<ActorComponent>(uid, out var victim))
        {
            var now = _timing.CurTime;
            var key = (user, victim.PlayerSession.UserId);
            if (key.Item1 == key.Item2 || _lastKill.TryGetValue(key, out var last) && now - last < PlayerCooldown)
                return;

            _lastKill[key] = now;
            foreach (var (faction, delta) in PlayerKill)
                _reputation.Adjust(user, faction, delta);

            return;
        }

        if (!TryComp<ReputationRewardComponent>(uid, out var reward) || reward.Paid)
            return;

        reward.Paid = true;
        foreach (var (faction, delta) in reward.Standing)
            _reputation.Adjust(user, faction, delta);
    }
}
