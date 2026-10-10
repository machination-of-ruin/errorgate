using System.Numerics;
using Content.Server.Electrocution;
using Content.Server.Lightning;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Mobs.Components;
using Content.Shared.Throwing;
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
    [Dependency] private readonly ErrorgateAnomalySystem _anomaly = default!;
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

        SubscribeLocalEvent<ArcFaultComponent, ErrorgateAnomalyTickEvent>(OnTick);
    }

    private void OnTick(Entity<ArcFaultComponent> ent, ref ErrorgateAnomalyTickEvent args)
    {
        // Charged only
        if (ent.Comp.NextArc > _timing.CurTime || !_random.Prob(ent.Comp.InnerShockChance))
            return;

        if (Shock(ent.Owner, ent.Comp, args.Target, ent.Comp.InnerShockDamage))
            Discharged(ent.Owner, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ArcFaultComponent>();
        while (query.MoveNext(out var uid, out var arc))
        {
            if (arc.NextArc > curTime || arc.NextScan > curTime)
                continue;

            arc.NextScan = curTime + TimeSpan.FromSeconds(arc.ScanInterval);
            FireArc(uid, arc);
        }
    }

    private void Discharged(EntityUid uid, ArcFaultComponent arc)
    {
        arc.NextArc = _timing.CurTime + TimeSpan.FromSeconds(arc.CooldownSeconds);
        _anomaly.Reveal(uid);
    }

    private bool Shock(EntityUid uid, ArcFaultComponent arc, EntityUid target, int damage)
    {
        // Whoever was just shocked is left alone for a moment, so shocks cannot be chained
        var now = _timing.CurTime;
        if (TryComp<ArcShockImmunityComponent>(target, out var immunity) && immunity.Until > now)
            return false;

        _lightning.ShootLightning(uid, target, arc.ArcPrototype, false);
        if (!_electrocution.TryDoElectrocution(
                target,
                uid,
                damage,
                TimeSpan.FromSeconds(arc.ShockTime),
                true,
                ignoreInsulation: true))
        {
            return false;
        }

        EnsureComp<ArcShockImmunityComponent>(target).Until = now + TimeSpan.FromSeconds(arc.ImmunitySeconds);
        return true;
    }

    /// <summary>
    ///     Throws one arc at a random living thing in reach, or at a thrown object when there is no one: that is how a
    ///     careful player sets it off and gets across while it recharges. With nothing in reach nothing happens at all,
    ///     the fault stays silent and dark.
    /// </summary>
    private void FireArc(EntityUid uid, ArcFaultComponent arc)
    {
        var origin = _transform.GetMapCoordinates(uid);
        var now = _timing.CurTime;

        _nearby.Clear();
        _living.Clear();
        _lookup.GetEntitiesInRange(origin, arc.ArcRange, _nearby, LookupFlags.Uncontained);

        foreach (var (target, _) in _nearby)
        {
            if (!HasComp<MobStateComponent>(target)
                || (_transform.GetWorldPosition(target) - origin.Position).Length() > arc.ArcRange
                || TryComp<ArcShockImmunityComponent>(target, out var immunity) && immunity.Until > now)
            {
                continue;
            }

            _living.Add(target);
        }

        if (_living.Count > 0)
        {
            if (Shock(uid, arc, _random.Pick(_living), arc.ArcDamage))
                Discharged(uid, arc);

            return;
        }

        var thrown = EntityQueryEnumerator<ThrownItemComponent, TransformComponent>();
        while (thrown.MoveNext(out var item, out _, out var itemXform))
        {
            if (itemXform.MapID != origin.MapId
                || (_transform.GetWorldPosition(itemXform) - origin.Position).Length() > arc.ArcRange)
            {
                continue;
            }

            // Only a bolt, nothing is hurt. The fault is spent.
            _lightning.ShootLightning(uid, item, arc.ArcPrototype, false);
            Discharged(uid, arc);
            return;
        }
    }
}
