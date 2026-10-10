using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Heat fault behaviour: fire on everything inside, hot air around it.
/// </summary>
public sealed class HeatFaultSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    [ValidatePrototypeId<DamageTypePrototype>]
    private const string HeatDamageType = "Heat";

    private EntityQuery<FlammableComponent> _flammableQuery;

    public override void Initialize()
    {
        base.Initialize();

        _flammableQuery = GetEntityQuery<FlammableComponent>();

        SubscribeLocalEvent<HeatFaultComponent, ErrorgateAnomalyTickEvent>(OnTick);
    }

    private void OnTick(Entity<HeatFaultComponent> ent, ref ErrorgateAnomalyTickEvent args)
    {
        if (!TryComp<ErrorgateAnomalyComponent>(ent, out var anomaly))
            return;

        // Heat damage that grows toward the center: a few seconds at the edge are survivable, the middle is not
        var edge = MathF.Max(anomaly.Radius, ent.Comp.InnerRadius + 0.01f);
        var closeness = 1f - Math.Clamp((args.Distance - ent.Comp.InnerRadius) / (edge - ent.Comp.InnerRadius), 0f, 1f);
        var perSecond = MathHelper.Lerp(ent.Comp.OuterDamagePerSecond, ent.Comp.InnerDamagePerSecond, closeness);
        var damage = new DamageSpecifier(_proto.Index<DamageTypePrototype>(HeatDamageType), FixedPoint2.New(perSecond * anomaly.DamageInterval));
        _damageable.TryChangeDamage(args.Target, damage, origin: ent.Owner);

        if (TerminatingOrDeleted(args.Target) || !_flammableQuery.TryComp(args.Target, out var flammable))
            return;

        // Everyone inside burns
        _flammable.AdjustFireStacks(args.Target, ent.Comp.FireStacks, flammable);
        _flammable.Ignite(args.Target, ent.Owner, flammable);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<HeatFaultComponent, ErrorgateAnomalyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var heat, out var anomaly, out var xform))
        {
            if (heat.NextAtmosHeat > curTime)
                continue;

            heat.NextAtmosHeat = curTime + TimeSpan.FromSeconds(1);
            HeatAir(uid, heat, anomaly, xform);
        }
    }

    private void HeatAir(EntityUid uid, HeatFaultComponent heat, ErrorgateAnomalyComponent anomaly, TransformComponent xform)
    {
        // Only grids that simulate their own air. Never the air of the whole map.
        if (xform.GridUid is not { } gridUid
            || !TryComp<GridAtmosphereComponent>(gridUid, out var gridAtmos)
            || !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return;
        }

        TryComp<GasTileOverlayComponent>(gridUid, out var overlay);
        Entity<GridAtmosphereComponent?, GasTileOverlayComponent?>? gridEnt =
            new Entity<GridAtmosphereComponent?, GasTileOverlayComponent?>(gridUid, gridAtmos, overlay);

        var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var reach = (int) MathF.Ceiling(anomaly.Radius);
        var radiusSquared = anomaly.Radius * anomaly.Radius;

        for (var dx = -reach; dx <= reach; dx++)
        {
            for (var dy = -reach; dy <= reach; dy++)
            {
                if (dx * dx + dy * dy > radiusSquared)
                    continue;

                var tile = center + new Vector2i(dx, dy);
                var mixture = _atmos.GetTileMixture(gridEnt, (Entity<MapAtmosphereComponent?>?) null, tile, true);
                if (mixture == null || mixture.Immutable || mixture.TotalMoles <= 0f)
                    continue;

                if (mixture.Temperature < heat.AtmosTemperature)
                    mixture.Temperature = MathF.Min(heat.AtmosTemperature, mixture.Temperature + heat.AtmosHeatPerSecond);
            }
        }
    }
}
