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
    ///     How far the whole fault can hurt, if that is more than <see cref="Radius"/> (an arc reaches further than
    ///     the zone it shocks inside of all the time). Placement keeps this much room around every fault.
    ///     Zero means <see cref="Radius"/>.
    /// </summary>
    [DataField]
    public float DangerRadius;

    /// <summary>
    ///     Things flying inside this distance reveal the fault, if that is more than <see cref="Radius"/>.
    /// </summary>
    [DataField]
    public float RevealRadius;

    /// <summary>
    ///     The distance the fault can hurt from.
    /// </summary>
    public float EffectiveDanger => MathF.Max(Radius, DangerRadius);

    /// <summary>
    ///     Distance at which the client starts to show the fault on the screen (static, desaturation, haze).
    ///     Zero means the fault has no screen effect. It is meant to be at least the danger radius, and at most the
    ///     throwing range (about eight tiles), so a careful player sees it, and can test it with a thrown item,
    ///     before it can hurt.
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

    /// <summary>
    ///     Seconds the fault stays switched on (pulling, hurting), then <see cref="IdleSeconds"/> off, and so on. Zero means always on.
    ///     While it is off it does no damage and (for a collapse) does not pull.
    /// </summary>
    [DataField]
    public float ActiveSeconds;

    [DataField]
    public float IdleSeconds;

    /// <summary>
    ///     Seconds the fault is primed before it switches on: awake and faintly visible, but harmless. Zero skips it.
    /// </summary>
    [DataField]
    public float PrimeSeconds;

    /// <summary>
    ///     Primed, see <see cref="PrimeSeconds"/>. Never true together with <see cref="Active"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Primed;

    /// <summary>
    ///     Whether the fault is switched on right now. Always true for faults without a cycle.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Active = true;

    [ViewVariables]
    public TimeSpan NextSwitch;

    /// <summary>
    ///     Ambient sound volume (dB) while on / off, only used by faults with a cycle.
    /// </summary>
    [DataField]
    public float ActiveVolume = -8f;

    [DataField]
    public float IdleVolume = -16f;

    /// <summary>
    ///     While the game time is before this, the fault is revealed: its sprite and light show clearly.
    ///     Set when something is thrown into it or when it hurts a creature.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? RevealedUntil;

    /// <summary>
    ///     Seconds a reveal lasts.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float RevealDuration = 8f;

    /// <summary>
    ///     Played at the fault when it goes from hidden to revealed.
    /// </summary>
    [DataField]
    public SoundSpecifier? RevealSound;

    /// <summary>
    ///     Opacity of the sprite while hidden (0 to 1), the sprite layers hold the revealed look.
    /// </summary>
    [DataField]
    public float RestAlpha = 0.05f;

    /// <summary>
    ///     Light while hidden. The client flickers it a little.
    /// </summary>
    [DataField]
    public float RestLightEnergy = 0.4f;

    [DataField]
    public float RestLightRadius = 1.5f;

    /// <summary>
    ///     Light while revealed.
    /// </summary>
    [DataField]
    public float RevealedLightEnergy = 2f;

    [DataField]
    public float RevealedLightRadius = 4f;
}

/// <summary>
///     Raised on a fault for every living entity inside its <see cref="ErrorgateAnomalyComponent.Radius"/>,
///     right after the common damage was applied.
/// </summary>
[ByRefEvent]
public readonly record struct ErrorgateAnomalyTickEvent(EntityUid Target, float Distance);
