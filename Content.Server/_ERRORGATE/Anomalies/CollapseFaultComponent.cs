namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Collapse fault: drags every loose thing toward its center, harder the closer it is. The center crushes
///     (damage from <c>ErrorgateAnomaly</c>), and what is already dead there is torn apart.
/// </summary>
[RegisterComponent]
public sealed partial class CollapseFaultComponent : Component
{
    /// <summary>
    ///     How far the pull reaches, tiles.
    /// </summary>
    [DataField]
    public float PullRange = 8f;

    /// <summary>
    ///     Acceleration (tiles per second squared) at the edge of the pull range.
    /// </summary>
    [DataField]
    public float MinAcceleration = 6f;

    /// <summary>
    ///     Acceleration at the center. Grows with the square of how close the thing is.
    /// </summary>
    [DataField]
    public float MaxAcceleration = 90f;
}
