using System.Numerics;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.NPC.Squad;

/// <summary>
///     Squads and the world faults (<c>docs/design/anomalies.md</c>). A member that comes within the warning range of a fault is likely
///     to spot it (it says so, and the whole squad knows it from then on); a member that is hurt by one learns of it at once. A squad
///     does not choose goals, legs or formation spots inside or across the danger zone of a fault it knows, and steps out of one.
/// </summary>
public sealed class SquadFaultSystem : EntitySystem
{
    /// <summary>How far past the danger radius the squad keeps away.</summary>
    private const float Margin = 1.5f;

    /// <summary>How far a member sees a fault, at most (the screen effect radius of the faults is about this).</summary>
    private const float SpotRange = 8f;

    /// <summary>The chance per second that a member within <see cref="SpotRange"/> notices a fault it does not know yet.</summary>
    private const float SpotChance = 0.5f;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly NpcSpeechSystem _speech = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcSquadMemberComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnDamaged(EntityUid uid, NpcSquadMemberComponent member, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } origin || !HasComp<ErrorgateAnomalyComponent>(origin)
            || !TryComp<SquadComponent>(member.Squad, out var squad))
            return;

        Learn((member.Squad, squad), origin, uid, "faulthurt");
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        var query = EntityQueryEnumerator<SquadComponent>();
        while (query.MoveNext(out var uid, out var squad))
        {
            squad.KnownFaults.RemoveWhere(f => !Exists(f) || TerminatingOrDeleted(f));
            foreach (var member in squad.Members)
            {
                if (!TryComp<MobStateComponent>(member, out var state) || state.CurrentState != MobState.Alive)
                    continue;

                var coords = _transform.GetMapCoordinates(member);
                foreach (var fault in _lookup.GetEntitiesInRange<ErrorgateAnomalyComponent>(coords, SpotRange))
                {
                    if (squad.KnownFaults.Contains(fault.Owner))
                        continue;

                    var distance = (_transform.GetWorldPosition(fault) - coords.Position).Length();
                    if (distance <= fault.Comp.EffectiveDanger + 1f || _random.Prob(SpotChance))
                        Learn((uid, squad), fault.Owner, member, "fault");
                }
            }
        }
    }

    /// <summary>The squad knows the fault from now on; <paramref name="by"/> says so.</summary>
    public void Learn(Entity<SquadComponent> squad, EntityUid fault, EntityUid by, string category)
    {
        if (!squad.Comp.KnownFaults.Add(fault))
            return;

        // The way it was going may lead through it
        squad.Comp.Leg = null;
        _speech.TrySay(by, category, 1f);
    }

    private IEnumerable<(Vector2 Position, float Radius)> Zones(SquadComponent squad, MapId map)
    {
        foreach (var fault in squad.KnownFaults)
        {
            if (!TryComp<ErrorgateAnomalyComponent>(fault, out var anomaly))
                continue;

            var coords = _transform.GetMapCoordinates(fault);
            if (coords.MapId == map)
                yield return (coords.Position, anomaly.EffectiveDanger + Margin);
        }
    }

    /// <summary>Whether a spot is inside the danger zone of a fault the squad knows.</summary>
    public bool InZone(SquadComponent squad, MapCoordinates point)
    {
        foreach (var (position, radius) in Zones(squad, point.MapId))
        {
            if ((point.Position - position).Length() < radius)
                return true;
        }

        return false;
    }

    /// <summary>Whether the straight way between two spots goes through the danger zone of a fault the squad knows.</summary>
    public bool Blocked(SquadComponent squad, Vector2 from, Vector2 to, MapId map)
    {
        var line = to - from;
        var lengthSquared = line.LengthSquared();
        foreach (var (position, radius) in Zones(squad, map))
        {
            var t = lengthSquared < 0.01f ? 0f : Math.Clamp(Vector2.Dot(position - from, line) / lengthSquared, 0f, 1f);
            if ((from + line * t - position).Length() < radius)
                return true;
        }

        return false;
    }

    /// <summary>A spot out of the danger zone the point is in, straight away from its fault; null if the point is in none.</summary>
    public Vector2? Outside(SquadComponent squad, MapCoordinates point)
    {
        foreach (var (position, radius) in Zones(squad, point.MapId))
        {
            var offset = point.Position - position;
            if (offset.Length() >= radius)
                continue;

            var direction = offset.LengthSquared() < 0.01f ? Vector2.UnitX : Vector2.Normalize(offset);
            return position + direction * (radius + 1f);
        }

        return null;
    }
}
