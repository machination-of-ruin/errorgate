using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Singularity.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._ERRORGATE.Anomalies;

/// <summary>
///     Shows or hides the world faults. A hidden fault is a barely visible shimmer with a faint light, a revealed one
///     (<see cref="ErrorgateAnomalyComponent.RevealedUntil"/>) fades in, pulses and fades out again.
/// </summary>
public sealed class AnomalyVisualSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedAnomalyDistortionSystem _distortion = default!;

    private const float FadeIn = 0.5f;
    private const float FadeOut = 2f;

    // Lens of a collapse fault: the quiet look, and the look while it pulls (the lens then covers the pull area)
    private const float IdleIntensity = 3000f;
    private const float IdleFalloff = 2.7f;
    private const float ActiveIntensity = 12000f;
    private const float ActiveFalloff = 2.2f;
    private const float SwitchSpeed = 2.5f;

    // How switched on a fault with a cycle looks right now (0 to 1)
    private readonly Dictionary<EntityUid, float> _switched = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ErrorgateAnomalyComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<ErrorgateAnomalyComponent> ent, ref ComponentShutdown args)
    {
        _switched.Remove(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var real = (float) _timing.RealTime.TotalSeconds;

        var query = EntityQueryEnumerator<ErrorgateAnomalyComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var anomaly, out var sprite))
        {
            var phase = uid.Id * 1.7f;
            var reveal = 0f;
            var pulse = 1f;

            if (anomaly.RevealedUntil is { } until && until > now)
            {
                var remaining = (float) (until - now).TotalSeconds;
                var elapsed = anomaly.RevealDuration - remaining;
                reveal = Math.Clamp(Math.Min(elapsed / FadeIn, remaining / FadeOut), 0f, 1f);
                pulse = 1f + 0.15f * MathF.Sin(real * 4f + phase);
            }

            var cycle = 1f;
            if (anomaly.ActiveSeconds > 0f)
            {
                _switched.TryGetValue(uid, out cycle);
                cycle += ((anomaly.Active ? 1f : 0f) - cycle) * MathF.Min(1f, SwitchSpeed * frameTime);
                _switched[uid] = cycle;

                // A pulling fault shows more than a quiet one
                reveal = MathF.Max(reveal, 0.4f * cycle);

                if (TryComp(uid, out SingularityDistortionComponent? lens))
                {
                    _distortion.SetDistortion(
                        uid,
                        MathHelper.Lerp(IdleIntensity, ActiveIntensity, cycle),
                        MathHelper.Lerp(IdleFalloff, ActiveFalloff, cycle),
                        lens);
                }
            }

            var alpha = Math.Clamp(MathHelper.Lerp(anomaly.RestAlpha, 1f, reveal) * pulse, 0f, 1f);
            _sprite.SetColor((uid, sprite), Color.White.WithAlpha(alpha));

            if (!TryComp(uid, out PointLightComponent? light))
                continue;

            // A faint, slow flicker. Never a strobe.
            var flicker = 1f + 0.1f * MathF.Sin(real * 1.7f + phase) + 0.05f * MathF.Sin(real * 4.3f + phase * 2f);
            var energy = MathHelper.Lerp(anomaly.RestLightEnergy, anomaly.RevealedLightEnergy, reveal) * flicker;
            var radius = MathHelper.Lerp(anomaly.RestLightRadius, anomaly.RevealedLightRadius, reveal);

            _light.SetEnergy(uid, energy, light);
            _light.SetRadius(uid, radius, light);
        }
    }
}
