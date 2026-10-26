using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>
///     Soldiers and bandits get up when they are lying and nothing keeps them down (a knockdown, a wound that took their legs), and pick up
///     the gun they dropped when they are standing again, in combat or not.
/// </summary>
public sealed class NpcStandSystem : EntitySystem
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NpcGunSystem _guns = default!;
    [Dependency] private readonly SharedLayingDownSystem _layingDown = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        var query = EntityQueryEnumerator<NpcNeedsComponent, ActiveNPCComponent, StandingStateComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var standing))
        {
            if (!_mobState.IsAlive(uid) || HasComp<KnockedDownComponent>(uid))
                continue;

            if (standing.CurrentState == StandingState.Lying)
                _layingDown.TryStandUp(uid, standingState: standing);
            else if (standing.CurrentState == StandingState.Standing)
                _guns.TryPickUpDropped(uid);
        }
    }
}
