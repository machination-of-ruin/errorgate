using Content.Server.Beam.Components;
using Content.Shared.Audio;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     The shared tick of every world fault: whatever is alive (or was) inside the danger radius takes the fault's
///     damage, then the fault's own system reacts through <see cref="ErrorgateAnomalyTickEvent"/>.
///     Faults are revealed for a while when a creature is hurt by them or when something is thrown into them.
/// </summary>
public sealed class ErrorgateAnomalySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    ///     Loose things slower than this (tiles per second) are lying still and do not reveal a fault.
    /// </summary>
    private const float MovingSpeed = 0.3f;

    private EntityQuery<ThrownItemComponent> _thrownQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<BeamComponent> _beamQuery;
    private EntityQuery<ErrorgateAnomalyComponent> _anomalyQuery;

    private readonly HashSet<Entity<MobStateComponent>> _targets = new();
    private readonly HashSet<Entity<PhysicsComponent>> _loose = new();
    private readonly List<(EntityUid Target, float Distance)> _hit = new();

    public override void Initialize()
    {
        base.Initialize();

        _thrownQuery = GetEntityQuery<ThrownItemComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _beamQuery = GetEntityQuery<BeamComponent>();
        _anomalyQuery = GetEntityQuery<ErrorgateAnomalyComponent>();
    }

    /// <summary>
    ///     Whether the fault is currently shown.
    /// </summary>
    public bool IsRevealed(ErrorgateAnomalyComponent anomaly)
    {
        return anomaly.RevealedUntil is { } until && until > _timing.CurTime;
    }

    /// <summary>
    ///     Shows the fault for its reveal duration. A short sound plays if it was hidden.
    /// </summary>
    public void Reveal(EntityUid uid, ErrorgateAnomalyComponent? anomaly = null)
    {
        if (!Resolve(uid, ref anomaly))
            return;

        var now = _timing.CurTime;
        var until = now + TimeSpan.FromSeconds(anomaly.RevealDuration);
        var wasRevealed = IsRevealed(anomaly);

        // Do not resend the state for every tick of a long stay
        if (wasRevealed && anomaly.RevealedUntil!.Value > until - TimeSpan.FromSeconds(1))
            return;

        anomaly.RevealedUntil = until;
        Dirty(uid, anomaly);

        if (!wasRevealed)
            _audio.PlayPvs(anomaly.RevealSound, uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ErrorgateAnomalyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anomaly, out var xform))
        {
            var center = _transform.GetMapCoordinates(uid, xform);

            UpdateCycle(uid, anomaly, curTime);

            // Thrown things are fast, so this is looked at every update and not only on a damage tick
            CheckLooseThings(uid, anomaly, center, curTime);

            if (anomaly.NextTick > curTime)
                continue;

            anomaly.NextTick = curTime + TimeSpan.FromSeconds(anomaly.DamageInterval);

            // Switched off: harmless
            if (!anomaly.Active)
                continue;

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

            // It hurt something, so it shows
            Reveal(uid, anomaly);
        }
    }

    /// <summary>
    ///     Switches faults with a cycle on and off. The first stretch is of random length, so faults placed together
    ///     do not move in step.
    /// </summary>
    private void UpdateCycle(EntityUid uid, ErrorgateAnomalyComponent anomaly, TimeSpan curTime)
    {
        if (anomaly.ActiveSeconds <= 0f || anomaly.IdleSeconds <= 0f)
            return;

        if (anomaly.NextSwitch == TimeSpan.Zero)
        {
            // Starts on, so a freshly spawned fault always bites at first
            anomaly.NextSwitch = curTime + TimeSpan.FromSeconds(_random.NextFloat(1f, anomaly.ActiveSeconds));
            SetVolume(uid, anomaly);
            return;
        }

        if (anomaly.NextSwitch > curTime)
            return;

        // idle -> primed -> active -> idle
        float length;
        if (anomaly.Active)
        {
            anomaly.Active = false;
            length = anomaly.IdleSeconds;
        }
        else if (anomaly.Primed || anomaly.PrimeSeconds <= 0f)
        {
            anomaly.Primed = false;
            anomaly.Active = true;
            length = anomaly.ActiveSeconds;
        }
        else
        {
            anomaly.Primed = true;
            length = anomaly.PrimeSeconds;
        }


        anomaly.NextSwitch = curTime + TimeSpan.FromSeconds(length * _random.NextFloat(0.75f, 1.25f));
        Dirty(uid, anomaly);
        SetVolume(uid, anomaly);
    }

    private void SetVolume(EntityUid uid, ErrorgateAnomalyComponent anomaly)
    {
        var volume = anomaly.Active
            ? anomaly.ActiveVolume
            : anomaly.Primed ? (anomaly.ActiveVolume + anomaly.IdleVolume) / 2f : anomaly.IdleVolume;
        _ambient.SetVolume(uid, volume);
    }

    /// <summary>
    ///     Reveals the fault when a thrown or otherwise flying object is inside it.
    /// </summary>
    private void CheckLooseThings(EntityUid uid, ErrorgateAnomalyComponent anomaly, MapCoordinates center, TimeSpan curTime)
    {
        // Already freshly revealed
        if (anomaly.RevealedUntil is { } until
            && until > curTime + TimeSpan.FromSeconds(anomaly.RevealDuration - 1))
        {
            return;
        }

        _loose.Clear();
        _lookup.GetEntitiesInRange(center, MathF.Max(anomaly.Radius, anomaly.RevealRadius), _loose, LookupFlags.Dynamic);

        foreach (var (thing, body) in _loose)
        {
            if (thing == uid
                || body.BodyType != BodyType.Dynamic
                || body.CollisionLayer == (int) CollisionGroup.GhostImpassable
                || _mobQuery.HasComp(thing)
                || _beamQuery.HasComp(thing)
                || _anomalyQuery.HasComp(thing))
            {
                continue;
            }

            if (_thrownQuery.HasComp(thing) || body.LinearVelocity.LengthSquared() > MovingSpeed * MovingSpeed)
            {
                Reveal(uid, anomaly);
                return;
            }
        }
    }
}
