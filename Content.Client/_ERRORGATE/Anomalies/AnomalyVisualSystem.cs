using Content.Shared._ERRORGATE.Anomalies;
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

    private const float FadeIn = 0.5f;
    private const float FadeOut = 2f;

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
