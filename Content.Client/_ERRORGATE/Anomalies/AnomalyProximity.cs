using Content.Shared._ERRORGATE.Anomalies;
using Robust.Client.Player;

namespace Content.Client._ERRORGATE.Anomalies;

/// <summary>
///     How close the local player is to the nearest fault of a kind, for the screen effects.
/// </summary>
internal static class AnomalyProximity
{
    /// <summary>
    ///     0 when no fault of this kind is within its warning radius, up to 1 at its center.
    /// </summary>
    public static float Get(
        IEntityManager entMan,
        SharedTransformSystem xformSys,
        IPlayerManager player,
        ErrorgateAnomalyKind kind)
    {
        if (player.LocalEntity is not { } local || !entMan.TryGetComponent(local, out TransformComponent? localXform))
            return 0f;

        var localPos = xformSys.GetWorldPosition(localXform);
        var best = 0f;

        var query = entMan.EntityQueryEnumerator<ErrorgateAnomalyComponent, TransformComponent>();
        while (query.MoveNext(out _, out var anomaly, out var xform))
        {
            if (anomaly.Kind != kind || anomaly.WarningRadius <= 0f || xform.MapID != localXform.MapID)
                continue;

            var distance = (xformSys.GetWorldPosition(xform) - localPos).Length();
            if (distance >= anomaly.WarningRadius)
                continue;

            best = MathF.Max(best, 1f - distance / anomaly.WarningRadius);
        }

        return best;
    }
}
