using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     The shared tick of every world fault: whatever is alive (or was) inside the danger radius takes the fault's
///     damage, then the fault's own system reacts through <see cref="ErrorgateAnomalyTickEvent"/>.
/// </summary>
public sealed class ErrorgateAnomalySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly HashSet<Entity<MobStateComponent>> _targets = new();
    private readonly List<(EntityUid Target, float Distance)> _hit = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ErrorgateAnomalyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anomaly, out var xform))
        {
            if (anomaly.NextTick > curTime)
                continue;

            anomaly.NextTick = curTime + TimeSpan.FromSeconds(anomaly.DamageInterval);

            var center = _transform.GetMapCoordinates(uid, xform);

            _targets.Clear();
            _hit.Clear();
            _lookup.GetEntitiesInRange(center, anomaly.Radius, _targets, LookupFlags.Uncontained);

            foreach (var target in _targets)
            {
                var distance = (_transform.GetWorldPosition(target) - center.Position).Length();
                if (distance <= anomaly.Radius)
                    _hit.Add((target, distance));
            }

            if (_hit.Count == 0)
                continue;

            _audio.PlayPvs(anomaly.TickSound, uid);

            // Copy first: the events below may delete or gib the targets
            foreach (var (target, distance) in _hit)
            {
                if (TerminatingOrDeleted(target))
                    continue;

                if (!anomaly.Damage.Empty)
                    _damageable.TryChangeDamage(target, anomaly.Damage, origin: uid);

                if (TerminatingOrDeleted(target))
                    continue;

                var ev = new ErrorgateAnomalyTickEvent(target, distance);
                RaiseLocalEvent(uid, ref ev);
            }
        }
    }
}
