namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Heat fault: sets whatever stands in it on fire and keeps the air around it hot.
///     The damage itself comes from <c>ErrorgateAnomaly</c>.
/// </summary>
[RegisterComponent]
public sealed partial class HeatFaultComponent : Component
{
    /// <summary>
    ///     Heat damage per second at the edge of the fault.
    /// </summary>
    [DataField]
    public float OuterDamagePerSecond = 6f;

    /// <summary>
    ///     Heat damage per second inside <see cref="InnerRadius"/>. In between it grows with the closeness.
    /// </summary>
    [DataField]
    public float InnerDamagePerSecond = 30f;

    [DataField]
    public float InnerRadius = 2.5f;

    /// <summary>
    ///     Fire stacks added to every living thing inside the fault on each tick.
    /// </summary>
    [DataField]
    public float FireStacks = 2f;

    /// <summary>
    ///     The air in the danger radius is heated up to this temperature (Kelvin), if the grid has simulated air.
    ///     Clients show a haze over hot air tiles on their own.
    /// </summary>
    [DataField]
    public float AtmosTemperature = 650f;

    /// <summary>
    ///     How much the air warms per second, Kelvin.
    /// </summary>
    [DataField]
    public float AtmosHeatPerSecond = 150f;

    [ViewVariables]
    public TimeSpan NextAtmosHeat;
}
