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
    ///     Acceleration (tiles per second squared) at the edge of the pull range, for loose objects.
    /// </summary>
    [DataField]
    public float MinAcceleration = 3f;

    /// <summary>
    ///     Acceleration at the center. Grows with the square of how close the thing is.
    /// </summary>
    [DataField]
    public float MaxAcceleration = 90f;

    /// <summary>
    ///     Creatures are not pushed with forces (their movement and friction cancel it), they are moved directly.
    ///     Speed (tiles per second) of that pull at the edge of the range.
    /// </summary>
    [DataField]
    public float MobMinSpeed = 0.8f;

    /// <summary>
    ///     Speed of the pull on creatures at the center. The pull is weak in the outer ring (about 1 tile per second at
    ///     six tiles), so it can be felt long before it holds anyone. A walker (3.5) cannot leave the inner three or four
    ///     tiles, a sprinter (5) gets out from further in.
    /// </summary>
    [DataField]
    public float MobMaxSpeed = 5.5f;
}
