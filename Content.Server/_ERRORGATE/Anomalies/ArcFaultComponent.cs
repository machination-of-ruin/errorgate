using Robust.Shared.Prototypes;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Arc fault: every few seconds it throws an arc at something near it, and it shocks whatever stands inside it.
/// </summary>
[RegisterComponent]
public sealed partial class ArcFaultComponent : Component
{
    /// <summary>
    ///     How far an arc can reach, tiles.
    /// </summary>
    [DataField]
    public float ArcRange = 8f;

    [DataField]
    public float MinArcInterval = 2f;

    [DataField]
    public float MaxArcInterval = 6f;

    [DataField]
    public EntProtoId ArcPrototype = "Lightning";

    /// <summary>
    ///     Chance that an arc picks a living thing over an object, if both are in reach.
    /// </summary>
    [DataField]
    public float LivingTargetChance = 0.7f;

    /// <summary>
    ///     Shock damage of an arc that hits a living thing.
    /// </summary>
    [DataField]
    public int ArcDamage = 30;

    /// <summary>
    ///     Chance per damage tick that something standing inside the fault is shocked.
    /// </summary>
    [DataField]
    public float InnerShockChance = 0.7f;

    [DataField]
    public int InnerShockDamage = 40;

    /// <summary>
    ///     Seconds a shock stuns for.
    /// </summary>
    [DataField]
    public float ShockTime = 2f;

    [ViewVariables]
    public TimeSpan NextArc;
}
