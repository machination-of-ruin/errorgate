using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Campfire;

/// <summary>Rest by a campfire and it mends you, slowly (every type of brute, burn and poison, a point each every few seconds).</summary>
public sealed class CampfireWarmthSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<CampfireWarmthComponent, TransformComponent>();
        while (query.MoveNext(out _, out var fire, out var xform))
        {
            if (curTime < fire.NextHeal)
                continue;

            fire.NextHeal = curTime + TimeSpan.FromSeconds(fire.IntervalSeconds);
            var heal = new DamageSpecifier();
            foreach (var type in fire.Types)
            {
                heal.DamageDict[type] = FixedPoint2.New(-fire.Heal);
            }

            foreach (var near in _lookup.GetEntitiesInRange<DamageableComponent>(xform.Coordinates, fire.Range))
            {
                if (!HasComp<MobStateComponent>(near) || _mobState.IsDead(near))
                    continue;

                var sitting = _standing.IsDown(near.Owner) || (TryComp<BuckleComponent>(near, out var buckle) && buckle.Buckled);
                if (!sitting)
                    continue;

                _damageable.TryChangeDamage(near.Owner, heal, true);
            }
        }
    }
}
