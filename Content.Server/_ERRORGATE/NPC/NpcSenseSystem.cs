using Content.Server.GameTicking;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>A gun fired, a fight: heard around for a while (see <see cref="NpcSenseSystem"/>).</summary>
[RegisterComponent]
public sealed partial class NpcNoiseComponent : Component
{
    public TimeSpan Until;
    public float Radius;
}

/// <summary>
///     What an NPC can make out in the dark. A target is sensed when it is close (a few steps: felt more than seen), when it is lit
///     (a lamp, a flashlight, a fire near it, in the NPC's range), when it made a noise (a shot) within the hearing range, or, unlit,
///     within a share of the range that follows the daylight of its map (none at night). The light is an estimate from the day cycle of
///     the map and the enabled lights near the target; the server has no real light map.
/// </summary>
public sealed class NpcSenseSystem : EntitySystem
{
    /// <summary>Anything this close is sensed whatever the light.</summary>
    private const float FeelRange = 2.5f;

    private const float ShotRadius = 30f;
    private static readonly TimeSpan ShotMemory = TimeSpan.FromSeconds(8);

    /// <summary>Lights further than this from the target are not looked at.</summary>
    private const float LightSearch = 12f;

    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunShotEvent>(OnShot);
    }

    private void OnShot(ref GunShotEvent args)
    {
        var noise = EnsureComp<NpcNoiseComponent>(args.User);
        noise.Until = _timing.CurTime + ShotMemory;
        noise.Radius = ShotRadius;
    }

    /// <summary>The brightness of the sky of a map from 0 (night) to about 0.5 (day), from its day cycle if it has one.</summary>
    public float Daylight(MapId mapId)
    {
        var mapUid = _map.GetMapOrInvalid(mapId);
        if (!mapUid.IsValid() || !TryComp<MapLightComponent>(mapUid, out var light))
            return 0.5f;

        var color = light.AmbientLightColor;
        if (TryComp<LightCycleComponent>(mapUid, out var cycle) && cycle.Enabled)
        {
            var time = (float) (_timing.CurTime + cycle.Offset - _ticker.RoundStartTimeSpan - _meta.GetPauseTime(mapUid)).TotalSeconds;
            color = SharedLightCycleSystem.GetColor(new Entity<LightCycleComponent>(mapUid, cycle), cycle.OriginalColor, time);
        }

        return 0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B;
    }

    /// <summary>True when the sky is dim enough for an NPC to want a flashlight: dusk and night.</summary>
    public bool IsDim(MapId mapId) => Daylight(mapId) < 0.30f;

    /// <summary>Whether an enabled light near the target reaches it.</summary>
    public bool IsLit(EntityUid target)
    {
        var coords = _transform.GetMapCoordinates(target);
        foreach (var light in _lookup.GetEntitiesInRange<PointLightComponent>(coords, LightSearch))
        {
            if (!light.Comp.Enabled || light.Comp.Energy < 0.1f)
                continue;

            var distance = (_transform.GetWorldPosition(light) - coords.Position).Length();
            if (distance <= light.Comp.Radius * 0.8f)
                return true;
        }

        return false;
    }

    /// <summary>Whether <paramref name="npc"/> can make out <paramref name="target"/>, a target at most <paramref name="range"/> away.</summary>
    public bool CanSense(EntityUid npc, EntityUid target, float range)
    {
        var from = _transform.GetMapCoordinates(npc);
        var to = _transform.GetMapCoordinates(target);
        if (from.MapId != to.MapId)
            return false;

        var distance = (to.Position - from.Position).Length();
        if (distance <= FeelRange)
            return true;

        if (TryComp<NpcNoiseComponent>(target, out var noise) && noise.Until > _timing.CurTime && distance <= noise.Radius)
            return true;

        if (distance > range)
            return false;

        if (IsLit(target))
            return true;

        // Unlit: as far as the sky allows
        var share = Math.Clamp((Daylight(to.MapId) - 0.08f) / 0.30f, 0f, 1f);
        return distance <= range * share;
    }
}
