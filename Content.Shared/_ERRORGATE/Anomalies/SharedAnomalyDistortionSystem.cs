using Content.Shared.Singularity.Components;

namespace Content.Shared._ERRORGATE.Anomalies;

/// <summary>
///     Lets the world faults change the strength of their <see cref="SingularityDistortionComponent"/>, whose fields are
///     otherwise reserved for the singularity system.
/// </summary>
public sealed class SharedAnomalyDistortionSystem : EntitySystem
{
    public void SetDistortion(EntityUid uid, float intensity, float falloffPower, SingularityDistortionComponent? distortion = null)
    {
        if (!Resolve(uid, ref distortion, false))
            return;

        distortion.Intensity = intensity;
        distortion.FalloffPower = falloffPower;
    }
}
