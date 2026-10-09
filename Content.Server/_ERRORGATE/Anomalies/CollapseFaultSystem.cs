using System.Numerics;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Collapse fault behaviour: a gravity well without the singularity.
/// </summary>
public sealed class CollapseFaultSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private readonly HashSet<EntityUid> _pulled = new();

    public override void Initialize()
    {
        base.Initialize();

        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<CollapseFaultComponent, ErrorgateAnomalyTickEvent>(OnTick);
    }

    private void OnTick(Entity<CollapseFaultComponent> ent, ref ErrorgateAnomalyTickEvent args)
    {
        // The crush already did its damage. What is dead is torn apart.
        if (!_mobState.IsDead(args.Target))
            return;

        if (HasComp<BodyComponent>(args.Target))
            _body.GibBody(args.Target, true);
        else
            QueueDel(args.Target);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CollapseFaultComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var collapse, out var xform))
        {
            Pull(uid, collapse, xform, frameTime);
        }
    }

    private void Pull(EntityUid uid, CollapseFaultComponent collapse, TransformComponent xform, float frameTime)
    {
        var center = _transform.GetMapCoordinates(uid, xform);

        _pulled.Clear();
        _lookup.GetEntitiesInRange(center.MapId, center.Position, collapse.PullRange, _pulled, LookupFlags.Dynamic | LookupFlags.Sundries);

        foreach (var target in _pulled)
        {
            if (target == uid
                || !_physicsQuery.TryComp(target, out var body)
                || body.BodyType != BodyType.Dynamic
                || body.CollisionLayer == (int) CollisionGroup.GhostImpassable
                || HasComp<MapGridComponent>(target)
                || HasComp<MapComponent>(target)
                || HasComp<ErrorgateAnomalyComponent>(target))
            {
                continue;
            }

            var targetXform = Transform(target);
            if (targetXform.Anchored)
                continue;

            var displacement = center.Position - _transform.GetWorldPosition(targetXform);
            var distance = displacement.Length();
            if (distance < 0.05f || distance > collapse.PullRange)
                continue;

            var closeness = 1f - distance / collapse.PullRange;
            var acceleration = MathHelper.Lerp(collapse.MinAcceleration, collapse.MaxAcceleration, closeness * closeness);

            // Same acceleration for everything, light or heavy
            var impulse = displacement / distance * acceleration * body.Mass * frameTime;
            _physics.ApplyLinearImpulse(target, impulse, body: body);
        }
    }
}
