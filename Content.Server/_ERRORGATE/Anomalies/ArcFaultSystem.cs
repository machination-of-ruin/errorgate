using System.Numerics;
using Content.Server.Electrocution;
using Content.Server.Lightning;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Mobs.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Arc fault behaviour: random arcs, shocks inside the fault.
/// </summary>
public sealed class ArcFaultSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ElectrocutionSystem _electrocution = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly HashSet<Entity<PhysicsComponent>> _nearby = new();
    private readonly List<EntityUid> _living = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArcFaultComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ArcFaultComponent, ErrorgateAnomalyTickEvent>(OnTick);
    }

    private void OnMapInit(Entity<ArcFaultComponent> ent, ref MapInitEvent args)
    {
        ScheduleArc(ent.Comp);
    }

    private void ScheduleArc(ArcFaultComponent arc)
    {
        var seconds = _random.NextFloat(arc.MinArcInterval, arc.MaxArcInterval);
        arc.NextArc = _timing.CurTime + TimeSpan.FromSeconds(seconds);
    }

    private void OnTick(Entity<ArcFaultComponent> ent, ref ErrorgateAnomalyTickEvent args)
    {
        if (!_random.Prob(ent.Comp.InnerShockChance))
            return;

        Shock(ent.Owner, ent.Comp, args.Target, ent.Comp.InnerShockDamage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ArcFaultComponent>();
        while (query.MoveNext(out var uid, out var arc))
        {
            if (arc.NextArc == TimeSpan.Zero)
                ScheduleArc(arc);

            if (arc.NextArc > curTime)
                continue;

            ScheduleArc(arc);
            FireArc(uid, arc);
        }
    }

    private void Shock(EntityUid uid, ArcFaultComponent arc, EntityUid target, int damage)
    {
        // Whoever was just shocked is left alone for a moment, so shocks cannot be chained
        var now = _timing.CurTime;
        if (TryComp<ArcShockImmunityComponent>(target, out var immunity) && immunity.Until > now)
            return;

        _lightning.ShootLightning(uid, target, arc.ArcPrototype, false);
        if (!_electrocution.TryDoElectrocution(
                target,
                uid,
                damage,
                TimeSpan.FromSeconds(arc.ShockTime),
                true,
                ignoreInsulation: true))
        {
            return;
        }

        EnsureComp<ArcShockImmunityComponent>(target).Until = now + TimeSpan.FromSeconds(arc.ImmunitySeconds);
    }

    /// <summary>
    ///     Throws one arc at a random living thing in reach. With nobody in reach nothing happens at all, the fault
    ///     stays silent and dark.
    /// </summary>
    private void FireArc(EntityUid uid, ArcFaultComponent arc)
    {
        var origin = _transform.GetMapCoordinates(uid);

        _nearby.Clear();
        _living.Clear();
        _lookup.GetEntitiesInRange(origin, arc.ArcRange, _nearby, LookupFlags.Uncontained);

        foreach (var (target, _) in _nearby)
        {
            if (!HasComp<MobStateComponent>(target)
                || (_transform.GetWorldPosition(target) - origin.Position).Length() > arc.ArcRange)
            {
                continue;
            }

            _living.Add(target);
        }

        if (_living.Count == 0)
            return;

        Shock(uid, arc, _random.Pick(_living), arc.ArcDamage);
    }
}
