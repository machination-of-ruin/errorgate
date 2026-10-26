using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>Added to NPCs that never go hungry or thirsty (see <see cref="NpcNeedsSystem"/>).</summary>
[RegisterComponent]
public sealed partial class NpcNeedsComponent : Component
{
    public TimeSpan Next;
}

/// <summary>Soldiers and bandits have no way to eat: their hunger and thirst are kept at the "okay" level so they never starve.</summary>
public sealed class NpcNeedsSystem : EntitySystem
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly ThirstSystem _thirst = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<NpcNeedsComponent>();
        while (query.MoveNext(out var uid, out var needs))
        {
            if (needs.Next > now)
                continue;

            needs.Next = now + Interval;
            if (TryComp<HungerComponent>(uid, out var hunger))
                _hunger.SetHunger(uid, hunger.Thresholds[HungerThreshold.Okay], hunger);

            if (TryComp<ThirstComponent>(uid, out var thirst))
                _thirst.SetThirst(uid, thirst, thirst.ThirstThresholds[ThirstThreshold.Okay]);
        }
    }
}
