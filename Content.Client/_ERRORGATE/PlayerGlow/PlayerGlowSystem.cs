using Content.Shared._ERRORGATE.PlayerGlow;
using Content.Shared.Ghost;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Content.Shared.Physics;
using Robust.Shared.Physics.Systems;
using System.Numerics;

namespace Content.Client._ERRORGATE.PlayerGlow;

/// <summary>
///     A faint light around your own character, so total darkness never takes you from yourself. The light is an entity that exists
///     only on this client, so it is never networked: nobody else sees it, and it does not reveal you to anyone. See
///     docs/design/VIBE.md (darkness is real) and progression.md (light tiers): this is a floor of visibility, not a light source.
///
///     The engine only adds light, it cannot say "at least this bright". So the glow measures how bright the place already is at the
///     character (map light plus the lamps in reach, worked out the way the light shader does, walls ignored) and only gives what is
///     missing up to <see cref="PlayerGlowComponent.Floor"/>.
/// </summary>
public sealed class PlayerGlowSystem : EntitySystem
{
    private const string GlowPrototype = "ErrorgatePlayerGlow";

    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PointLightSystem _lights = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    private readonly HashSet<Entity<PointLightComponent>> _nearby = new();
    private EntityUid? _glow;

    public override void Initialize()
    {
        base.Initialize();

        _player.LocalPlayerAttached += OnAttached;
        _player.LocalPlayerDetached += OnDetached;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _player.LocalPlayerAttached -= OnAttached;
        _player.LocalPlayerDetached -= OnDetached;
        RemoveGlow();
    }

    private void OnAttached(EntityUid player)
    {
        RemoveGlow();

        // A ghost has its own light and its own way to see
        if (HasComp<GhostComponent>(player))
            return;

        var glow = Spawn(GlowPrototype, Transform(player).Coordinates);
        _transform.SetParent(glow, player);
        _glow = glow;
    }

    private void OnDetached(EntityUid player) => RemoveGlow();

    private void RemoveGlow()
    {
        if (_glow is { } glow)
            Del(glow);

        _glow = null;
    }

    public override void FrameUpdate(float frameTime)
    {
        if (_glow is not { } glow || !TryComp(glow, out PlayerGlowComponent? comp) || !TryComp(glow, out PointLightComponent? light))
            return;

        comp.SinceSample += frameTime;
        if (comp.SinceSample >= comp.SampleInterval)
        {
            comp.SinceSample = 0f;
            comp.TargetEnergy = GlowEnergy(glow, comp, light);
        }

        // Follow the measurement: slowly up into the dark, quickly down into the light
        var smoothing = comp.TargetEnergy > comp.Energy ? comp.SmoothingUp : comp.SmoothingDown;
        comp.Energy += (comp.TargetEnergy - comp.Energy) * (1f - MathF.Exp(-smoothing * frameTime));
        _lights.SetEnergy(glow, comp.Energy, light);
        _lights.SetEnabled(glow, comp.Energy > 0.005f, light);
    }

    /// <summary>The energy the glow needs so that the place at the character reaches the floor, from what is already there.</summary>
    private float GlowEnergy(EntityUid glow, PlayerGlowComponent comp, PointLightComponent light)
    {
        var position = _transform.GetMapCoordinates(glow);
        var already = AmbientBrightness(position.MapId);

        _nearby.Clear();
        _lookup.GetEntitiesInRange(position, comp.LampReach, _nearby, LookupFlags.All);
        foreach (var (uid, lamp) in _nearby)
        {
            if (uid == glow || !lamp.Enabled || lamp.Energy <= 0f || lamp.Radius <= 0f)
                continue;

            var lampPosition = _transform.GetWorldPosition(uid);
            var diff = lampPosition - position.Position;
            var brightness = LampBrightness(lamp, diff.LengthSquared(), lamp.Energy);

            // A lamp that casts shadows does not light through a wall, so it does not count
            var blocked = brightness > 0.001f && lamp.CastShadows && Blocked(position.MapId, lampPosition, position.Position);
            if (blocked)
                continue;

            already += brightness;
        }

        var missing = comp.Floor - already;
        if (missing <= 0f)
            return 0f;

        // What the glow itself gives at its own centre per point of energy (the same shape the light shader draws)
        var perEnergy = LampBrightness(light, 0f, 1f);
        return perEnergy > 0f ? missing / perEnergy : 0f;
    }

    /// <summary>Whether something opaque (a wall, a closed door) stands between a lamp and the character.</summary>
    private bool Blocked(MapId map, Vector2 lamp, Vector2 character)
    {
        // Wall lamps sit in or on the wall: start the ray a little away from the lamp so it does not hit its own wall
        const float skip = 0.6f;
        var toCharacter = character - lamp;
        var distance = toCharacter.Length();
        if (distance <= skip)
            return false;

        var direction = toCharacter / distance;
        var ray = new CollisionRay(lamp + direction * skip, direction, (int) CollisionGroup.Opaque);
        // Only what the engine's own shadows use stands in the way: an occluder that is on (a wall, a closed door). Bodies of mobs and the
        // lamp itself have opaque fixtures too, but they do not block light.
        foreach (var _ in _physics.IntersectRayWithPredicate(map, ray, distance - skip, e => !TryComp(e, out OccluderComponent? occluder) || !occluder.Enabled, returnOnFirstHit: true))
        {
            return true;
        }

        return false;
    }

    private float AmbientBrightness(MapId map)
    {
        if (map == MapId.Nullspace || !TryComp(_map.GetMapOrInvalid(map), out MapLightComponent? mapLight))
            return 0f;

        return Luma(mapLight.AmbientLightColor);
    }

    /// <summary>
    ///     How much a lamp adds at a distance, as in light_shared.swsl: (1 - d / range) / sqrt(d + 1), with d the squared distance plus
    ///     the height of the light, times energy and the brightness of its colour.
    /// </summary>
    private static float LampBrightness(PointLightComponent lamp, float squaredDistance, float energy)
    {
        var d = squaredDistance + 1f;
        var falloff = 1f - MathF.Min(MathF.Sqrt(d) / lamp.Radius, 1f);
        var value = falloff / MathF.Sqrt(d + 1f) * energy;
        return MathF.Min(value, 1f) * Luma(lamp.Color);
    }

    private static float Luma(Color color) => 0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B;
}
