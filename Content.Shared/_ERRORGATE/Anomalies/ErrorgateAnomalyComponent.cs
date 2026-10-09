using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._ERRORGATE.Anomalies;

/// <summary>
///     Which fault an anomaly is. Clients pick their screen effect from this.
/// </summary>
[Serializable, NetSerializable]
public enum ErrorgateAnomalyKind : byte
{
    /// <summary>Heat haze, burns whatever stands in it.</summary>
    Heat,

    /// <summary>Discharge without a circuit, arcs to whatever is near.</summary>
    Arc,

    /// <summary>Gravity error, drags everything inward and crushes the center.</summary>
    Collapse,
}

/// <summary>
///     Common part of every world fault (STALKER-style environmental hazard): a danger radius, a damage tick and
///     a hint distance for client side effects. The behaviour itself lives in the per-kind components
///     (<c>HeatFault</c>, <c>ArcFault</c>, <c>CollapseFault</c>), which react to <see cref="ErrorgateAnomalyTickEvent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ErrorgateAnomalyComponent : Component
{
    [DataField, AutoNetworkedField]
    public ErrorgateAnomalyKind Kind;

    /// <summary>
    ///     Anything alive inside this distance (tiles) is hit by every damage tick.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Radius = 3f;

    /// <summary>
    ///     Distance at which the client starts to show the fault on the screen (static, desaturation).
    ///     Zero means the fault has no screen effect.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float WarningRadius;

    /// <summary>
    ///     Applied to every living thing inside <see cref="Radius"/> on each tick. May be empty when the kind
    ///     deals its damage another way (arc faults electrocute).
    /// </summary>
    [DataField]
    public DamageSpecifier Damage = new();

    /// <summary>
    ///     Seconds between damage ticks.
    /// </summary>
    [DataField]
    public float DamageInterval = 0.5f;

    /// <summary>
    ///     Played at the fault every tick something is inside it.
    /// </summary>
    [DataField]
    public SoundSpecifier? TickSound;

    [ViewVariables]
    public TimeSpan NextTick;
}

/// <summary>
///     Raised on a fault for every living entity inside its <see cref="ErrorgateAnomalyComponent.Radius"/>,
///     right after the common damage was applied.
/// </summary>
[ByRefEvent]
public readonly record struct ErrorgateAnomalyTickEvent(EntityUid Target, float Distance);
