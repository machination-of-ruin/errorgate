using Content.Shared._ERRORGATE.Reputation;
using Content.Shared.Mobs;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Reputation;

/// <summary>
///     The kill matrix: a player who deals the killing blow moves their standing with the factions. NPCs pay what their
///     <see cref="ReputationRewardComponent"/> says, players pay <see cref="PlayerKill"/>. Killing the same player again within
///     <see cref="PlayerCooldown"/> pays nothing (no farming a friend or an alt). No other cap or decay.
/// </summary>
public sealed class ReputationKillSystem : EntitySystem
{
    /// <summary>What killing a player does to the standings of the killer: faction to change.</summary>
    public static readonly (string Faction, int Delta)[] PlayerKill = [("Military", -25), ("Bandit", 25)];

    public static readonly TimeSpan PlayerCooldown = TimeSpan.FromHours(1);

    [Dependency] private readonly ReputationSystem _reputation = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<(NetUserId Killer, NetUserId Victim), TimeSpan> _lastKill = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);
    }

    private void OnMobState(MobStateChangedEvent args)
    {
        var uid = args.Target;
        if (args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead)
            return;

        if (args.Origin is not { } origin || origin == uid || !TryComp<ActorComponent>(origin, out var killer))
            return;

        var user = killer.PlayerSession.UserId;

        if (TryComp<ActorComponent>(uid, out var victim))
        {
            var now = _timing.CurTime;
            var key = (user, victim.PlayerSession.UserId);
            if (_lastKill.TryGetValue(key, out var last) && now - last < PlayerCooldown)
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
