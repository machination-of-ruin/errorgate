using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Mobs;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Despawn;

public sealed class DespawnDeadBodySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly BodySystem _bodySystem = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DespawnDeadBodyComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(EntityUid uid, DespawnDeadBodyComponent component, MobStateChangedEvent args)
    {
        // Revived mobs are safe again
        component.DespawnAt = args.NewMobState == MobState.Dead
            ? _timing.CurTime + TimeSpan.FromSeconds(component.DespawnAfterSeconds)
            : null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        if (curTime < _nextCheck)
            return;

        _nextCheck = curTime + CheckInterval;

        var query = EntityQueryEnumerator<DespawnDeadBodyComponent>();
        while (query.MoveNext(out var uid, out var despawn))
        {
            if (despawn.DespawnAt is not { } at || at > curTime)
                continue;

            despawn.DespawnAt = null;

            if (despawn.GibInsteadOfDeleting && HasComp<BodyComponent>(uid))
                _bodySystem.GibBody(uid);
            else
                QueueDel(uid);
        }
    }
}
