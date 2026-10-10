using Robust.Shared.Prototypes;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Arc fault: every few seconds it throws an arc at a living thing near it (and only then: it is dark and silent otherwise), and it shocks whatever stands inside it.
///     Nothing beyond <see cref="ArcRange"/> is ever hit, and nobody is shocked twice inside
///     <see cref="ImmunitySeconds"/>, so a shock stuns but never chains into a stunlock.
/// </summary>
[RegisterComponent]
public sealed partial class ArcFaultComponent : Component
{
    /// <summary>
    ///     How far an arc can reach, tiles. This is the whole damage range of the fault.
    /// </summary>
    [DataField]
    public float ArcRange = 4f;

    [DataField]
    public float MinArcInterval = 2f;

    [DataField]
    public float MaxArcInterval = 6f;

    [DataField]
    public EntProtoId ArcPrototype = "ErrorgateArcBolt";

    /// <summary>
    ///     Shock damage of an arc that hits a living thing.
    /// </summary>
    [DataField]
    public int ArcDamage = 45;

    /// <summary>
    ///     Chance per damage tick that something standing inside the fault is shocked.
    /// </summary>
    [DataField]
    public float InnerShockChance = 0.8f;

    [DataField]
    public int InnerShockDamage = 55;

    /// <summary>
    ///     Seconds a shock stuns for. Short on purpose.
    /// </summary>
    [DataField]
    public float ShockTime = 0.6f;

    /// <summary>
    ///     Seconds after a shock in which the victim cannot be shocked by an arc fault again.
    /// </summary>
    [DataField]
    public float ImmunitySeconds = 2.5f;

    [ViewVariables]
    public TimeSpan NextArc;
}

/// <summary>
///     Put on whoever an arc fault just shocked, for a short while.
/// </summary>
[RegisterComponent]
public sealed partial class ArcShockImmunityComponent : Component
{
    [ViewVariables]
    public TimeSpan Until;
}
